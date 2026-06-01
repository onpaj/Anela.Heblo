using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxByCode;

public class GetTransportBoxByCodeResponse : BaseResponse
{
    public TransportBoxDto? TransportBox { get; set; }

    public GetTransportBoxByCodeResponse() : base()
    {
    }

    public GetTransportBoxByCodeResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters)
    {
    }

    public GetTransportBoxByCodeResponse(Exception ex) : base(ex)
    {
    }
}