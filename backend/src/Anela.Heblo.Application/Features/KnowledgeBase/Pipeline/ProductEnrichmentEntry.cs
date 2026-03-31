namespace Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;

public class ProductEnrichmentEntry
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? Url { get; set; } // placeholder — populated in a future story
}
