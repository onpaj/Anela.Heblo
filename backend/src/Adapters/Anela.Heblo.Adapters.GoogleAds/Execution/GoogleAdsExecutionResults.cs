using Anela.Heblo.Adapters.GoogleAds.Api;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Adapters.GoogleAds.Execution;

internal static class GoogleAdsExecutionResults
{
    public const string ValidateOnlyMarker = "ValidateOnly:";
    public static readonly AdTargetState Missing = new(false, null, null);

    public static AdExecutionResult Failed(string error, string? responseJson = null) =>
        new(AdExecutionOutcome.Failed, null, null, null, responseJson, error);

    /// <summary>
    /// A validate-only success is reported as Failed: nothing changed in the account, and the
    /// contract has no "validated" outcome, so nothing downstream may believe it did.
    /// </summary>
    public static AdExecutionResult From(GoogleAdsMutateResult response, string before, string after, bool validateOnly)
    {
        if (!response.IsSuccess)
            return Failed($"{response.ErrorCode}: {response.ErrorMessage}", response.ResponseJson);
        if (validateOnly)
            return Failed(
                $"{ValidateOnlyMarker} Google accepted the request; nothing was changed (GoogleAds:ValidateOnly is on).",
                response.ResponseJson);
        return new AdExecutionResult(
            AdExecutionOutcome.Succeeded, before, after, response.ResourceNames.FirstOrDefault(), response.ResponseJson, null);
    }
}
