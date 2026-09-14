using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseOrderHistory;

public class GetPurchaseOrderHistoryRequest : IRequest<ListResponse<PurchaseOrderHistoryDto>>
{
    public int Id { get; set; }
}
