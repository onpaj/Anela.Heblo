namespace Anela.Heblo.Domain.Features.Article;

public sealed record ArticleFeedbackProjection(
    Guid Id,
    string? Title,
    string Topic,
    string? RequestedBy,
    DateTimeOffset CreatedAt,
    int? PrecisionScore,
    int? StyleScore,
    string? FeedbackComment);
