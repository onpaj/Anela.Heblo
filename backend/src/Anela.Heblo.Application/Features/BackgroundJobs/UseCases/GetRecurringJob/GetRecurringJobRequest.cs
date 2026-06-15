using MediatR;

namespace Anela.Heblo.Application.Features.BackgroundJobs.UseCases.GetRecurringJob;

public class GetRecurringJobRequest : IRequest<GetRecurringJobResponse>
{
    public string JobName { get; set; } = string.Empty;
}
