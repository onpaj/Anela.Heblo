using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.BackgroundJobs.UseCases.UpdateRecurringJobStatus;

public class UpdateRecurringJobStatusResponse : BaseResponse
{
    public string JobName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public DateTime LastModifiedAt { get; set; }
    public string LastModifiedBy { get; set; } = string.Empty;

    public UpdateRecurringJobStatusResponse() : base() { }
    public UpdateRecurringJobStatusResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}
