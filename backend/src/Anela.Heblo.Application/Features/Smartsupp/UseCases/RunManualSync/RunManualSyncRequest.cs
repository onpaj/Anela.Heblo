using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.RunManualSync;

public class RunManualSyncRequest : IRequest<RunManualSyncResponse>
{
    public DateTime? Since { get; set; }
}
