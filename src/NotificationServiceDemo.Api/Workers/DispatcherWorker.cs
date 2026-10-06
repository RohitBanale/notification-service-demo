using Microsoft.Extensions.Options;
using NotificationServiceDemo.Api.Services;

namespace NotificationServiceDemo.Api.Workers;

/// <summary>Background loop that polls for due notifications and sends them.</summary>
public class DispatcherWorker(IServiceScopeFactory scopes, IOptions<DispatcherOptions> options, ILogger<DispatcherWorker> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled) return;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<NotificationProcessor>();
                await processor.ProcessDueAsync(DateTime.UtcNow, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { log.LogError(ex, "Dispatcher loop failed"); }

            try { await Task.Delay(TimeSpan.FromSeconds(options.Value.PollIntervalSeconds), ct); }
            catch (OperationCanceledException) { break; }
        }
    }
}
