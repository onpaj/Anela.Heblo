using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.RecordPresence;

public sealed class RecordPresenceResponse : BaseResponse
{
    public RecordPresenceResponse() { }
    public RecordPresenceResponse(ErrorCodes errorCode) : base(errorCode) { }
}
