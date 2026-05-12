namespace Anela.Heblo.Domain.Features.Leaflet;

public sealed record LeafletFeedbackStats(
    int TotalGenerations,
    int TotalWithFeedback,
    double? AvgPrecisionScore,
    double? AvgStyleScore);
