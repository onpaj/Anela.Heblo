namespace Anela.Heblo.Domain.Features.Article;

public sealed record ArticleFeedbackStats(
    int TotalArticles,
    int TotalWithFeedback,
    double? AvgPrecisionScore,
    double? AvgStyleScore);
