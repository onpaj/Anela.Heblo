namespace Anela.Heblo.Domain.Features.Rag;

public class RagFeedbackAggregateStats
{
    public int TotalQuestions { get; set; }
    public int TotalWithFeedback { get; set; }
    public double? AvgPrecisionScore { get; set; }
    public double? AvgStyleScore { get; set; }
}
