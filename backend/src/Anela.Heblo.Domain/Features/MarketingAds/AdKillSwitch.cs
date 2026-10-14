namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>Persisted execution switch. Scope is "Global" or a platform name (an override that can only disable).</summary>
public class AdKillSwitch
{
    public const string GlobalScope = "Global";

    public string Scope { get; set; } = null!;
    public bool ExecutionEnabled { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = null!;
}
