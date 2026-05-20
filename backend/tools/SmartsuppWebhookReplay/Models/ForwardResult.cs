namespace SmartsuppWebhookReplay.Models;

public sealed class ForwardResult
{
    public int HttpStatus { get; init; }
    public string ResponseBody { get; init; } = null!;
    public int DurationMs { get; init; }
    public DateTime SentAt { get; init; }
}
