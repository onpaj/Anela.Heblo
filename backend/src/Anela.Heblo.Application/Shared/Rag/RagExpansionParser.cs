using System.Text;
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
    public const int MaxTagLength = 60;

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
            payload = Deserialize(candidate);
        }
        catch (JsonException)
        {
            // Models often emit real line breaks inside string values, which strict JSON forbids. Retry once.
            try
            {
                payload = Deserialize(EscapeControlCharsInStrings(candidate));
            }
            catch (JsonException)
            {
                return new RagExpansionParseResult(RagExpansionResult.Untagged(rawResponse), IsMalformedJson: true);
            }
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

        var normalized = InnerWhitespace.Replace(tag.Trim(), " ").ToLowerInvariant();
        return normalized.Length > MaxTagLength ? null : normalized;
    }

    private static ExpansionPayload? Deserialize(string json) =>
        JsonSerializer.Deserialize<ExpansionPayload>(json, JsonOptions);

    /// <summary>
    /// Escapes raw control characters that occur inside JSON string literals; whitespace between tokens is untouched.
    /// </summary>
    private static string EscapeControlCharsInStrings(string json)
    {
        var sb = new StringBuilder(json.Length + 16);
        var inString = false;
        var isEscaped = false;

        foreach (var c in json)
        {
            if (!inString)
            {
                sb.Append(c);
                inString = c == '"';
                continue;
            }

            if (isEscaped)
            {
                sb.Append(c);
                isEscaped = false;
                continue;
            }

            switch (c)
            {
                case '\\':
                    isEscaped = true;
                    sb.Append(c);
                    break;
                case '"':
                    inString = false;
                    sb.Append(c);
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                case < ' ':
                    sb.Append("\\u").Append(((int)c).ToString("x4"));
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
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
