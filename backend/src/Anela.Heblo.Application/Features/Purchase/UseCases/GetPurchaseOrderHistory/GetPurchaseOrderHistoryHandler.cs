using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Purchase;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseOrderHistory;

public class GetPurchaseOrderHistoryHandler : IRequestHandler<GetPurchaseOrderHistoryRequest, ListResponse<PurchaseOrderHistoryDto>>
{
    private readonly ILogger<GetPurchaseOrderHistoryHandler> _logger;
    private readonly IPurchaseOrderRepository _repository;

    public GetPurchaseOrderHistoryHandler(
        ILogger<GetPurchaseOrderHistoryHandler> logger,
        IPurchaseOrderRepository repository)
    {
        _logger = logger;
        _repository = repository;
    }

    public async Task<ListResponse<PurchaseOrderHistoryDto>> Handle(GetPurchaseOrderHistoryRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting purchase order history for ID {Id}", request.Id);

        var exists = await _repository.ExistsAsync(request.Id, cancellationToken);
        if (!exists)
        {
            _logger.LogWarning("Purchase order not found for ID {Id}", request.Id);
            return new ListResponse<PurchaseOrderHistoryDto>(
                ErrorCodes.PurchaseOrderNotFound,
                new Dictionary<string, string> { { "Id", request.Id.ToString() } });
        }

        var history = await _repository.GetHistoryAsync(request.Id, cancellationToken);

        var items = history
            .Select(h => new PurchaseOrderHistoryDto
            {
                Id = h.Id,
                Action = h.Action,
                OldValue = h.OldValue,
                NewValue = h.NewValue,
                ChangedAt = h.ChangedAt,
                ChangedBy = h.ChangedBy,
            })
            .ToList();

        _logger.LogInformation("Returning {Count} history entries for purchase order {Id}", items.Count, request.Id);

        return new ListResponse<PurchaseOrderHistoryDto>
        {
            Items = items,
            TotalCount = items.Count,
        };
    }
}
