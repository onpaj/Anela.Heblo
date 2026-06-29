using System.Diagnostics;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetStockUpOperationsSummary;

public class GetStockUpOperationsSummaryHandler : IRequestHandler<GetStockUpOperationsSummaryRequest, GetStockUpOperationsSummaryResponse>
{
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
            var (pending, submitted, failed) = await _repository.GetActiveCountsAsync(request.SourceType, cancellationToken);

            var response = new GetStockUpOperationsSummaryResponse
            {
                PendingCount = pending,
                SubmittedCount = submitted,
                FailedCount = failed,
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
