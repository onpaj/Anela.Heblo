using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Anela.Heblo.Application.Shared.Rag;

/// <summary>
/// Parses the query-expansion LLM response. JSON (optionally wrapped in a Markdown code fence) yields the
/// expanded query plus normalised tags; anything else falls back to the whole raw response as the text to
/// embed with no tags — exactly the pre-tagging behaviour, so a bad prompt can only lose tags, never answers.
/// </summary>
public static class RagExpansionParser
{
    public const int MaxTagsPerKind = 5;

    private static readonly Regex CodeFence = new(
        @"^```(?:json)?\s*(?<body>.*?)\s*```$",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex InnerWhitespace = new(@"\s+", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static RagExpansionParseResult Parse(string rawResponse)
    {
        var trimmed = rawResponse.Trim();
        var fence = CodeFence.Match(trimmed);
        var candidate = fence.Success ? fence.Groups["body"].Value.Trim() : trimmed;

        if (!candidate.StartsWith('{'))
            return new RagExpansionParseResult(RagExpansionResult.Untagged(rawResponse), IsMalformedJson: false);

        ExpansionPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ExpansionPayload>(candidate, JsonOptions);
        }
        catch (JsonException)
        {
            return new RagExpansionParseResult(RagExpansionResult.Untagged(rawResponse), IsMalformedJson: true);
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Expanded))
            return new RagExpansionParseResult(RagExpansionResult.Untagged(rawResponse), IsMalformedJson: true);

        var expansion = new RagExpansionResult(
            payload.Expanded.Trim(),
            NormalizeTags(payload.ProblemTags),
            NormalizeTags(payload.ProductTags));

        return new RagExpansionParseResult(expansion, IsMalformedJson: false);
    }

    public static IReadOnlyList<string> NormalizeTags(IEnumerable<string?>? tags) =>
        (tags ?? [])
            .Select(NormalizeTag)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Take(MaxTagsPerKind)
            .ToList();

    public static string? NormalizeTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        return InnerWhitespace.Replace(tag.Trim(), " ").ToLowerInvariant();
    }

    private sealed class ExpansionPayload
    {
        [JsonPropertyName("expanded")]
        public string? Expanded { get; init; }

        [JsonPropertyName("problemTags")]
        public List<string?>? ProblemTags { get; init; }

        [JsonPropertyName("productTags")]
        public List<string?>? ProductTags { get; init; }
    }
}
