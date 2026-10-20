namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>Autonomy mode per platform × action type (both C1 enum names). PK (Platform, ActionType).</summary>
public class AdAutonomySetting
{
    public string Platform { get; set; } = null!;
    public string ActionType { get; set; } = null!;
    public AdAutonomyMode Mode { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = null!;
}
