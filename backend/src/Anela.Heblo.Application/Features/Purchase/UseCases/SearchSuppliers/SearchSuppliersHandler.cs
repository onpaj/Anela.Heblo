using Anela.Heblo.Application.Features.Purchase.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.SearchSuppliers;

public class SearchSuppliersHandler : IRequestHandler<SearchSuppliersRequest, SearchSuppliersResponse>
{
    private readonly ISupplierRepository _supplierRepository;

    public SearchSuppliersHandler(ISupplierRepository supplierRepository)
    {
        _supplierRepository = supplierRepository;
    }

    public async Task<SearchSuppliersResponse> Handle(SearchSuppliersRequest request, CancellationToken cancellationToken)
    {
        var suppliers = await _supplierRepository.SearchSuppliersAsync(
            request.SearchTerm,
            request.Limit,
            cancellationToken);

        return new SearchSuppliersResponse
        {
            Suppliers = suppliers.Select(s => new SupplierDto
            {
                Id = s.Id,
                Name = s.Name,
                Code = s.Code,
                Note = s.Note,
                Email = s.Email,
                Phone = s.Phone,
                Url = s.Url
            }).ToList()
        };
    }
}