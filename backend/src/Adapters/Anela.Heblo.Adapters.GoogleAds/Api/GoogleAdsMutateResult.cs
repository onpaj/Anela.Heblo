using System.Text.Json;

namespace Anela.Heblo.Adapters.GoogleAds.Api;

internal sealed record GoogleAdsMutateResult(
    bool IsSuccess, IReadOnlyList<string> ResourceNames, string ResponseJson, string? ErrorCode, string? ErrorMessage)
{
    public static GoogleAdsMutateResult Success(IReadOnlyList<string> resourceNames, string responseJson) =>
        new(true, resourceNames, responseJson, null, null);

    public static GoogleAdsMutateResult Rejected(string errorCode, string errorMessage, string responseJson) =>
        new(false, Array.Empty<string>(), responseJson, errorCode, errorMessage);

    /// <summary>
    /// {"results":[{"resourceName":"…"}]}; validate-only responses are "{}". An unreadable 2xx body is
    /// still a success with no names: Google answered 200, so the change may be live and must not be
    /// reported as a transport failure.
    /// </summary>
    public static GoogleAdsMutateResult FromResponse(string responseJson)
    {
        try
        {
            return Success(ReadResourceNames(responseJson), responseJson);
        }
        catch (JsonException)
        {
            return Success(Array.Empty<string>(), responseJson);
        }
    }

    private static IReadOnlyList<string> ReadResourceNames(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        var names = document.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array
            ? results.EnumerateArray()
                .Select(r => r.TryGetProperty("resourceName", out var n) ? n.GetString() : null)
                .OfType<string>()
                .ToList()
            : new List<string>();
        return names;
    }
}
