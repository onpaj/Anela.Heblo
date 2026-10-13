using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingPerformance.Configuration;

public class MarketingPerformanceOptionsValidator : IValidateOptions<MarketingPerformanceOptions>
{
    private const int MaxSupportedRecomputeRangeMonths = 120;


    public ValidateOptionsResult Validate(string? name, MarketingPerformanceOptions options)
    {
        var errors = new List<string>();

        if (options.RecomputeWindowMonths < 1)
            errors.Add("MarketingPerformance:RecomputeWindowMonths must be >= 1.");
        if (options.VatRate <= 1m)
            errors.Add("MarketingPerformance:VatRate must be > 1 (e.g. 1.21).");
        // Upper bound too: this value is the only thing capping how many live ERP queries
        // one accepted recompute fires, so a config typo of 6000 must not silently permit
        // a 500-year backfill loop.
        if (options.MaxRecomputeRangeMonths is < 1 or > MaxSupportedRecomputeRangeMonths)
            errors.Add($"MarketingPerformance:MaxRecomputeRangeMonths must be between 1 and {MaxSupportedRecomputeRangeMonths}.");

        var defs = options.ToDefinitions();
        if (defs.Count == 0)
            errors.Add("MarketingPerformance:Channels must contain at least one channel.");

        foreach (var d in defs.Where(d => string.IsNullOrWhiteSpace(d.Code)))
            errors.Add("MarketingPerformance:Channels contains a channel with an empty Code.");
        foreach (var d in defs.Where(d => d.VatIds.Count == 0))
            errors.Add($"MarketingPerformance:Channels['{d.Code}'] has no VatIds.");

        var duplicateCodes = defs.GroupBy(d => d.Code).Where(g => g.Count() > 1).Select(g => g.Key);
        foreach (var code in duplicateCodes)
            errors.Add($"MarketingPerformance:Channels has duplicate Code '{code}'.");

        foreach (var d in defs)
        {
            var repeated = d.VatIds.GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.First());
            foreach (var vat in repeated)
                errors.Add($"MarketingPerformance:Channels['{d.Code}'] lists VAT ID '{vat}' more than once.");
        }

        var crossChannelDuplicates = defs
            .SelectMany(d => d.VatIds.Distinct(StringComparer.OrdinalIgnoreCase))
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.First());
        foreach (var vat in crossChannelDuplicates)
            errors.Add($"MarketingPerformance: VAT ID '{vat}' is assigned to more than one channel.");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
