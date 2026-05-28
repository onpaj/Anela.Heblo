using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseOrderHistory;

public record GetPurchaseOrderHistoryRequest(int Id) : IRequest<ListResponse<PurchaseOrderHistoryDto>>;
