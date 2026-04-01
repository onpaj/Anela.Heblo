namespace Anela.Heblo.Domain.Features.KnowledgeBase;

public class FeedbackAggregateStats
{
    public int TotalQuestions { get; set; }
    public int TotalWithFeedback { get; set; }
    public double? AvgPrecisionScore { get; set; }
    public double? AvgStyleScore { get; set; }
}
