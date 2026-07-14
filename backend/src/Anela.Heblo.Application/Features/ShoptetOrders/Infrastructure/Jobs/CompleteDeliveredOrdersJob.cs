using Anela.Heblo.Application.Features.FeatureFlags;
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
    private readonly IFeatureFlagChecker _featureFlags;
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
        IFeatureFlagChecker featureFlags,
        ILogger<CompleteDeliveredOrdersJob> logger)
    {
        _orderClient = orderClient;
        _shipmentClient = shipmentClient;
        _settings = settings.Value;
        _statusChecker = statusChecker;
        _featureFlags = featureFlags;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }

        var applyChanges = await _featureFlags.IsEnabledAsync(
            FeatureFlagKeys.DeliveredOrderCompletion, cancellationToken);
        if (!applyChanges)
        {
            _logger.LogInformation(
                "CompleteDeliveredOrders: running in DRY RUN mode (feature flag '{Flag}' disabled) — delivered orders will be logged but not changed.",
                FeatureFlagKeys.DeliveredOrderCompletion);
        }

        var useTestSource = await _featureFlags.IsEnabledAsync(
            FeatureFlagKeys.DeliveredOrderCompletionTestSource, cancellationToken);
        var sourceStateIds = useTestSource
            ? _settings.DeliveredCompletionTestSourceStateIds
            : _settings.DeliveredCompletionSourceStateIds;
        if (useTestSource)
        {
            _logger.LogInformation(
                "CompleteDeliveredOrders: using TEST source states [{TestStates}] (feature flag '{Flag}' enabled) instead of the production states.",
                string.Join(", ", sourceStateIds), FeatureFlagKeys.DeliveredOrderCompletionTestSource);
        }

        var targetState = _settings.CompletedStatusId;
        var scanned = 0;
        var delivered = 0;
        var completed = 0;

        foreach (var stateId in sourceStateIds)
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

                    delivered++;

                    if (!applyChanges)
                    {
                        _logger.LogInformation(
                            "CompleteDeliveredOrders [DRY RUN]: order {OrderCode} in state {StateId} is delivered — would move to {TargetState} and add remark. No changes made.",
                            order.Code, stateId, targetState);
                        continue;
                    }

                    await _orderClient.UpdateStatusAsync(order.Code, targetState, cancellationToken);
                    await AppendCompletionNoteAsync(order.Code, cancellationToken);
                    completed++;

                    _logger.LogInformation(
                        "CompleteDeliveredOrders: order {OrderCode} moved from state {StateId} to {TargetState} (delivered).",
                        order.Code, stateId, targetState);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "CompleteDeliveredOrders: failed to complete order {OrderCode}. Will retry next run.",
                        order.Code);
                }
            }
        }

        _logger.LogInformation(
            "CompleteDeliveredOrders: scanned {Scanned} order(s), {Delivered} delivered, {Completed} completed{Mode}.",
            scanned, delivered, completed, applyChanges ? string.Empty : " [DRY RUN — no changes applied]");
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
