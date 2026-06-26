using MediatR;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.ResetOrderShipment;

public class ResetOrderShipmentRequest : IRequest<ResetOrderShipmentResponse>
{
    public string OrderCode { get; set; } = null!;
    public int NumberOfPackages { get; set; } = 1;
}
