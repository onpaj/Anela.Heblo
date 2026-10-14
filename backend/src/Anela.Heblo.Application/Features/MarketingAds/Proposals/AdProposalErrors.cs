namespace Anela.Heblo.Application.Features.MarketingAds.Proposals;

/// <summary>Builds the Params dictionary of an error response.</summary>
public static class AdProposalErrors
{
    public static Dictionary<string, string> Params(IReadOnlyList<string> errors) =>
        new() { ["Errors"] = string.Join(" ", errors) };

    public static Dictionary<string, string> Reason(string? reason) =>
        new() { ["Reason"] = reason ?? string.Empty };
}
