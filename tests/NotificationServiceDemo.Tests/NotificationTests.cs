using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NotificationServiceDemo.Api.Data;
using NotificationServiceDemo.Api.Models;
using NotificationServiceDemo.Api.Services;
using Xunit;

namespace NotificationServiceDemo.Tests;

/// Each test gets its own app instance and SQLite file, with the background workers switched off,
/// so the tests call the processor directly and control "now".
public class TestApp : WebApplicationFactory<Program>
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"notification-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = $"Data Source={_db}"
        }));
        builder.ConfigureServices(s =>
        {
            s.PostConfigure<DispatcherOptions>(o => o.Enabled = false);
            s.PostConfigure<SummaryOptions>(o => o.Enabled = false);
        });
    }

    public async Task<Guid> SeedAsync(string message, string status = "Queued")
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var n = new Notification { Recipient = "test@example.com", Channel = "email", Message = message,
                                   Status = Enum.Parse<NotificationStatus>(status) };
        db.Notifications.Add(n);
        await db.SaveChangesAsync();
        return n.Id;
    }

    public async Task<int> ProcessAsync(DateTime nowUtc)
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationProcessor>().ProcessDueAsync(nowUtc);
    }

    public async Task<Notification> LoadAsync(Guid id)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Notifications.FindAsync(id) ?? throw new InvalidOperationException("Notification not found");
    }
}

public class ApiTests
{
    [Fact]
    public async Task Valid_request_is_queued_with_202()
    {
        using var app = new TestApp();
        var client = app.CreateClient();

        var res = await client.PostAsJsonAsync("/api/notifications",
            new { recipient = "+911234567890", channel = "WhatsApp", message = "Your report is ready" });
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Queued", body.GetProperty("status").GetString());
        Assert.Equal("whatsapp", body.GetProperty("channel").GetString());

        var id = body.GetProperty("id").GetGuid();
        var get = await client.GetFromJsonAsync<JsonElement>($"/api/notifications/{id}");
        Assert.Equal("Queued", get.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("", "email", "hello")]
    [InlineData("a@b.com", "sms", "hello")]
    [InlineData("a@b.com", "email", "")]
    public async Task Invalid_requests_return_400(string recipient, string channel, string message)
    {
        using var app = new TestApp();
        var res = await app.CreateClient().PostAsJsonAsync("/api/notifications", new { recipient, channel, message });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Unknown_id_returns_404()
    {
        using var app = new TestApp();
        var res = await app.CreateClient().GetAsync($"/api/notifications/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}

public class ProcessorTests
{
    [Fact]
    public async Task Due_notification_is_sent()
    {
        using var app = new TestApp();
        var id = await app.SeedAsync("hello");

        var processed = await app.ProcessAsync(DateTime.UtcNow.AddMinutes(1));

        var n = await app.LoadAsync(id);
        Assert.Equal(1, processed);
        Assert.Equal(NotificationStatus.Sent, n.Status);
        Assert.Equal(1, n.Attempts);
        Assert.NotNull(n.SentAtUtc);
    }

    [Fact]
    public async Task Failed_send_is_retried_after_backoff_and_then_succeeds()
    {
        using var app = new TestApp();
        var id = await app.SeedAsync("hello [flaky]");
        var t0 = DateTime.UtcNow.AddMinutes(1);

        await app.ProcessAsync(t0);                       // attempt 1 fails
        var afterFirst = await app.LoadAsync(id);
        Assert.Equal(NotificationStatus.Queued, afterFirst.Status);
        Assert.Equal(1, afterFirst.Attempts);
        Assert.NotNull(afterFirst.LastError);

        Assert.Equal(0, await app.ProcessAsync(t0));      // retry is not due yet

        await app.ProcessAsync(t0.AddMinutes(1));         // attempt 2 succeeds
        var final = await app.LoadAsync(id);
        Assert.Equal(NotificationStatus.Sent, final.Status);
        Assert.Equal(2, final.Attempts);
        Assert.Null(final.LastError);
    }

    [Fact]
    public async Task Notification_is_marked_failed_after_max_attempts()
    {
        using var app = new TestApp();
        var id = await app.SeedAsync("hello [fail]");
        var t0 = DateTime.UtcNow.AddMinutes(1);

        await app.ProcessAsync(t0);
        await app.ProcessAsync(t0.AddHours(1));
        await app.ProcessAsync(t0.AddHours(2));

        var n = await app.LoadAsync(id);
        Assert.Equal(NotificationStatus.Failed, n.Status);
        Assert.Equal(3, n.Attempts);
        Assert.Contains("Simulated", n.LastError);

        Assert.Equal(0, await app.ProcessAsync(t0.AddHours(3)));   // failed items are not retried again
    }
}

public class SummaryTests
{
    [Fact]
    public async Task Summary_counts_notifications_by_status()
    {
        using var app = new TestApp();
        await app.SeedAsync("a", "Sent");
        await app.SeedAsync("b", "Sent");
        await app.SeedAsync("c", "Failed");
        await app.SeedAsync("d", "Queued");

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var text = await SummaryBuilder.BuildAsync(db, DateTime.UtcNow.AddMinutes(1));

        Assert.Contains("Sent: 2", text);
        Assert.Contains("Failed: 1", text);
        Assert.Contains("Queued: 1", text);
    }

    [Fact]
    public async Task Summary_endpoint_queues_a_notification()
    {
        using var app = new TestApp();
        var res = await app.CreateClient().PostAsync("/api/notifications/summary", null);
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.StartsWith("Daily summary", body.GetProperty("message").GetString());
        Assert.Equal("Queued", body.GetProperty("status").GetString());
    }
}
