using AutoMapper;
using MediatR;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrders;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrder;

public class GetManufactureOrderHandler : IRequestHandler<GetManufactureOrderRequest, GetManufactureOrderResponse>
{
    private readonly IManufactureOrderRepository _repository;
    private readonly IMapper _mapper;

    public GetManufactureOrderHandler(IManufactureOrderRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<GetManufactureOrderResponse> Handle(GetManufactureOrderRequest request, CancellationToken cancellationToken)
    {
        var order = await _repository.GetOrderByIdAsync(request.Id, cancellationToken);

        if (order == null)
        {
            return new GetManufactureOrderResponse(Application.Shared.ErrorCodes.ResourceNotFound,
                new Dictionary<string, string> { { "id", request.Id.ToString() } });
        }

        var orderDto = _mapper.Map<ManufactureOrderDto>(order);

        return new GetManufactureOrderResponse
        {
            Order = orderDto
        };
    }
}