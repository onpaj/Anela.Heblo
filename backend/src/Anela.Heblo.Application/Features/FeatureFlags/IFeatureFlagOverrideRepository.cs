using Anela.Heblo.Domain.Features.FeatureFlags;

namespace Anela.Heblo.Application.Features.FeatureFlags;

public interface IFeatureFlagOverrideRepository
{
    Task<Dictionary<string, bool>> GetAllAsDictionaryAsync(CancellationToken ct = default);
    Task<bool?> GetByKeyAsync(string key, CancellationToken ct = default);
    Task<IReadOnlyList<FeatureFlagOverride>> GetAllAsync(CancellationToken ct = default);
    Task UpsertAsync(string key, bool isEnabled, string updatedBy, CancellationToken ct = default);
    Task<bool> DeleteAsync(string key, CancellationToken ct = default);
}
