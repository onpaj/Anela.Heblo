namespace Anela.Heblo.Application.Features.Purchase.Contracts;

public interface IMaterialCatalogService
{
    Task<MaterialInfo?> GetByIdAsync(string productCode, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, MaterialInfo>> GetByIdsAsync(
        IEnumerable<string> productCodes,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MaterialInfo>> GetAllAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<MaterialStockSnapshot>> GetStockAnalysisSnapshotsAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MaterialBomReference>> GetMaterialsWithBomAsync(
        CancellationToken cancellationToken);
}
