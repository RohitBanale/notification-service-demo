using Microsoft.Extensions.Options;
using NotificationServiceDemo.Api.Services;

namespace NotificationServiceDemo.Api.Workers;

/// <summary>Queues one summary notification per day at the configured UTC time.</summary>
public class DailySummaryWorker(IServiceScopeFactory scopes, IOptions<SummaryOptions> options, ILogger<DailySummaryWorker> log)
    : BackgroundService
{
    private DateOnly? _lastRun;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled) return;
        if (!TimeOnly.TryParse(options.Value.TimeUtc, out var runAt))
        {
            log.LogWarning("Summary:TimeUtc '{Value}' is not a valid time, daily summary disabled", options.Value.TimeUtc);
            return;
        }

        // If the app starts after today's run time, wait for tomorrow instead of sending immediately.
        var start = DateTime.UtcNow;
        if (TimeOnly.FromDateTime(start) >= runAt) _lastRun = DateOnly.FromDateTime(start);

        while (!ct.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var today = DateOnly.FromDateTime(now);
            if (_lastRun != today && TimeOnly.FromDateTime(now) >= runAt)
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<SummaryService>().EnqueueAsync(now, ct);
                    _lastRun = today;
                    log.LogInformation("Daily summary queued");
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { log.LogError(ex, "Daily summary failed"); }
            }

            try { await Task.Delay(TimeSpan.FromSeconds(30), ct); }
            catch (OperationCanceledException) { break; }
        }
    }
}
