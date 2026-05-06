using System.Diagnostics;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetStockUpOperationsSummary;

public class GetStockUpOperationsSummaryHandler : IRequestHandler<GetStockUpOperationsSummaryRequest, GetStockUpOperationsSummaryResponse>
{
    // Single source of truth for the active-state set. The PostgreSQL partial index
    // IX_StockUpOperations_State_Active uses the same integer set in its predicate.
    // Cast through (int) so silent breakage is impossible if enum values are renumbered.
    private static readonly int[] ActiveStates =
    {
        (int)StockUpOperationState.Pending,    // 0
        (int)StockUpOperationState.Submitted,  // 1
        (int)StockUpOperationState.Failed      // 3
    };

    private readonly IStockUpOperationRepository _repository;
    private readonly ILogger<GetStockUpOperationsSummaryHandler> _logger;

    public GetStockUpOperationsSummaryHandler(
        IStockUpOperationRepository repository,
        ILogger<GetStockUpOperationsSummaryHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<GetStockUpOperationsSummaryResponse> Handle(GetStockUpOperationsSummaryRequest request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            // ActiveStates.Contains((int)x.State) translates to a literal IN (0, 1, 3) in SQL,
            // which the planner can match to the partial-index predicate.
            var query = _repository.GetAll()
                .Where(x => ActiveStates.Contains((int)x.State));

            if (request.SourceType.HasValue)
            {
                query = query.Where(x => x.SourceType == request.SourceType.Value);
            }

            var counts = await query
                .GroupBy(x => x.State)
                .Select(g => new { State = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            var response = new GetStockUpOperationsSummaryResponse
            {
                PendingCount = counts.FirstOrDefault(x => x.State == StockUpOperationState.Pending)?.Count ?? 0,
                SubmittedCount = counts.FirstOrDefault(x => x.State == StockUpOperationState.Submitted)?.Count ?? 0,
                FailedCount = counts.FirstOrDefault(x => x.State == StockUpOperationState.Failed)?.Count ?? 0,
                Success = true
            };

            stopwatch.Stop();
            _logger.LogInformation(
                "GetStockUpOperationsSummary completed in {ElapsedMs}ms [SourceType={SourceType}, Pending={PendingCount}, Submitted={SubmittedCount}, Failed={FailedCount}]",
                stopwatch.ElapsedMilliseconds,
                request.SourceType?.ToString(),
                response.PendingCount,
                response.SubmittedCount,
                response.FailedCount);

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(
                ex,
                "GetStockUpOperationsSummary failed after {ElapsedMs}ms [SourceType={SourceType}]",
                stopwatch.ElapsedMilliseconds,
                request.SourceType?.ToString());

            return new GetStockUpOperationsSummaryResponse(
                ErrorCodes.InternalServerError,
                new Dictionary<string, string>
                {
                    { "error", "An unexpected error occurred." }
                }
            );
        }
    }
}
