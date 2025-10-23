using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetStockTakingJobStatus;

public class GetStockTakingJobStatusRequest : IRequest<GetStockTakingJobStatusResponse>
{
    public string JobId { get; set; } = string.Empty;
}