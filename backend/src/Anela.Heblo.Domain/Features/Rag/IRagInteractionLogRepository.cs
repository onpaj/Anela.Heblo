namespace Anela.Heblo.Domain.Features.Rag;

/// <summary>
/// Persistence for the unified RAG interaction / eval-dataset log, shared by KnowledgeBase and Smartsupp.
/// </summary>
public interface IRagInteractionLogRepository
{
    Task SaveAsync(RagInteractionLog log, CancellationToken ct = default);

    Task<RagInteractionLog?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Persists changes made to a tracked entity (e.g. after submitting feedback).</summary>
    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Records the answer the user actually sent for the draft identified by <paramref name="id"/>.
    /// <c>WasEdited</c> is derived by comparing <paramref name="sentAnswer"/> to the stored generated answer.
    /// No-ops when the row does not exist (e.g. a regenerated draft that was never persisted).
    /// </summary>
    Task UpdateSentAsync(Guid id, string sentAnswer, DateTimeOffset sentAt, CancellationToken ct = default);

    Task<(List<RagInteractionLog> Logs, int TotalCount)> GetFeedbackLogsPagedAsync(
        RagFeature? feature,
        bool? hasFeedback,
        string? userId,
        string sortBy,
        bool sortDescending,
        int pageNumber,
        int pageSize,
        CancellationToken ct = default);

    Task<RagFeedbackAggregateStats> GetFeedbackStatsAsync(RagFeature? feature, CancellationToken ct = default);
}
