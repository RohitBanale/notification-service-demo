using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NotificationServiceDemo.Api.Data;
using NotificationServiceDemo.Api.Models;

namespace NotificationServiceDemo.Api.Services;

/// <summary>
/// Sends every queued notification that is due. Failed sends are retried with exponential backoff
/// until MaxAttempts is reached, then marked Failed. "Now" is passed in so it is easy to test.
/// </summary>
public class NotificationProcessor(
    AppDbContext db, INotificationProvider provider, IOptions<DispatcherOptions> options, ILogger<NotificationProcessor> log)
{
    public async Task<int> ProcessDueAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var opt = options.Value;
        var due = await db.Notifications
            .Where(n => n.Status == NotificationStatus.Queued && n.NextAttemptAtUtc <= nowUtc)
            .OrderBy(n => n.CreatedAtUtc)
            .Take(opt.BatchSize)
            .ToListAsync(ct);

        foreach (var n in due)
        {
            n.Attempts++;
            try
            {
                await provider.SendAsync(n, ct);
                n.Status = NotificationStatus.Sent;
                n.SentAtUtc = nowUtc;
                n.LastError = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                n.LastError = ex.Message;
                if (n.Attempts >= opt.MaxAttempts)
                {
                    n.Status = NotificationStatus.Failed;
                    log.LogWarning("Notification {Id} failed permanently after {Attempts} attempts: {Error}", n.Id, n.Attempts, ex.Message);
                }
                else
                {
                    var delay = TimeSpan.FromSeconds(opt.BaseDelaySeconds * Math.Pow(2, n.Attempts - 1));
                    n.NextAttemptAtUtc = nowUtc + delay;
                    log.LogInformation("Notification {Id} attempt {Attempts} failed, retrying in {Delay}s", n.Id, n.Attempts, delay.TotalSeconds);
                }
            }
        }

        await db.SaveChangesAsync(ct);
        return due.Count;
    }
}
