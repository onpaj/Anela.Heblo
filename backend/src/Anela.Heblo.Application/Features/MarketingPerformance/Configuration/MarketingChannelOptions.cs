namespace Anela.Heblo.Application.Features.MarketingPerformance.Configuration;

public class MarketingChannelOptions
{
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public List<string> VatIds { get; set; } = new();
}
