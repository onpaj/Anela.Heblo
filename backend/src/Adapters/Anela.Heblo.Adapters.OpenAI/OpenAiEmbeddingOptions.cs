namespace Anela.Heblo.Adapters.OpenAI;

public class OpenAiEmbeddingOptions
{
    public const string SectionKey = "OpenAI";

    public string ApiKey { get; set; } = "";
    public string EmbeddingModel { get; set; } = "text-embedding-3-large";
    public int EmbeddingDimensions { get; set; } = 3072;
}
