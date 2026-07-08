using Anela.Heblo.Domain.Features.Rag;

namespace Anela.Heblo.Application.Shared.Rag;

/// <summary>
/// Scoped accumulator that captures the artifacts of a single RAG answer generation as they become
/// available across the (nested) MediatR handlers running in the same DI scope: the retrieval output
/// (expanded query + chunks) from <c>SearchDocumentsHandler</c>, and the question / rendered prompt /
/// answer from the feature handler. A logging pipeline behavior reads it after the handler completes
/// and persists a <see cref="RagInteractionLog"/>.
/// </summary>
public interface IRagInteractionRecorder
{
    RagFeature? Feature { get; }
    string? Question { get; }
    string? ExpandedQuery { get; }
    int TopK { get; }
    IReadOnlyList<RagRetrievedChunk> Chunks { get; }
    string? SystemPrompt { get; }
    string? Answer { get; }
    string? ConversationId { get; }
    string? Topic { get; }

    /// <summary>True once a feature handler has recorded question/prompt/answer for this interaction.</summary>
    bool HasInteraction { get; }

    void RecordRetrieval(string expandedQuery, int topK, IReadOnlyList<RagRetrievedChunk> chunks);

    void RecordInteraction(
        RagFeature feature,
        string question,
        string systemPrompt,
        string answer,
        string? conversationId = null,
        string? topic = null);
}

public class RagInteractionRecorder : IRagInteractionRecorder
{
    public RagFeature? Feature { get; private set; }
    public string? Question { get; private set; }
    public string? ExpandedQuery { get; private set; }
    public int TopK { get; private set; }
    public IReadOnlyList<RagRetrievedChunk> Chunks { get; private set; } = [];
    public string? SystemPrompt { get; private set; }
    public string? Answer { get; private set; }
    public string? ConversationId { get; private set; }
    public string? Topic { get; private set; }

    public bool HasInteraction { get; private set; }

    public void RecordRetrieval(string expandedQuery, int topK, IReadOnlyList<RagRetrievedChunk> chunks)
    {
        ExpandedQuery = expandedQuery;
        TopK = topK;
        Chunks = chunks;
    }

    public void RecordInteraction(
        RagFeature feature,
        string question,
        string systemPrompt,
        string answer,
        string? conversationId = null,
        string? topic = null)
    {
        Feature = feature;
        Question = question;
        SystemPrompt = systemPrompt;
        Answer = answer;
        ConversationId = conversationId;
        Topic = topic;
        HasInteraction = true;
    }
}
