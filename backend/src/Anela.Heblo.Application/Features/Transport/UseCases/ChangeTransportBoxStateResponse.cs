using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Transport.UseCases;

public class ChangeTransportBoxStateResponse : BaseResponse
{
    public string? ErrorMessage { get; set; }
    public GetTransportBoxByIdResponse? UpdatedBox { get; set; }
}