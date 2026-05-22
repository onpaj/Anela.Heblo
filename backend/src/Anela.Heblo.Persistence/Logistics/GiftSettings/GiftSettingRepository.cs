using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Logistics.GiftSettings;

public class GiftSettingRepository : IGiftSettingRepository
{
    private readonly ApplicationDbContext _context;

    public GiftSettingRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<GiftSetting> GetAsync(CancellationToken cancellationToken = default)
    {
        return await _context.GiftSettings.FirstOrDefaultAsync(cancellationToken)
            ?? GiftSetting.CreateDefault();
    }

    public async Task SaveAsync(GiftSetting setting, CancellationToken cancellationToken = default)
    {
        var existing = await _context.GiftSettings.FirstOrDefaultAsync(cancellationToken);
        if (existing is null)
        {
            _context.GiftSettings.Add(setting);
        }
        else
        {
            existing.Update(setting.IsEnabled, setting.ThresholdCzk, setting.Text, setting.ModifiedBy ?? string.Empty);
        }
        await _context.SaveChangesAsync(cancellationToken);
    }
}
