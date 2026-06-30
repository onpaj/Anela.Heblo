using MediatR;

namespace Anela.Heblo.Application.Features.ExpeditionList.UseCases.PrintExpeditionOrder;

public class PrintExpeditionOrderRequest : IRequest<PrintExpeditionOrderResponse>
{
    public string OrderCode { get; set; } = null!;
}
