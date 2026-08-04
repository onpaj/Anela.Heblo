using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.LabelIdentification.Services;

public interface ILabelReferenceIndex
{
    IReadOnlyList<LabelReferenceEntry> Entries { get; }
}

/// <summary>
/// Immutable in-memory index of label reference text, loaded once from an embedded
/// resource. Nothing parses a PDF at request time — the reference data is the
/// extracted, normalized INCI text (~27 KB for the whole catalogue).
/// </summary>
public sealed class LabelReferenceIndex : ILabelReferenceIndex
{
    private const string ResourceName =
        "Anela.Heblo.Application.Features.LabelIdentification.Data.label-references.json";

    public IReadOnlyList<LabelReferenceEntry> Entries { get; }

    public LabelReferenceIndex()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' not found. Regenerate it with " +
                "Anela.Heblo.LabelReferenceExtractor and ensure the csproj embeds it.");

        var raw = JsonSerializer.Deserialize<List<RawEntry>>(stream)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is not valid JSON.");

        Entries = raw
            .Select(e => new LabelReferenceEntry
            {
                Family = e.Family,
                Codes = e.Codes,
                Normalized = e.Normalized,
                Tokens = LabelReferenceEntry.Tokenize(e.Normalized),
            })
            .ToList();
    }

    private sealed class RawEntry
    {
        [JsonPropertyName("family")]
        public string Family { get; set; } = string.Empty;

        [JsonPropertyName("codes")]
        public List<string> Codes { get; set; } = new();

        [JsonPropertyName("normalized")]
        public string Normalized { get; set; } = string.Empty;
    }
}
