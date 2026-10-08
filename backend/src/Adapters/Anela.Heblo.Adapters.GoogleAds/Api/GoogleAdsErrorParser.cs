using System.Text.Json;

namespace Anela.Heblo.Adapters.GoogleAds.Api;

internal sealed record GoogleAdsError(string? ErrorCode, string Message, string? RequestId);

/// <summary>
/// Reads Google's error envelope {"error":{"code","message","status","details":[...]}}. The first
/// GoogleAdsFailure error wins ("authorizationError.USER_PERMISSION_DENIED"); an ErrorInfo reason
/// ("errorInfo.SERVICE_DISABLED") is the fallback, then the gRPC status ("status.UNAVAILABLE").
/// </summary>
internal static class GoogleAdsErrorParser
{
    private const int MaxRawLength = 300;

    public static GoogleAdsError Parse(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("error", out var error)
                   && error.ValueKind == JsonValueKind.Object
                ? FromEnvelope(error)
                : new GoogleAdsError(null, Truncate(body), null);
        }
        catch (JsonException)
        {
            return new GoogleAdsError(null, Truncate(body), null);
        }
    }

    private static GoogleAdsError FromEnvelope(JsonElement error)
    {
        var message = StringProperty(error, "message") ?? string.Empty;
        var details = error.TryGetProperty("details", out var d) && d.ValueKind == JsonValueKind.Array
            ? d.EnumerateArray().ToList()
            : new List<JsonElement>();

        var requestId = details.Select(x => StringProperty(x, "requestId")).FirstOrDefault(x => x is not null);
        var failure = details.SelectMany(FailureErrors).FirstOrDefault();
        if (failure.ValueKind == JsonValueKind.Object)
            return new GoogleAdsError(FailureCode(failure), StringProperty(failure, "message") ?? message, requestId);

        var reason = details.Select(x => StringProperty(x, "reason")).FirstOrDefault(x => x is not null);
        var status = StringProperty(error, "status");
        var code = reason is not null ? $"errorInfo.{reason}" : status is not null ? $"status.{status}" : null;
        return new GoogleAdsError(code, message, requestId);
    }

    private static IEnumerable<JsonElement> FailureErrors(JsonElement detail) =>
        detail.ValueKind == JsonValueKind.Object
        && detail.TryGetProperty("errors", out var errors)
        && errors.ValueKind == JsonValueKind.Array
            ? errors.EnumerateArray()
            : Enumerable.Empty<JsonElement>();

    private static string? FailureCode(JsonElement failure)
    {
        if (!failure.TryGetProperty("errorCode", out var errorCode) || errorCode.ValueKind != JsonValueKind.Object)
            return null;
        var first = errorCode.EnumerateObject().FirstOrDefault();
        return first.Value.ValueKind == JsonValueKind.String ? $"{first.Name}.{first.Value.GetString()}" : null;
    }

    private static string? StringProperty(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Truncate(string body) => body.Length <= MaxRawLength ? body : body[..MaxRawLength];
}
