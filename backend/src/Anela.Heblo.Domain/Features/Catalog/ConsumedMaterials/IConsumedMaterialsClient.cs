namespace Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;

public interface IConsumedMaterialsClient
{
    Task<IReadOnlyList<ConsumedMaterialRecord>> GetConsumedAsync(DateTime dateFrom, DateTime dateTo, int limit = 0, CancellationToken cancellationToken = default);
}