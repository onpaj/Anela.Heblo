using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingPerformance.Configuration;

public class MarketingPerformanceOptionsValidator : IValidateOptions<MarketingPerformanceOptions>
{
    public ValidateOptionsResult Validate(string? name, MarketingPerformanceOptions options)
    {
        var errors = new List<string>();

        if (options.RecomputeWindowMonths < 1)
            errors.Add("MarketingPerformance:RecomputeWindowMonths must be >= 1.");
        if (options.VatRate <= 1m)
            errors.Add("MarketingPerformance:VatRate must be > 1 (e.g. 1.21).");
        if (options.MaxRecomputeRangeMonths < 1)
            errors.Add("MarketingPerformance:MaxRecomputeRangeMonths must be >= 1.");

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

        var duplicateVatIds = defs.SelectMany(d => d.VatIds)
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.First());
        foreach (var vat in duplicateVatIds)
            errors.Add($"MarketingPerformance: VAT ID '{vat}' is assigned to more than one channel.");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
