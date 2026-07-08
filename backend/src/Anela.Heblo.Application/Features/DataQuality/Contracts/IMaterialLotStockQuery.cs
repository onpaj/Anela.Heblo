namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public interface IMaterialLotStockQuery
{
    Task<IReadOnlyList<MaterialLotStockSnapshot>> GetMaterialsWithExpirationAsync(
        CancellationToken cancellationToken = default);
}
