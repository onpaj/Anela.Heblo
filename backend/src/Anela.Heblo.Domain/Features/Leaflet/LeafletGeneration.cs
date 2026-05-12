namespace Anela.Heblo.Domain.Features.Leaflet;

public class LeafletGeneration
{
    public Guid Id { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string Length { get; set; } = string.Empty;
    public string FinalMarkdown { get; set; } = string.Empty;
    public int KbSourceCount { get; set; }
    public int LeafletSourceCount { get; set; }
    public long DurationMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? UserId { get; set; }
    public int? PrecisionScore { get; set; }
    public int? StyleScore { get; set; }
    public string? FeedbackComment { get; set; }
}
