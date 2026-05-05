namespace Anela.Heblo.Domain.Features.Leaflet;

public class LeafletFeedbackStats
{
    public int TotalGenerations { get; set; }
    public int TotalWithFeedback { get; set; }
    public double? AvgPrecisionScore { get; set; }
    public double? AvgStyleScore { get; set; }
}
