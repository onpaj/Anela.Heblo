using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.PackingMaterials;

public interface IPackingMaterialRepository : IRepository<PackingMaterial, int>
{
    Task<IEnumerable<PackingMaterial>> GetAllWithLogsAsync(CancellationToken cancellationToken = default);
    Task<PackingMaterial?> GetByIdWithLogsAsync(int id, CancellationToken cancellationToken = default);
    Task<IEnumerable<PackingMaterialLog>> GetRecentLogsAsync(int packingMaterialId, DateTime fromDate, CancellationToken cancellationToken = default);
    Task<bool> HasDailyProcessingBeenRunAsync(DateOnly date, CancellationToken cancellationToken = default);
}