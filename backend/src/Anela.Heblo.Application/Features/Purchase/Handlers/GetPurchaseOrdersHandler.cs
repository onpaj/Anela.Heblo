using MediatR;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Application.Features.Purchase.Model;
using Anela.Heblo.Domain.Features.Purchase;

namespace Anela.Heblo.Application.Features.Purchase;

public class GetPurchaseOrdersHandler : IRequestHandler<GetPurchaseOrdersRequest, GetPurchaseOrdersResponse>
{
    private readonly ILogger<GetPurchaseOrdersHandler> _logger;
    private readonly IPurchaseOrderRepository _repository;

    public GetPurchaseOrdersHandler(
        ILogger<GetPurchaseOrdersHandler> logger,
        IPurchaseOrderRepository repository)
    {
        _logger = logger;
        _repository = repository;
    }

    public async Task<GetPurchaseOrdersResponse> Handle(GetPurchaseOrdersRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting purchase orders with filters - SearchTerm: {SearchTerm}, Status: {Status}, ActiveOrdersOnly: {ActiveOrdersOnly}, Page: {PageNumber}/{PageSize}",
            request.SearchTerm, request.Status, request.ActiveOrdersOnly, request.PageNumber, request.PageSize);

        (List<PurchaseOrder> orders, int totalCount) = await _repository.GetPaginatedAsync(
            request.SearchTerm,
            request.Status,
            request.FromDate,
            request.ToDate,
            request.SupplierId,
            request.ActiveOrdersOnly,
            request.PageNumber,
            request.PageSize,
            request.SortBy,
            request.SortDescending,
            cancellationToken);

        var totalPages = (int)Math.Ceiling((double)totalCount / request.PageSize);

        var orderSummaries = orders.Select(order => new PurchaseOrderSummaryDto
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            SupplierId = 0, // No longer using SupplierId
            SupplierName = order.SupplierName,
            OrderDate = order.OrderDate,
            ExpectedDeliveryDate = order.ExpectedDeliveryDate,
            ContactVia = order.ContactVia,
            Status = order.Status.ToString(),
            TotalAmount = order.TotalAmount,
            LineCount = order.Lines.Count,
            IsEditable = order.IsEditable,
            CreatedAt = order.CreatedAt,
            CreatedBy = order.CreatedBy
        }).ToList();

        _logger.LogInformation("Found {Count} purchase orders out of {TotalCount} total",
            orders.Count, totalCount);

        return new GetPurchaseOrdersResponse
        {
            Orders = orderSummaries,
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            TotalPages = totalPages
        };
    }
}