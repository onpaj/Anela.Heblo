using MediatR;

namespace Anela.Heblo.Application.Features.BackgroundJobs.UseCases.UpdateRecurringJobCron;

public class UpdateRecurringJobCronRequest : IRequest<UpdateRecurringJobCronResponse>
{
    public string JobName { get; set; } = string.Empty;
    public string CronExpression { get; set; } = string.Empty;
}
