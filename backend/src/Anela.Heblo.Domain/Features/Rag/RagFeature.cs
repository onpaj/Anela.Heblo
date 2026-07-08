namespace Anela.Heblo.Domain.Features.Rag;

/// <summary>
/// Identifies which RAG-backed feature produced an interaction log row.
/// Stored as an int column via <c>.HasConversion&lt;int&gt;()</c> — do not renumber existing values.
/// </summary>
public enum RagFeature
{
    KnowledgeBase = 0,
    SmartsuppDraftReply = 1,
}
