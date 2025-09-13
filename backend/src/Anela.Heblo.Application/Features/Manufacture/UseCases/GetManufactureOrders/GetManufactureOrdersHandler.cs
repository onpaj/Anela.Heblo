using AutoMapper;
using MediatR;
using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrders;

public class GetManufactureOrdersHandler : IRequestHandler<GetManufactureOrdersRequest, GetManufactureOrdersResponse>
{
    private readonly IManufactureOrderRepository _repository;
    private readonly IMapper _mapper;

    public GetManufactureOrdersHandler(IManufactureOrderRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<GetManufactureOrdersResponse> Handle(GetManufactureOrdersRequest request, CancellationToken cancellationToken)
    {
        var orders = await _repository.GetOrdersAsync(
            request.State,
            request.DateFrom,
            request.DateTo,
            request.ResponsiblePerson,
            request.OrderNumber,
            request.ProductCode,
            cancellationToken);

        var orderDtos = _mapper.Map<List<ManufactureOrderDto>>(orders);

        return new GetManufactureOrdersResponse
        {
            Orders = orderDtos
        };
    }
}