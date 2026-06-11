using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetOrderTrackingNumber;

public class GetOrderTrackingNumberResponse : BaseResponse
{
    public string? TrackingNumber { get; set; }
}
