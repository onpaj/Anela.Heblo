using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.OpenOrResumeBoxByCode;

public class OpenOrResumeBoxByCodeResponse : BaseResponse
{
    public TransportBoxDto? TransportBox { get; set; }
    public bool Resumed { get; set; }

    public OpenOrResumeBoxByCodeResponse() : base()
    {
    }

    public OpenOrResumeBoxByCodeResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters)
    {
    }
}
