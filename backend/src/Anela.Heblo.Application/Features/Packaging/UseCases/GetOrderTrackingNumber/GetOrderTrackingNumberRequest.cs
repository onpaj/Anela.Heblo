using MediatR;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetOrderTrackingNumber;

public class GetOrderTrackingNumberRequest : IRequest<GetOrderTrackingNumberResponse>
{
    public string OrderCode { get; set; } = null!;
}
