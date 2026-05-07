namespace Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;

public sealed record AggregatedFact
{
    public string Claim { get; init; } = "";
    public double Confidence { get; init; }
    public string? SourceUrl { get; init; }
    public string? SourceTitle { get; init; }
    public string? ValidationNote { get; init; }
}
