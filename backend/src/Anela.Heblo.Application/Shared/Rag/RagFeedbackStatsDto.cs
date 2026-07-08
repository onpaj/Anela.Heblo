namespace Anela.Heblo.Application.Shared.Rag;

public class RagFeedbackStatsDto
{
    public int TotalQuestions { get; set; }
    public int TotalWithFeedback { get; set; }
    public double? AvgPrecisionScore { get; set; }
    public double? AvgStyleScore { get; set; }
}
