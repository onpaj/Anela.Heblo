using Anela.Heblo.Application.Features.Packaging.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetCarrierLabelOffsets;

public class GetCarrierLabelOffsetsResponse : BaseResponse
{
    public List<CarrierLabelOffsetDto> Offsets { get; set; } = new();
}
