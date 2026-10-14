using System.Net;

namespace Anela.Heblo.Adapters.GoogleAds.Api;

/// <summary>
/// A transport, authentication or authorization failure talking to Google Ads. The message never
/// contains credentials. <see cref="IsTransient"/> drives the retry policy.
/// </summary>
internal sealed class GoogleAdsApiException : Exception
{
    public GoogleAdsApiException(
        string message, HttpStatusCode? statusCode, string? errorCode, string? requestId, bool isTransient)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        RequestId = requestId;
        IsTransient = isTransient;
    }

    public HttpStatusCode? StatusCode { get; }
    public string? ErrorCode { get; }
    public string? RequestId { get; }
    public bool IsTransient { get; }
}
