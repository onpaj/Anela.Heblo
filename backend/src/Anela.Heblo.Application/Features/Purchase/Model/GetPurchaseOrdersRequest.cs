using MediatR;

namespace Anela.Heblo.Application.Features.Purchase.Model;

public record GetPurchaseOrdersRequest(
    string? SearchTerm = null,
    string? Status = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    Guid? SupplierId = null,
    int PageNumber = 1,
    int PageSize = 20,
    string SortBy = "OrderDate",
    bool SortDescending = true
) : IRequest<GetPurchaseOrdersResponse>;