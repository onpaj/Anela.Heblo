using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.BackgroundJobs.UseCases.TriggerRecurringJob;

public class TriggerRecurringJobResponse : BaseResponse
{
    public string? JobId { get; set; }

    public TriggerRecurringJobResponse() : base() { }

    public TriggerRecurringJobResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
