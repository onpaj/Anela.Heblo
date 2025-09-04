using Anela.Heblo.Application.Features.Purchase.Contracts;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.SearchSuppliers;

public class SearchSuppliersResponse
{
    public List<SupplierDto> Suppliers { get; set; } = new();
}