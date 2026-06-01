using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.SearchSuppliers;

public class SearchSuppliersResponse : BaseResponse
{
    public List<SupplierDto> Suppliers { get; set; } = new();
}