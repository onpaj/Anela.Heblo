using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.MarketingPerformance;

public class MarketingPerformanceChannelCost : IEntity<int>
{
    public int Id { get; set; }
    public int MonthId { get; set; }
    public MarketingPerformanceMonth? Month { get; set; }
    /// <summary>Channel code from settings (e.g. "meta"). String, not enum: adding a channel is a config change.</summary>
    public string ChannelCode { get; set; } = string.Empty;
    /// <summary>Sum of Flexi sumZklCelkem for non-storno invoices of the channel's suppliers.</summary>
    public decimal CostWithoutVat { get; set; }
    public int InvoiceCount { get; set; }
}
