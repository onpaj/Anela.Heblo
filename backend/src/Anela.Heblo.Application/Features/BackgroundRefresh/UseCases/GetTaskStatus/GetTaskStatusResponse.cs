using Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetTaskStatus;

public class GetTaskStatusResponse : BaseResponse
{
    public bool Found { get; set; }
    public RefreshTaskStatusDto? Status { get; set; }
}
