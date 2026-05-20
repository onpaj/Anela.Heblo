namespace SmartsuppWebhookReplay.Models;

public sealed class ReplayOptions
{
    public const string SectionName = "Replay";
    public required string TargetUrl { get; init; }
    public int TimeoutSeconds { get; init; } = 30;
    public string? WebhookSecret { get; init; }
}
