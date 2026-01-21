using Anela.Heblo.Domain.Features.Catalog.Stock;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetStockUpOperationsSummary;

public class GetStockUpOperationsSummaryHandler : IRequestHandler<GetStockUpOperationsSummaryRequest, GetStockUpOperationsSummaryResponse>
{
    private readonly IStockUpOperationRepository _repository;

    public GetStockUpOperationsSummaryHandler(IStockUpOperationRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetStockUpOperationsSummaryResponse> Handle(GetStockUpOperationsSummaryRequest request, CancellationToken cancellationToken)
    {
        var query = _repository.GetAll()
            .Where(x => x.State == StockUpOperationState.Pending
                     || x.State == StockUpOperationState.Submitted
                     || x.State == StockUpOperationState.Failed);

        // Apply optional SourceType filter
        if (request.SourceType.HasValue)
        {
            query = query.Where(x => x.SourceType == request.SourceType.Value);
        }

        // Group by state and count efficiently
        var counts = await query
            .GroupBy(x => x.State)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return new GetStockUpOperationsSummaryResponse
        {
            PendingCount = counts.FirstOrDefault(x => x.State == StockUpOperationState.Pending)?.Count ?? 0,
            SubmittedCount = counts.FirstOrDefault(x => x.State == StockUpOperationState.Submitted)?.Count ?? 0,
            FailedCount = counts.FirstOrDefault(x => x.State == StockUpOperationState.Failed)?.Count ?? 0,
            Success = true
        };
    }
}
