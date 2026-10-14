namespace Anela.Heblo.Domain.Features.Rag;

/// <summary>
/// Filter for the RAG feedback/eval list. <see cref="CreatedToExclusive"/> is an exclusive upper bound
/// (half-open range). Tag filters are exact matches against already-normalised tags.
/// </summary>
public sealed record RagFeedbackLogFilter(
    RagFeature? Feature,
    bool? HasFeedback = null,
    string? UserId = null,
    DateTimeOffset? CreatedFrom = null,
    DateTimeOffset? CreatedToExclusive = null,
    string? ProblemTag = null,
    string? ProductTag = null);
