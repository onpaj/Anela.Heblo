using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetOrderTrackingNumbers;

public class GetOrderTrackingNumbersResponse : BaseResponse
{
    public List<string> TrackingNumbers { get; set; } = [];
}
