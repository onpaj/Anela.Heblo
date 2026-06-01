
namespace Anela.Heblo.Domain.Features.Catalog.Lots
{
    public interface ILotsClient
    {
        Task<IReadOnlyList<CatalogLot>> GetAsync(string? productCode = null, int limit = 0, int skip = 0, CancellationToken cancellationToken = default);
    }
}

