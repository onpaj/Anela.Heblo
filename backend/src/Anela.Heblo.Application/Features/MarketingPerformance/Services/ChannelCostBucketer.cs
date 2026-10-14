using Anela.Heblo.Domain.Features.MarketingPerformance;

namespace Anela.Heblo.Application.Features.MarketingPerformance.Services;

public class ChannelCostBucket
{
    public string ChannelCode { get; init; } = string.Empty;
    public decimal CostWithoutVat { get; init; }
    public int InvoiceCount { get; init; }
}

public class ChannelBucketingResult
{
    public List<ChannelCostBucket> Buckets { get; init; } = new();
    public int SkippedCancelled { get; init; }
    public List<string> UnmatchedVatIds { get; init; } = new();
}

/// <summary>Assigns received invoices to channels by supplier DIČ. Pure; one explicit row per configured channel.</summary>
public static class ChannelCostBucketer
{
    /// <remarks>
    /// Each VAT ID is expected to belong to exactly one channel; <c>MarketingPerformanceOptionsValidator</c> enforces
    /// that at startup. If a duplicate ever reaches here anyway, the first matching channel wins rather than throwing.
    /// </remarks>
    public static ChannelBucketingResult Bucket(IReadOnlyList<AdCostInvoice> invoices, IReadOnlyList<MarketingChannelDefinition> channels)
    {
        var channelByVatId = channels
            .SelectMany(ch => ch.VatIds.Select(v => (VatId: v, ch.Code)))
            .GroupBy(x => x.VatId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Code, StringComparer.OrdinalIgnoreCase);

        var live = invoices.Where(i => !i.IsCancelled).ToList();
        var skippedCancelled = invoices.Count - live.Count;

        var matched = live
            .Select(i => (Invoice: i, Code: channelByVatId.TryGetValue(i.SupplierVatId, out var code) ? code : null))
            .ToList();

        var buckets = channels.Select(ch =>
        {
            var mine = matched.Where(m => m.Code == ch.Code).Select(m => m.Invoice).ToList();
            return new ChannelCostBucket
            {
                ChannelCode = ch.Code,
                CostWithoutVat = mine.Sum(i => i.AmountWithoutVat),
                InvoiceCount = mine.Count,
            };
        }).ToList();

        var unmatched = matched.Where(m => m.Code is null)
            .Select(m => m.Invoice.SupplierVatId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ChannelBucketingResult { Buckets = buckets, SkippedCancelled = skippedCancelled, UnmatchedVatIds = unmatched };
    }
}
