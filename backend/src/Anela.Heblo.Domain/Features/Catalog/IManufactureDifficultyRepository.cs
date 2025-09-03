namespace Anela.Heblo.Domain.Features.Catalog;

public interface IManufactureDifficultyRepository
{
    Task<ManufactureDifficultySetting?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<List<ManufactureDifficultySetting>> ListAsync(string? productCode = null, DateTime? asOfDate = null, CancellationToken cancellationToken = default);
    Task<ManufactureDifficultySetting?> FindAsync(string productCode, DateTime asOfDate, CancellationToken cancellationToken = default);
    Task<ManufactureDifficultySetting> CreateAsync(ManufactureDifficultySetting setting, CancellationToken cancellationToken = default);
    Task<ManufactureDifficultySetting> UpdateAsync(ManufactureDifficultySetting setting, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> HasOverlapAsync(string productCode, DateTime? validFrom, DateTime? validTo, int? excludeId = null, CancellationToken cancellationToken = default);
}