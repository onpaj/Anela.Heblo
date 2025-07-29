using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Application.Domain.Catalog;

public interface ICatalogRepository : IReadOnlyRepository<CatalogAggregate, string>
{
}