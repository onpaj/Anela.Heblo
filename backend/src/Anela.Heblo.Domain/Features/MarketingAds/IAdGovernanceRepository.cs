namespace Anela.Heblo.Domain.Features.MarketingAds;

public interface IAdGovernanceRepository
{
    Task<IReadOnlyList<AdAutonomySetting>> GetAutonomySettingsAsync(CancellationToken cancellationToken);   // AsNoTracking
    Task<IReadOnlyList<AdLimitSetting>> GetLimitSettingsAsync(string? platform, string? actionType, CancellationToken cancellationToken); // AsNoTracking
    Task<IReadOnlyList<AdKillSwitch>> GetKillSwitchesAsync(CancellationToken cancellationToken);           // AsNoTracking
    Task<AdAutonomySetting?> FindAutonomySettingAsync(string platform, string actionType, CancellationToken cancellationToken); // tracked
    Task<AdLimitSetting?> FindLimitSettingAsync(string platform, string actionType, string key, CancellationToken cancellationToken); // tracked
    Task<AdKillSwitch?> FindKillSwitchAsync(string scope, CancellationToken cancellationToken);             // tracked
    void AddAutonomySetting(AdAutonomySetting setting);
    void AddLimitSetting(AdLimitSetting setting);
    void AddKillSwitch(AdKillSwitch killSwitch);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
