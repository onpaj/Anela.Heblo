using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.PackingMaterials;

public interface IPackingMaterialRepository : IRepository<PackingMaterial, int>
{
    Task<IEnumerable<PackingMaterialLog>> GetRecentLogsAsync(int packingMaterialId, DateTime fromDate, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<int, IReadOnlyList<PackingMaterialLog>>> GetRecentLogsForMaterialsAsync(
        IEnumerable<int> packingMaterialIds,
        DateTime fromDate,
        CancellationToken cancellationToken = default);
    Task<bool> HasDailyProcessingBeenRunAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task<IEnumerable<PackingMaterial>> GetAllWithAllocationsAsync(CancellationToken cancellationToken = default);
    Task<PackingMaterial?> GetByIdWithAllocationsAsync(int id, CancellationToken cancellationToken = default);
    Task AddConsumptionRowsAsync(IEnumerable<PackingMaterialConsumption> rows, CancellationToken cancellationToken = default);
    Task<IEnumerable<PackingMaterialConsumption>> GetConsumptionsByDateAsync(DateOnly date, CancellationToken cancellationToken = default);
}
