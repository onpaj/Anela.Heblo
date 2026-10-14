using System.Text.Json;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.RevertAdProposal;

/// <summary>
/// Recovers a platform resource id that the execution job could not keep in its column: an id longer than the
/// column is stored in the response JSON (raw, or in a <c>{platformResourceId, platformResponse}</c> envelope),
/// and a mutate confirmed by re-read stores the re-read row instead. Google Ads names resources with
/// <c>resourceName</c> in both its mutate responses and its search rows. The executor still validates the
/// recovered id before using it.
/// </summary>
internal static class StoredResourceId
{
    private const string EnvelopeProperty = "platformResourceId";
    private const string ResourceNameProperty = "resourceName";

    /// <returns>The id, or null when the JSON names none or more than one.</returns>
    public static string? Recover(string? responseJson)
    {
        if (string.IsNullOrWhiteSpace(responseJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(responseJson);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty(EnvelopeProperty, out var enveloped)
                && enveloped.ValueKind == JsonValueKind.String)
            {
                return enveloped.GetString();
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            CollectResourceNames(root, names);
            return names.Count == 1 ? names.Single() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void CollectResourceNames(JsonElement element, ISet<string> names)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals(ResourceNameProperty) && property.Value.ValueKind == JsonValueKind.String)
                    {
                        names.Add(property.Value.GetString()!);
                    }
                    else
                    {
                        CollectResourceNames(property.Value, names);
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectResourceNames(item, names);
                }
                break;
        }
    }
}
