using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.RunManualSync;

public class RunManualSyncResponse : BaseResponse
{
    public int ConversationsProcessed { get; set; }
    public int MessagesProcessed { get; set; }
    public int ConversationsReconciled { get; set; }
    public int ConversationsClosedRemotely { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }

    public RunManualSyncResponse() { }

    public RunManualSyncResponse(ErrorCodes errorCode) : base(errorCode) { }
}
