using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateLot;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.ListLots;

public class ListLotsHandler : IRequestHandler<ListLotsRequest, ListLotsResponse>
{
    private readonly ILogger<ListLotsHandler> _logger;
    private readonly ILotRepository _lotRepository;

    public ListLotsHandler(ILogger<ListLotsHandler> logger, ILotRepository lotRepository)
    {
        _logger = logger;
        _lotRepository = lotRepository;
    }

    public async Task<ListLotsResponse> Handle(ListLotsRequest request, CancellationToken cancellationToken)
    {
        var result = await _lotRepository.GetPaginatedAsync(
            request.MaterialCode,
            request.ExpirationFrom,
            request.ExpirationTo,
            request.Page,
            request.PageSize,
            cancellationToken);

        return new ListLotsResponse
        {
            Lots = result.Items.Select(CreateLotHandler.MapToDto).ToList(),
            TotalCount = result.TotalCount,
            PageNumber = result.PageNumber,
            PageSize = result.PageSize
        };
    }
}
