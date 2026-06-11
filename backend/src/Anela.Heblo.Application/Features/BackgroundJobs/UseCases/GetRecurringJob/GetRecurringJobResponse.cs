using Anela.Heblo.Application.Features.BackgroundJobs.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.BackgroundJobs.UseCases.GetRecurringJob;

public class GetRecurringJobResponse : BaseResponse
{
    public RecurringJobDto? Job { get; set; }

    public GetRecurringJobResponse() : base() { }
    public GetRecurringJobResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}
