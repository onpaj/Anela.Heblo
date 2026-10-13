namespace Anela.Heblo.Domain.Features.MarketingPerformance;

public class MarketingChannelDefinition
{
    public string Code { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public IReadOnlyList<string> VatIds { get; init; } = Array.Empty<string>();
}
