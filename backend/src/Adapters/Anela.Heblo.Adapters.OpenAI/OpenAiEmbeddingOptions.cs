namespace Anela.Heblo.Adapters.OpenAI;

public class OpenAiEmbeddingOptions
{
    public const string SectionKey = "OpenAI";

    public string ApiKey { get; set; } = "";
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    public int EmbeddingDimensions { get; set; } = 1536;
}
