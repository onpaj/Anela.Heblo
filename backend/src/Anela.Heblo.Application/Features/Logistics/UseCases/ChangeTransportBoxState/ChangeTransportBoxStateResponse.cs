using Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxById;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;

public class ChangeTransportBoxStateResponse : BaseResponse
{
    public GetTransportBoxByIdResponse? UpdatedBox { get; set; }
}