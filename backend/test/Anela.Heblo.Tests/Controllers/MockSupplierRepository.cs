using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Domain.Entities;
using Anela.Heblo.Domain.Features.Purchase;

namespace Anela.Heblo.Tests.Controllers;

public class MockSupplierRepository : ISupplierRepository
{
    private static readonly List<Supplier> MockSuppliers = new()
    {
        new Supplier
        {
            Id = 1,
            Name = "Test Supplier",
            Code = "SUP001",
            Email = "test@supplier.com",
            Phone = "+420123456789",
            Note = "Test supplier for integration tests",
            Url = "https://www.testsupplier.com"
        },
        new Supplier
        {
            Id = 2,
            Name = "Another Supplier",
            Code = "SUP002",
            Email = "another@supplier.com",
            Phone = "+420987654321",
            Note = "Another test supplier",
            Url = "https://www.anothersupplier.com"
        }
    };

    public Task<List<Supplier>> SearchSuppliersAsync(string searchTerm, int limit = 10, CancellationToken cancellationToken = default)
    {
        var filteredSuppliers = MockSuppliers.AsQueryable();

        if (!string.IsNullOrEmpty(searchTerm))
        {
            filteredSuppliers = filteredSuppliers
                .Where(s => s.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                           s.Code.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
        }

        var result = filteredSuppliers
            .Take(limit)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<Supplier?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var supplier = MockSuppliers.FirstOrDefault(s => s.Id == id);
        return Task.FromResult(supplier);
    }

    public Task<Supplier?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var supplier = MockSuppliers.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(supplier);
    }
}