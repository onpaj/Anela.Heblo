using Anela.Heblo.Domain.Entities;

namespace Anela.Heblo.Application.Features.Purchase.Contracts;

public interface ISupplierRepository
{
    Task<List<Supplier>> SearchSuppliersAsync(string searchTerm, int limit = 0, CancellationToken cancellationToken = default);
    Task<Supplier?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
}