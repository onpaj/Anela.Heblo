namespace Anela.Heblo.Domain.Features.Purchase;

public interface ISupplierRepository
{
    Task<List<Supplier>> SearchSuppliersAsync(string searchTerm, int limit = 0, CancellationToken cancellationToken = default);
    Task<Supplier?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
}
