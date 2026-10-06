using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NotificationServiceDemo.Api.Data;
using NotificationServiceDemo.Api.Models;
using NotificationServiceDemo.Api.Services;

namespace NotificationServiceDemo.Api.Controllers;

[ApiController]
[Route("api/notifications")]
public class NotificationsController(AppDbContext db, SummaryService summary) : ControllerBase
{
    private static readonly string[] Channels = ["whatsapp", "email"];

    /// Queues a notification. It is sent by the background dispatcher, so the response is 202 Accepted.
    [HttpPost]
    public async Task<IActionResult> Create(CreateNotificationRequest req, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(req.Recipient))
            errors["recipient"] = ["Recipient is required."];

        var channel = req.Channel?.Trim().ToLowerInvariant();
        if (channel is null || !Channels.Contains(channel))
            errors["channel"] = ["Channel must be 'whatsapp' or 'email'."];

        if (string.IsNullOrWhiteSpace(req.Message))
            errors["message"] = ["Message is required."];
        else if (req.Message.Length > 1000)
            errors["message"] = ["Message must be 1000 characters or fewer."];

        if (errors.Count > 0)
            return ValidationProblem(new ValidationProblemDetails(errors));

        var n = new Notification
        {
            Recipient = req.Recipient!.Trim(),
            Channel = channel!,
            Message = req.Message!.Trim()
        };
        db.Notifications.Add(n);
        await db.SaveChangesAsync(ct);
        return AcceptedAtAction(nameof(Get), new { id = n.Id }, NotificationDto.From(n));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var n = await db.Notifications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return n is null ? NotFound() : Ok(NotificationDto.From(n));
    }

    /// Latest 100 notifications, optionally filtered by ?status=Queued|Sent|Failed
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct)
    {
        var query = db.Notifications.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<NotificationStatus>(status, ignoreCase: true, out var parsed))
                return BadRequest("status must be Queued, Sent or Failed");
            query = query.Where(n => n.Status == parsed);
        }
        var items = await query.OrderByDescending(n => n.CreatedAtUtc).Take(100).ToListAsync(ct);
        return Ok(items.Select(NotificationDto.From));
    }

    /// Queues the daily summary right now (normally a background worker does this once per day).
    [HttpPost("summary")]
    public async Task<IActionResult> QueueSummary(CancellationToken ct)
    {
        var n = await summary.EnqueueAsync(DateTime.UtcNow, ct);
        return AcceptedAtAction(nameof(Get), new { id = n.Id }, NotificationDto.From(n));
    }
}
