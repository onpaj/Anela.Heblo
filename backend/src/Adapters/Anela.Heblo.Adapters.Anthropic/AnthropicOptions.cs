namespace Anela.Heblo.Adapters.Anthropic;

public class AnthropicOptions
{
    public const string SectionKey = "Anthropic";

    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "claude-sonnet-4-6";
    public int MaxTokens { get; set; } = 1024;
    public string MessagesUrl { get; set; } = "https://api.anthropic.com/v1/messages";

    /// <summary>
    /// Timeout in seconds for the Anthropic HTTP client. Defaults to 60s to avoid
    /// the .NET default 100s timeout which causes stream-read TimeoutExceptions on
    /// large AI responses.
    /// </summary>
    public int HttpTimeoutSeconds { get; set; } = 60;
}
