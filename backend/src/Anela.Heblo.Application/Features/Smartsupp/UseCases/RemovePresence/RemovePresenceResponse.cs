using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.RemovePresence;

public sealed class RemovePresenceResponse : BaseResponse
{
    public RemovePresenceResponse() { }
    public RemovePresenceResponse(ErrorCodes errorCode) : base(errorCode) { }
}
