using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.FeatureFlags.UseCases.ClearFlagOverride;

public class ClearFlagOverrideRequest : IRequest<ClearFlagOverrideResponse>
{
    public string Key { get; set; } = "";
}

public class ClearFlagOverrideResponse : BaseResponse
{
    public ClearFlagOverrideResponse() { }
    public ClearFlagOverrideResponse(ErrorCodes errorCode) : base(errorCode) { }
}
