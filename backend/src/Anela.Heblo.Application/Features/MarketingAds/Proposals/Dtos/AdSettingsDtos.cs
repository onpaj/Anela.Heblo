using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Dtos;

public class AdAutonomySettingDto
{
    public AdPlatform Platform { get; set; }
    public AdActionType ActionType { get; set; }
    public AdAutonomyMode Mode { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = null!;
}

public class AdLimitSettingDto
{
    public AdPlatform Platform { get; set; }
    public AdActionType ActionType { get; set; }
    public string Key { get; set; } = null!;
    public int Value { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = null!;
}

public class AdKillSwitchDto
{
    /// <summary>"Global" or a platform name.</summary>
    public string Scope { get; set; } = null!;
    public bool ExecutionEnabled { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = null!;
}

public class AdApprovalRateDto
{
    public AdPlatform Platform { get; set; }
    public AdActionType ActionType { get; set; }
    public int Decided { get; set; }
    public int ApprovedWithoutEdit { get; set; }
    /// <summary>ApprovedWithoutEdit / Decided, null when nothing was decided yet.</summary>
    public decimal? Rate { get; set; }
}
