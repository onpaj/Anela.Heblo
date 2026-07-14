using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ShoptetOrders.Infrastructure.Jobs;

public sealed class CompleteDeliveredOrdersJob : IRecurringJob
{
    private const string CompletionNote = "Automaticky vyřízeno – zásilka doručena";

    private readonly IEshopOrderClient _orderClient;
    private readonly IShipmentClient _shipmentClient;
    private readonly ShoptetOrdersSettings _settings;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<CompleteDeliveredOrdersJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "complete-delivered-orders",
        DisplayName = "Complete Delivered Orders",
        Description = "Moves Shoptet orders in the 'handed to carrier' states to 'vyřízena' once any of their shipments reports delivered.",
        CronExpression = "0 * * * *",
        DefaultIsEnabled = true,
    };

    public CompleteDeliveredOrdersJob(
        IEshopOrderClient orderClient,
        IShipmentClient shipmentClient,
        IOptions<ShoptetOrdersSettings> settings,
        IRecurringJobStatusChecker statusChecker,
        ILogger<CompleteDeliveredOrdersJob> logger)
    {
        _orderClient = orderClient;
        _shipmentClient = shipmentClient;
        _settings = settings.Value;
        _statusChecker = statusChecker;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }

        var targetState = _settings.CompletedStatusId;
        var scanned = 0;
        var completed = 0;

        foreach (var stateId in _settings.DeliveredCompletionSourceStateIds)
        {
            List<EshopOrderSummary> orders;
            try
            {
                orders = await _orderClient.ListOrdersByStatusAsync(stateId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "CompleteDeliveredOrders: failed to list orders in state {StateId}. Skipping this state.",
                    stateId);
                continue;
            }

            foreach (var order in orders)
            {
                scanned++;
                try
                {
                    if (!await _shipmentClient.HasDeliveredShipmentAsync(order.Code, cancellationToken))
                        continue;

                    await _orderClient.UpdateStatusAsync(order.Code, targetState, cancellationToken);
                    await AppendCompletionNoteAsync(order.Code, cancellationToken);
                    completed++;

                    _logger.LogInformation(
                        "CompleteDeliveredOrders: order {OrderCode} moved from state {StateId} to {TargetState} (delivered).",
                        order.Code, stateId, targetState);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "CompleteDeliveredOrders: failed to complete order {OrderCode}. Will retry next run.",
                        order.Code);
                }
            }
        }

        _logger.LogInformation(
            "CompleteDeliveredOrders: scanned {Scanned} order(s), completed {Completed}.",
            scanned, completed);
    }

    private async Task AppendCompletionNoteAsync(string orderCode, CancellationToken cancellationToken)
    {
        try
        {
            var currentRemark = await _orderClient.GetEshopRemarkAsync(orderCode, cancellationToken);
            var updatedRemark = string.IsNullOrEmpty(currentRemark)
                ? CompletionNote
                : $"{currentRemark}\n{CompletionNote}";
            await _orderClient.UpdateEshopRemarkAsync(orderCode, updatedRemark, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "CompleteDeliveredOrders: order {OrderCode} was completed but the note could not be appended.",
                orderCode);
        }
    }
}
