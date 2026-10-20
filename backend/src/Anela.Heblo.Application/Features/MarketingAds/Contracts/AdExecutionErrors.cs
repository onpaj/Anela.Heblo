namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>Error markers an executor puts at the start of <see cref="AdExecutionResult.Error"/>.</summary>
public static class AdExecutionErrors
{
    /// <summary>
    /// The platform accepted the request in validate-only mode and nothing changed in the account. The result
    /// is reported as Failed; it is neither applied nor a rejection worth retrying.
    /// </summary>
    public const string ValidateOnlyPrefix = "ValidateOnly:";

    public static bool IsValidateOnly(string? error) =>
        error is not null && error.StartsWith(ValidateOnlyPrefix, StringComparison.Ordinal);
}
