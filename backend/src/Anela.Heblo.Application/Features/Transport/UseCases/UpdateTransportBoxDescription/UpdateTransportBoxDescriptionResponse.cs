using Anela.Heblo.Application.Features.Transport.UseCases.GetTransportBoxById;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Transport.UseCases.UpdateTransportBoxDescription;

public class UpdateTransportBoxDescriptionResponse : BaseResponse
{
    public GetTransportBoxByIdResponse? UpdatedBox { get; set; }

    public UpdateTransportBoxDescriptionResponse() : base()
    {
    }

    public UpdateTransportBoxDescriptionResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) 
        : base(errorCode, parameters)
    {
    }
}