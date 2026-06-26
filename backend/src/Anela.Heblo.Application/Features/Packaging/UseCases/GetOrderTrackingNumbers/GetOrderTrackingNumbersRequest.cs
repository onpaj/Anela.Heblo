using MediatR;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetOrderTrackingNumbers;

public class GetOrderTrackingNumbersRequest : IRequest<GetOrderTrackingNumbersResponse>
{
    public required string OrderCode { get; init; }
}
