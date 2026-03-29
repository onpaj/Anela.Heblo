using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.BackgroundJobs.UseCases.UpdateRecurringJobCron;

public class UpdateRecurringJobCronResponse : BaseResponse
{
    public string JobName { get; set; } = string.Empty;
    public string CronExpression { get; set; } = string.Empty;
    public DateTime LastModifiedAt { get; set; }
    public string LastModifiedBy { get; set; } = string.Empty;

    public UpdateRecurringJobCronResponse() : base() { }
    public UpdateRecurringJobCronResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
