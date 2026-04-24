namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public class DqtRunDto
{
    public Guid Id { get; set; }
    public string TestType { get; set; } = string.Empty;
    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string TriggerType { get; set; } = string.Empty;
    public int TotalChecked { get; set; }
    public int TotalMismatches { get; set; }
    public string? ErrorMessage { get; set; }
}
