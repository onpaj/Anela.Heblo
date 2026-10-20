namespace Anela.Heblo.Application.Shared.Rag;

/// <summary>
/// Output of query expansion: the text to embed for vector search, plus the problem/product tags the
/// model extracted from the question. Tags are never part of <see cref="QueryToEmbed"/> — appending them
/// would pollute the query vector.
/// </summary>
public sealed record RagExpansionResult(
    string QueryToEmbed,
    IReadOnlyList<string> ProblemTags,
    IReadOnlyList<string> ProductTags)
{
    public static RagExpansionResult Untagged(string queryToEmbed) => new(queryToEmbed, [], []);
}

/// <summary>Parse outcome; <see cref="IsMalformedJson"/> is true only when the text looked like JSON but could not be used.</summary>
public sealed record RagExpansionParseResult(RagExpansionResult Expansion, bool IsMalformedJson);
