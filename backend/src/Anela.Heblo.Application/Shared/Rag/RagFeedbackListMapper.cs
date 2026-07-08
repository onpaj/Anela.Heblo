using System.Text.Json;
using Anela.Heblo.Domain.Features.Rag;

namespace Anela.Heblo.Application.Shared.Rag;

/// <summary>
/// Maps a <see cref="RagInteractionLog"/> into the feedback-browser DTO shared by KnowledgeBase and
/// Smartsupp, deserializing the jsonb chunk payload. Used by both feedback-list handlers to avoid drift.
/// </summary>
public static class RagFeedbackListMapper
{
    public static RagFeedbackLogSummary ToSummary(RagInteractionLog log, string? userName)
    {
        return new RagFeedbackLogSummary
        {
            Id = log.Id,
            Feature = log.Feature,
            Question = log.Question,
            Answer = log.Answer,
            ExpandedQuery = log.ExpandedQuery,
            SystemPrompt = log.SystemPrompt,
            RetrievedChunks = ParseChunks(log.RetrievedChunksJson),
            TopK = log.TopK,
            SourceCount = log.SourceCount,
            DurationMs = log.DurationMs,
            CreatedAt = log.CreatedAt,
            UserId = log.UserId,
            UserName = userName,
            ConversationId = log.ConversationId,
            Topic = log.Topic,
            SentAnswer = log.SentAnswer,
            WasEdited = log.WasEdited,
            SentAt = log.SentAt,
            PrecisionScore = log.PrecisionScore,
            StyleScore = log.StyleScore,
            FeedbackComment = log.FeedbackComment,
        };
    }

    public static RagFeedbackStatsDto ToStats(RagFeedbackAggregateStats stats)
    {
        return new RagFeedbackStatsDto
        {
            TotalQuestions = stats.TotalQuestions,
            TotalWithFeedback = stats.TotalWithFeedback,
            AvgPrecisionScore = stats.AvgPrecisionScore,
            AvgStyleScore = stats.AvgStyleScore,
        };
    }

    private static List<RagFeedbackChunkDto> ParseChunks(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            var chunks = JsonSerializer.Deserialize<List<RagRetrievedChunk>>(json);
            if (chunks is null)
                return [];

            return chunks.Select(c => new RagFeedbackChunkDto
            {
                ChunkId = c.ChunkId,
                DocumentId = c.DocumentId,
                Filename = c.Filename,
                Score = c.Score,
                Content = c.Content,
            }).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
