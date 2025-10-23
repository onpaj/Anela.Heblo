using Anela.Heblo.Application.Features.Catalog.Services;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.EnqueueStockTaking;

public class EnqueueStockTakingHandler : IRequestHandler<EnqueueStockTakingRequest, EnqueueStockTakingResponse>
{
    private readonly ICatalogStockTakingService _stockTakingService;

    public EnqueueStockTakingHandler(ICatalogStockTakingService stockTakingService)
    {
        _stockTakingService = stockTakingService;
    }

    public async Task<EnqueueStockTakingResponse> Handle(EnqueueStockTakingRequest request, CancellationToken cancellationToken)
    {
        var jobId = await _stockTakingService.EnqueueStockTakingAsync(
            request.ProductCode,
            request.TargetAmount,
            request.SoftStockTaking,
            cancellationToken);

        return new EnqueueStockTakingResponse
        {
            JobId = jobId,
            Message = $"Stock taking for {request.ProductCode} with target amount {request.TargetAmount} has been queued. Job ID: {jobId}"
        };
    }
}