namespace Anela.Heblo.Domain.Features.FeatureFlags;

public class FeatureFlagOverride
{
    public string Key { get; set; } = "";
    public bool IsEnabled { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
}
