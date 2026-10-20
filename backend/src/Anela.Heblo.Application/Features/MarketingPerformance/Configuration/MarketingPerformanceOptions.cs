using Anela.Heblo.Domain.Features.MarketingPerformance;

namespace Anela.Heblo.Application.Features.MarketingPerformance.Configuration;

public class MarketingPerformanceOptions
{
    public const string SectionName = "MarketingPerformance";

    /// <summary>How many months (current month included) the scheduled job recomputes. Default 2 = current + previous.</summary>
    public int RecomputeWindowMonths { get; set; } = 2;

    /// <summary>Divisor to derive without-VAT revenue from the stored with-VAT total.</summary>
    public decimal VatRate { get; set; } = 1.21m;

    public string CronExpression { get; set; } = "0 5 * * *";

    /// <summary>Upper bound for a single manual recompute request.</summary>
    public int MaxRecomputeRangeMonths { get; set; } = 60;

    public List<MarketingChannelOptions> Channels { get; set; } = new();

    public IReadOnlyList<MarketingChannelDefinition> ToDefinitions() =>
        Channels.Select(c => new MarketingChannelDefinition
        {
            Code = c.Code.Trim().ToLowerInvariant(),
            Label = c.Label.Trim(),
            VatIds = c.VatIds.Select(v => v.Trim()).Where(v => v.Length > 0).ToList(),
        }).ToList();
}
