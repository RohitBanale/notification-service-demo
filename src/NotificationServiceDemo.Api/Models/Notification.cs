namespace NotificationServiceDemo.Api.Models;

public enum NotificationStatus { Queued, Sent, Failed }

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Recipient { get; set; } = "";
    public string Channel { get; set; } = "";     // "whatsapp" | "email"
    public string Message { get; set; } = "";
    public NotificationStatus Status { get; set; } = NotificationStatus.Queued;
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime NextAttemptAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SentAtUtc { get; set; }
}

public record NotificationDto(
    Guid Id, string Recipient, string Channel, string Message, string Status,
    int Attempts, string? LastError, DateTime CreatedAtUtc, DateTime NextAttemptAtUtc, DateTime? SentAtUtc)
{
    public static NotificationDto From(Notification n) => new(
        n.Id, n.Recipient, n.Channel, n.Message, n.Status.ToString(),
        n.Attempts, n.LastError, n.CreatedAtUtc, n.NextAttemptAtUtc, n.SentAtUtc);
}

public record CreateNotificationRequest(string? Recipient, string? Channel, string? Message);
