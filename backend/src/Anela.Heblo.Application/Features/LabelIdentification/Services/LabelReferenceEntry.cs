namespace Anela.Heblo.Application.Features.LabelIdentification.Services;

public sealed class LabelReferenceEntry
{
    public required string Family { get; init; }
    public required IReadOnlyList<string> Codes { get; init; }
    public required string Normalized { get; init; }

    /// <summary>Comma-split ingredient set, precomputed for Jaccard overlap.</summary>
    public required IReadOnlySet<string> Tokens { get; init; }

    public static IReadOnlySet<string> Tokenize(string normalized) =>
        normalized
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
}
