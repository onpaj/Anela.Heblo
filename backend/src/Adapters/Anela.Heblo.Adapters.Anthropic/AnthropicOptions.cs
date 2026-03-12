namespace Anela.Heblo.Adapters.Anthropic;

public class AnthropicOptions
{
    public const string SectionKey = "Anthropic";

    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "claude-sonnet-4-6";
    public int MaxTokens { get; set; } = 1024;
    public string MessagesUrl { get; set; } = "https://api.anthropic.com/v1/messages";
}
