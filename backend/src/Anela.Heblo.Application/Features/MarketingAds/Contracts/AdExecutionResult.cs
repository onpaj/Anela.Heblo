namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>
/// <paramref name="PlatformResourceId"/> identifies what the platform created or changed (for
/// AddNegativeKeyword: the negative criterion's id, which revert uses).
/// </summary>
public sealed record AdExecutionResult(
    AdExecutionOutcome Outcome, string? BeforeValue, string? AfterValue,
    string? PlatformResourceId, string? PlatformResponseJson, string? Error);
