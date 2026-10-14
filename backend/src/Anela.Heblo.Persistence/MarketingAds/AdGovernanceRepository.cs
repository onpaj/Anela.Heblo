using Anela.Heblo.Domain.Features.MarketingAds;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anela.Heblo.Persistence.MarketingAds;

public class AdGovernanceRepository : IAdGovernanceRepository
{
    private readonly ApplicationDbContext _context;

    private readonly ILogger<AdGovernanceRepository> _logger;

    public AdGovernanceRepository(ApplicationDbContext context)
        : this(context, NullLogger<AdGovernanceRepository>.Instance)
    {
    }

    public AdGovernanceRepository(ApplicationDbContext context, ILogger<AdGovernanceRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AdAutonomySetting>> GetAutonomySettingsAsync(CancellationToken cancellationToken) =>
        await _context.AdAutonomySettings.AsNoTracking()
            .OrderBy(s => s.Platform).ThenBy(s => s.ActionType).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AdLimitSetting>> GetLimitSettingsAsync(string? platform, string? actionType, CancellationToken cancellationToken)
    {
        var limits = _context.AdLimitSettings.AsNoTracking();
        if (platform is not null) limits = limits.Where(l => l.Platform == platform);
        if (actionType is not null) limits = limits.Where(l => l.ActionType == actionType);
        return await limits.OrderBy(l => l.Platform).ThenBy(l => l.ActionType).ThenBy(l => l.Key).ToListAsync(cancellationToken);
    }

    // Read fresh on every call (no tracking): the execution job re-reads before each action.
    public async Task<IReadOnlyList<AdKillSwitch>> GetKillSwitchesAsync(CancellationToken cancellationToken) =>
        await _context.AdKillSwitches.AsNoTracking().OrderBy(s => s.Scope).ToListAsync(cancellationToken);

    public Task<AdAutonomySetting?> FindAutonomySettingAsync(string platform, string actionType, CancellationToken cancellationToken) =>
        _context.AdAutonomySettings.FirstOrDefaultAsync(s => s.Platform == platform && s.ActionType == actionType, cancellationToken);

    public Task<AdLimitSetting?> FindLimitSettingAsync(string platform, string actionType, string key, CancellationToken cancellationToken) =>
        _context.AdLimitSettings.FirstOrDefaultAsync(l => l.Platform == platform && l.ActionType == actionType && l.Key == key, cancellationToken);

    public Task<AdKillSwitch?> FindKillSwitchAsync(string scope, CancellationToken cancellationToken) =>
        _context.AdKillSwitches.FirstOrDefaultAsync(s => s.Scope == scope, cancellationToken);

    public void AddAutonomySetting(AdAutonomySetting setting) => _context.AdAutonomySettings.Add(setting);

    public void AddLimitSetting(AdLimitSetting setting) => _context.AdLimitSettings.Add(setting);

    public void AddKillSwitch(AdKillSwitch killSwitch) => _context.AdKillSwitches.Add(killSwitch);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => _context.SaveChangesAsync(cancellationToken);

    public async Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) // includes DbUpdateConcurrencyException
        {
            _logger.LogWarning(ex, "Saving ad governance settings lost a race; discarding tracked changes");
            _context.ChangeTracker.Clear();
            return false;
        }
    }
}
