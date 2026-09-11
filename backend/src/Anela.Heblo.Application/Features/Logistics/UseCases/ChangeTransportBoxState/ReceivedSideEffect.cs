using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;

public class ReceivedSideEffect : ITransportBoxTransitionSideEffect
{
    private readonly ILogisticsStockOperationService _stockOperationService;
    private readonly ILogger<ReceivedSideEffect> _logger;

    public ReceivedSideEffect(ILogisticsStockOperationService stockOperationService, ILogger<ReceivedSideEffect> logger)
    {
        _stockOperationService = stockOperationService;
        _logger = logger;
    }

    public bool Supports(TransportBoxState from, TransportBoxState to) =>
        to == TransportBoxState.Received &&
        (from == TransportBoxState.InTransit || from == TransportBoxState.Reserve || from == TransportBoxState.Quarantine);

    public async Task<ChangeTransportBoxStateResponse?> ExecuteAsync(
        TransportBox box, ChangeTransportBoxStateRequest request, CancellationToken cancellationToken)
    {
        var aggregated = box.Items
            .GroupBy(i => i.ProductCode)
            .Select(g => new
            {
                ProductCode = g.Key,
                Amount = (int)Math.Round(g.Sum(i => i.Amount), MidpointRounding.AwayFromZero),
                LineCount = g.Count()
            })
            .ToList();

        foreach (var group in aggregated)
        {
            var documentNumber = $"BOX-{box.Id:000000}-{group.ProductCode}";

            await _stockOperationService.StageOperationAsync(
                documentNumber,
                group.ProductCode,
                group.Amount,
                LogisticsStockOperationSource.TransportBox,
                box.Id,
                cancellationToken);

            _logger.LogDebug("Staged StockUpOperation {DocumentNumber} for product {ProductCode}, amount {Amount} (aggregated from {LineCount} item line(s))",
                documentNumber, group.ProductCode, group.Amount, group.LineCount);
        }

        _logger.LogInformation("Staged {OperationCount} StockUpOperation(s) from {ItemCount} item line(s) for box {BoxId} ({BoxCode})",
            aggregated.Count, box.Items.Count, box.Id, box.Code);

        return null;
    }
}
