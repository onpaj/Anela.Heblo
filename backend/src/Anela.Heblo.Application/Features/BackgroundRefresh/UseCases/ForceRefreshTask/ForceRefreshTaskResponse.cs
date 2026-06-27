using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.ForceRefreshTask;

public class ForceRefreshTaskResponse : BaseResponse
{
    public bool NotFound { get; set; }
    public string? ErrorMessage { get; set; }
}
