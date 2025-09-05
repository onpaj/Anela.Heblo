using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Transport.UseCases;

public class ChangeTransportBoxStateResponse : BaseResponse
{
    public GetTransportBoxByIdResponse? UpdatedBox { get; set; }
}