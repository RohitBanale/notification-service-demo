using NotificationServiceDemo.Api.Models;

namespace NotificationServiceDemo.Api.Services;

/// <summary>Sends one notification. Throw to signal a failure (the processor handles retries).</summary>
public interface INotificationProvider
{
    Task SendAsync(Notification notification, CancellationToken ct = default);
}

/// <summary>
/// Stand-in for a real WhatsApp / email provider (no keys or network needed).
/// Include "[fail]" in a message to simulate a permanent failure,
/// or "[flaky]" to fail the first attempt and succeed on the second.
/// </summary>
public class FakeNotificationProvider(ILogger<FakeNotificationProvider> log) : INotificationProvider
{
    public Task SendAsync(Notification n, CancellationToken ct = default)
    {
        if (n.Message.Contains("[fail]", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Simulated provider failure");
        if (n.Message.Contains("[flaky]", StringComparison.OrdinalIgnoreCase) && n.Attempts < 2)
            throw new InvalidOperationException("Simulated temporary failure");

        log.LogInformation("[{Channel}] to {Recipient}: {Message}", n.Channel, n.Recipient, n.Message);
        return Task.CompletedTask;
    }
}
