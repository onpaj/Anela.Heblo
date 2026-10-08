using System.Text.Json;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

public sealed record AdEntityState(string Name, AdEntityStatus Status, IReadOnlyDictionary<string, string?> Attributes);

public sealed record AdFieldChange(string ChangeType, string? OldValueJson, string? NewValueJson);

/// <summary>Snapshot-diff fallback for sources without a change log (spec section 5): name, status, attributes.</summary>
public static class AdSnapshotDiffer
{
    public const string NameChange = "Name";
    public const string StatusChange = "Status";
    public const string AttributesChange = "Attributes";
    public const string CreatedChange = "Created";

    public static IReadOnlyList<AdFieldChange> Diff(AdEntityState before, AdEntityState after)
    {
        var changes = new List<AdFieldChange>();

        if (!string.Equals(before.Name, after.Name, StringComparison.Ordinal))
            changes.Add(new AdFieldChange(NameChange, JsonSerializer.Serialize(before.Name), JsonSerializer.Serialize(after.Name)));

        if (before.Status != after.Status)
            changes.Add(new AdFieldChange(StatusChange,
                JsonSerializer.Serialize(before.Status.ToString()), JsonSerializer.Serialize(after.Status.ToString())));

        var changedKeys = before.Attributes.Keys.Union(after.Attributes.Keys)
            .Where(key => !string.Equals(ValueOf(before.Attributes, key), ValueOf(after.Attributes, key), StringComparison.Ordinal))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();
        if (changedKeys.Count > 0)
            changes.Add(new AdFieldChange(AttributesChange,
                AdAttributesJson.Subset(before.Attributes, changedKeys), AdAttributesJson.Subset(after.Attributes, changedKeys)));

        return changes;
    }

    public static AdFieldChange Created(AdEntityState state) =>
        new(CreatedChange, null, JsonSerializer.Serialize(new { name = state.Name, status = state.Status.ToString() }));

    private static string? ValueOf(IReadOnlyDictionary<string, string?> attributes, string key) =>
        attributes.TryGetValue(key, out var value) ? value : null;
}
