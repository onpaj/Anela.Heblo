using Anela.Heblo.Domain.Features.DataQuality;
using MediatR;

namespace Anela.Heblo.Application.Features.DataQuality.UseCases.GetDqtRuns;

public class GetDqtRunsRequest : IRequest<GetDqtRunsResponse>
{
    public DqtTestType? TestType { get; set; }
    public DqtRunStatus? Status { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
