using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NotificationServiceDemo.Api.Data;
using NotificationServiceDemo.Api.Models;

namespace NotificationServiceDemo.Api.Services;

public static class SummaryBuilder
{
    /// <summary>Text summary of notifications created in the 24 hours before nowUtc.</summary>
    public static async Task<string> BuildAsync(AppDbContext db, DateTime nowUtc, CancellationToken ct = default)
    {
        var since = nowUtc.AddHours(-24);
        var statuses = await db.Notifications
            .Where(n => n.CreatedAtUtc >= since)
            .Select(n => n.Status)
            .ToListAsync(ct);

        int Count(NotificationStatus s) => statuses.Count(x => x == s);
        return $"Daily summary (last 24h): Sent: {Count(NotificationStatus.Sent)}, " +
               $"Failed: {Count(NotificationStatus.Failed)}, Queued: {Count(NotificationStatus.Queued)}";
    }
}

/// <summary>Queues the daily summary as a normal notification, so it uses the same retry logic.</summary>
public class SummaryService(AppDbContext db, IOptions<SummaryOptions> options)
{
    public async Task<Notification> EnqueueAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var opt = options.Value;
        var n = new Notification
        {
            Recipient = opt.Recipient,
            Channel = opt.Channel.Trim().ToLowerInvariant(),
            Message = await SummaryBuilder.BuildAsync(db, nowUtc, ct),
            CreatedAtUtc = nowUtc,
            NextAttemptAtUtc = nowUtc
        };
        db.Notifications.Add(n);
        await db.SaveChangesAsync(ct);
        return n;
    }
}
