namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>One limit value (spec 6.3) per platform × action type × key. Keys: AdLimitKeys in Application.</summary>
public class AdLimitSetting
{
    public string Platform { get; set; } = null!;
    public string ActionType { get; set; } = null!;
    public string Key { get; set; } = null!;
    public int Value { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = null!;
}
