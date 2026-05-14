using AutoMapper;
using MediatR;
using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufacturedInventory;

public class GetManufacturedInventoryHandler : IRequestHandler<GetManufacturedInventoryRequest, GetManufacturedInventoryResponse>
{
    private readonly IManufacturedProductInventoryRepository _repository;
    private readonly IMapper _mapper;

    public GetManufacturedInventoryHandler(IManufacturedProductInventoryRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<GetManufacturedInventoryResponse> Handle(GetManufacturedInventoryRequest request, CancellationToken cancellationToken)
    {
        var filter = new ManufacturedInventoryFilter
        {
            Search = request.Search,
            OnlyWithStock = request.OnlyWithStock,
            ManufactureOrderId = request.ManufactureOrderId,
            Page = request.Page,
            PageSize = request.PageSize,
        };

        var (items, totalCount) = await _repository.GetPagedListAsync(filter, cancellationToken);

        var dtos = _mapper.Map<List<ManufacturedProductInventoryItemDto>>(items);

        return new GetManufacturedInventoryResponse
        {
            Items = dtos,
            TotalCount = totalCount,
        };
    }
}
