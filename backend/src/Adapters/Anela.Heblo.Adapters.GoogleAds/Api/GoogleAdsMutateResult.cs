using System.Text.Json;

namespace Anela.Heblo.Adapters.GoogleAds.Api;

internal sealed record GoogleAdsMutateResult(
    bool IsSuccess, IReadOnlyList<string> ResourceNames, string ResponseJson, string? ErrorCode, string? ErrorMessage)
{
    public static GoogleAdsMutateResult Success(IReadOnlyList<string> resourceNames, string responseJson) =>
        new(true, resourceNames, responseJson, null, null);

    public static GoogleAdsMutateResult Rejected(string errorCode, string errorMessage, string responseJson) =>
        new(false, Array.Empty<string>(), responseJson, errorCode, errorMessage);

    /// <summary>{"results":[{"resourceName":"…"}]}; validate-only responses are "{}".</summary>
    public static GoogleAdsMutateResult FromResponse(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        var names = document.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array
            ? results.EnumerateArray()
                .Select(r => r.TryGetProperty("resourceName", out var n) ? n.GetString() : null)
                .OfType<string>()
                .ToList()
            : new List<string>();
        return Success(names, responseJson);
    }
}
