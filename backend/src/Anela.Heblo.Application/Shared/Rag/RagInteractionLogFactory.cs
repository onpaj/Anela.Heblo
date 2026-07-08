using System.Text.Json;
using Anela.Heblo.Domain.Features.Rag;

namespace Anela.Heblo.Application.Shared.Rag;

/// <summary>
/// Builds a <see cref="RagInteractionLog"/> from a completed <see cref="IRagInteractionRecorder"/>.
/// Shared by the KnowledgeBase and Smartsupp logging pipeline behaviors.
/// </summary>
public static class RagInteractionLogFactory
{
    public static RagInteractionLog Build(
        IRagInteractionRecorder recorder,
        string? userId,
        long durationMs,
        DateTimeOffset createdAt)
    {
        return new RagInteractionLog
        {
            Id = Guid.NewGuid(),
            Feature = recorder.Feature ?? RagFeature.KnowledgeBase,
            CreatedAt = createdAt,
            UserId = userId,
            Question = recorder.Question ?? string.Empty,
            ExpandedQuery = recorder.ExpandedQuery,
            TopK = recorder.TopK,
            SourceCount = recorder.Chunks.Count,
            RetrievedChunksJson = JsonSerializer.Serialize(recorder.Chunks),
            SystemPrompt = recorder.SystemPrompt ?? string.Empty,
            Answer = recorder.Answer ?? string.Empty,
            ConversationId = recorder.ConversationId,
            Topic = recorder.Topic,
            DurationMs = durationMs,
        };
    }
}
