using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Pricing.Services;

/// <summary>
/// Raised when an edit would imply an impossible row (negative cost, non-positive price).
/// Handlers translate this into an error response; the cell keeps its previous value.
/// </summary>
public class PricingEditException : Exception
{
    public ErrorCodes ErrorCode { get; }
    public Dictionary<string, string> Parameters { get; }

    public PricingEditException(ErrorCodes errorCode, Dictionary<string, string> parameters)
        : base($"Pricing edit rejected: {errorCode}")
    {
        ErrorCode = errorCode;
        Parameters = parameters;
    }
}
