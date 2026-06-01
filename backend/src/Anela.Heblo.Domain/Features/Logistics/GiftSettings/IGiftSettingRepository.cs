namespace Anela.Heblo.Domain.Features.Logistics.GiftSettings;

public interface IGiftSettingRepository
{
    Task<GiftSetting> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(GiftSetting setting, CancellationToken cancellationToken = default);
}
