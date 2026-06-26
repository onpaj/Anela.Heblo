namespace Anela.Heblo.Application.Features.FeatureFlags.Contracts;

public class FlagStatusDto
{
    public string Key { get; set; } = "";
    public string Description { get; set; } = "";
    public bool CurrentValue { get; set; }
    public bool IsOverridden { get; set; }
    public bool DefaultValue { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
