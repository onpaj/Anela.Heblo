using Anela.Heblo.Application.Features.BackgroundJobs.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.BackgroundJobs.UseCases.GetRecurringJobsList;

public class GetRecurringJobsListResponse : BaseResponse
{
    public List<RecurringJobDto> Jobs { get; set; } = new List<RecurringJobDto>();
}
