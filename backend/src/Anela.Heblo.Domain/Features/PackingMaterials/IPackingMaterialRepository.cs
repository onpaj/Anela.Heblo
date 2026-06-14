using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.PackingMaterials;

public interface IPackingMaterialRepository : IRepository<PackingMaterial, int>
{
    /// <summary>
    /// Returns logs for a single packing material whose <see cref="PackingMaterialLog.CreatedAt"/>
    /// is greater than or equal to <paramref name="fromDate"/>, ordered by <c>CreatedAt</c> descending.
    /// </summary>
    /// <param name="packingMaterialId">The packing material identifier.</param>
    /// <param name="fromDate">Inclusive lower bound on <c>CreatedAt</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching logs, newest first.</returns>
    Task<IEnumerable<PackingMaterialLog>> GetRecentLogsAsync(
        int packingMaterialId,
        DateTime fromDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk variant of <see cref="GetRecentLogsAsync"/>. Returns a dictionary keyed by
    /// <see cref="PackingMaterialLog.PackingMaterialId"/>. Materials with no qualifying logs in the window
    /// are absent from the result — callers must treat absence as an empty list. Each material's logs
    /// are ordered by <c>CreatedAt</c> descending.
    /// </summary>
    /// <param name="packingMaterialIds">
    /// The packing material identifiers to load logs for. When empty, the method returns an empty
    /// dictionary without executing a database query.
    /// </param>
    /// <param name="fromDate">Inclusive lower bound on <c>CreatedAt</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dictionary of matching logs grouped by material id.</returns>
    Task<IReadOnlyDictionary<int, IReadOnlyList<PackingMaterialLog>>> GetRecentLogsForMaterialsAsync(
        IEnumerable<int> packingMaterialIds,
        DateTime fromDate,
        CancellationToken cancellationToken = default);

    Task<bool> HasDailyProcessingBeenRunAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task<IEnumerable<PackingMaterial>> GetAllWithAllocationsAsync(CancellationToken cancellationToken = default);
    Task<PackingMaterial?> GetByIdWithAllocationsAsync(int id, CancellationToken cancellationToken = default);
    Task AddConsumptionRowsAsync(IEnumerable<PackingMaterialConsumption> rows, CancellationToken cancellationToken = default);
    Task<IEnumerable<PackingMaterialConsumption>> GetConsumptionsByDateAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task AddDailyRunAsync(PackingMaterialDailyRun run, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<MaterialConsumptionHistoryRecord> Items, int TotalCount)> GetConsumptionHistoryAsync(
        MaterialConsumptionHistoryFilter filter,
        int skip,
        int take,
        bool ascending,
        CancellationToken cancellationToken = default);
}
