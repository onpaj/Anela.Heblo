namespace Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;

public class AggregatedFact
{
    public string Claim { get; set; } = "";
    public double Confidence { get; set; }
    public string? SourceUrl { get; set; }
    public string? SourceTitle { get; set; }
    public string? ValidationNote { get; set; }
}
