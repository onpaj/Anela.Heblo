using Anela.Heblo.Domain.Features.Rag;

namespace Anela.Heblo.Application.Shared.Rag;

/// <summary>
/// One row of the RAG feedback / eval browser. Shared by KnowledgeBase and Smartsupp draft-reply feedback
/// lists (both back the same <see cref="RagInteractionLog"/> table).
/// </summary>
public class RagFeedbackLogSummary
{
    public Guid Id { get; set; }
    public RagFeature Feature { get; set; }
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public string? ExpandedQuery { get; set; }
    public string SystemPrompt { get; set; } = string.Empty;
    public List<RagFeedbackChunkDto> RetrievedChunks { get; set; } = [];
    public int TopK { get; set; }
    public int SourceCount { get; set; }
    public long DurationMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? UserId { get; set; }
    public string? UserName { get; set; }

    // Smartsupp-only (null for KnowledgeBase)
    public string? ConversationId { get; set; }
    public string? Topic { get; set; }
    public string? SentAnswer { get; set; }
    public bool? WasEdited { get; set; }
    public DateTimeOffset? SentAt { get; set; }

    // Human feedback
    public int? PrecisionScore { get; set; }
    public int? StyleScore { get; set; }
    public string? FeedbackComment { get; set; }
    public bool HasFeedback => PrecisionScore.HasValue || StyleScore.HasValue;
}
