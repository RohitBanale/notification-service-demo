namespace NotificationServiceDemo.Api.Services;

public class DispatcherOptions
{
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 2;
    public int MaxAttempts { get; set; } = 3;
    public int BaseDelaySeconds { get; set; } = 5;   // delay doubles after every failed attempt
    public int BatchSize { get; set; } = 20;
}

public class SummaryOptions
{
    public bool Enabled { get; set; } = true;
    public string TimeUtc { get; set; } = "08:00";
    public string Recipient { get; set; } = "ops@example.com";
    public string Channel { get; set; } = "email";
}
