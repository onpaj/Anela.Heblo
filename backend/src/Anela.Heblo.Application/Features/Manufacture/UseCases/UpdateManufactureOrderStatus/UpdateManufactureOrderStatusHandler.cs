using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderStatus;

public class UpdateManufactureOrderStatusHandler : IRequestHandler<UpdateManufactureOrderStatusRequest, UpdateManufactureOrderStatusResponse>
{
    private readonly IManufactureOrderRepository _repository;
    private readonly IManufacturedProductInventoryRepository _inventoryRepository;
    private readonly IManufactureCatalogSource _catalogSource;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UpdateManufactureOrderStatusHandler> _logger;
    private readonly ICurrentUserService _currentUserService;
    private readonly IConditionsReadingProvider _conditionsProvider;

    public UpdateManufactureOrderStatusHandler(
        IManufactureOrderRepository repository,
        TimeProvider timeProvider,
        ILogger<UpdateManufactureOrderStatusHandler> logger,
        ICurrentUserService currentUserService,
        IConditionsReadingProvider conditionsProvider,
        IManufacturedProductInventoryRepository inventoryRepository,
        IManufactureCatalogSource catalogSource)
    {
        _repository = repository;
        _timeProvider = timeProvider;
        _logger = logger;
        _currentUserService = currentUserService;
        _conditionsProvider = conditionsProvider;
        _inventoryRepository = inventoryRepository;
        _catalogSource = catalogSource;
    }

    public async Task<UpdateManufactureOrderStatusResponse> Handle(UpdateManufactureOrderStatusRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var order = await _repository.GetOrderByIdAsync(request.Id, cancellationToken);

            if (order == null)
            {
                return new UpdateManufactureOrderStatusResponse(Application.Shared.ErrorCodes.ResourceNotFound,
                    new Dictionary<string, string> { { "id", request.Id.ToString() } });
            }

            var oldState = order.State;

            // Validate state transition (basic validation - can be extended)
            if (!order.CanTransitionTo(request.NewState))
            {
                return new UpdateManufactureOrderStatusResponse(Application.Shared.ErrorCodes.InvalidOperation,
                    new Dictionary<string, string>
                    {
                        { "oldState", oldState.ToString() },
                        { "newState", request.NewState.ToString() }
                    });
            }

            var currentUserName = _currentUserService.GetCurrentUser().GetDisplayName();

            // Update state
            order.ChangeState(request.NewState, _timeProvider.GetUtcNow().DateTime, currentUserName);

            if (request.ManualActionRequired.HasValue)
                order.ManualActionRequired = request.ManualActionRequired.Value;
            if (request.SemiProductOrderCode != null)
            {
                order.ErpOrderNumberSemiproduct = request.SemiProductOrderCode;
                order.ErpOrderNumberSemiproductDate = _timeProvider.GetUtcNow().DateTime;
            }

            if (request.ProductOrderCode != null)
            {
                order.ErpOrderNumberProduct = request.ProductOrderCode;
                order.ErpOrderNumberProductDate = _timeProvider.GetUtcNow().DateTime;
            }

            if (request.WeightWithinTolerance.HasValue)
                order.WeightWithinTolerance = request.WeightWithinTolerance.Value;

            if (request.WeightDifference.HasValue)
                order.WeightDifference = request.WeightDifference.Value;

            if (request.FlexiDocMaterialIssueForSemiProduct != null)
            {
                order.DocMaterialIssueForSemiProduct = request.FlexiDocMaterialIssueForSemiProduct;
                order.DocMaterialIssueForSemiProductDate = _timeProvider.GetUtcNow().DateTime;
            }

            if (request.FlexiDocSemiProductReceipt != null)
            {
                order.DocSemiProductReceipt = request.FlexiDocSemiProductReceipt;
                order.DocSemiProductReceiptDate = _timeProvider.GetUtcNow().DateTime;
            }

            if (request.FlexiDocSemiProductIssueForProduct != null)
            {
                order.DocSemiProductIssueForProduct = request.FlexiDocSemiProductIssueForProduct;
                order.DocSemiProductIssueForProductDate = _timeProvider.GetUtcNow().DateTime;
            }

            if (request.FlexiDocMaterialIssueForProduct != null)
            {
                order.DocMaterialIssueForProduct = request.FlexiDocMaterialIssueForProduct;
                order.DocMaterialIssueForProductDate = _timeProvider.GetUtcNow().DateTime;
            }

            if (request.FlexiDocProductReceipt != null)
            {
                order.DocProductReceipt = request.FlexiDocProductReceipt;
                order.DocProductReceiptDate = _timeProvider.GetUtcNow().DateTime;
            }

            if (request.Note != null)
            {
                order.Notes.Add(new ManufactureOrderNote()
                {
                    Text = request.Note,
                    CreatedAt = order.StateChangedAt,
                    CreatedByUser = order.StateChangedByUser
                });
            }

            if (request.NewState is ManufactureOrderState.SemiProductManufactured or ManufactureOrderState.Completed
                && order.ConditionsReadings.All(r => r.Stage != request.NewState))
            {
                var reading = await CaptureConditionsReadingAsync(order, request.NewState, cancellationToken);
                order.ConditionsReadings.Add(reading);
            }

            if (request.NewState == ManufactureOrderState.Completed)
            {
                await WriteDownInventoryAsync(order, currentUserName, cancellationToken);
            }

            await _repository.UpdateOrderAsync(order, cancellationToken);

            return new UpdateManufactureOrderStatusResponse
            {
                OldState = oldState.ToString(),
                NewState = request.NewState.ToString(),
                StateChangedAt = order.StateChangedAt,
                StateChangedByUser = order.StateChangedByUser
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating manufacture order status for order {OrderId}", request.Id);
            return new UpdateManufactureOrderStatusResponse(ErrorCodes.InternalServerError);
        }
    }

    private async Task WriteDownInventoryAsync(ManufactureOrder order, string changedByUser, CancellationToken cancellationToken)
    {
        var timestamp = _timeProvider.GetUtcNow().DateTime;

        var productsWithQuantity = order.Products
            .Where(p => p.ActualQuantity is > 0)
            .ToList();

        if (productsWithQuantity.Count == 0)
            return;

        var productCodes = productsWithQuantity.Select(p => p.ProductCode).ToList();
        var catalogEntries = await _catalogSource.GetByIdsAsync(productCodes, cancellationToken);

        var items = productsWithQuantity
            .Where(p => !catalogEntries.TryGetValue(p.ProductCode, out var entry) || entry.Type != ProductType.SemiProduct)
            .Select(p => new ManufacturedProductInventoryItem(
                productCode: p.ProductCode,
                productName: p.ProductName,
                amount: p.ActualQuantity!.Value,
                createdBy: changedByUser,
                createdAt: timestamp,
                lotNumber: p.LotNumber,
                expirationDate: p.ExpirationDate,
                manufactureOrderId: order.Id))
            .ToList();

        if (items.Count > 0)
            await _inventoryRepository.AddRangeAsync(items, cancellationToken);
    }

    private async Task<ManufactureOrderConditionsReading> CaptureConditionsReadingAsync(
        ManufactureOrder order,
        ManufactureOrderState stage,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _conditionsProvider.GetCurrentSnapshotAsync(cancellationToken);
            return new ManufactureOrderConditionsReading
            {
                ManufactureOrderId = order.Id,
                Stage = stage,
                InnerTemperature = snapshot.InnerTemperature,
                InnerHumidity = snapshot.InnerHumidity,
                OuterTemperature = snapshot.OuterTemperature,
                OuterHumidity = snapshot.OuterHumidity,
                RecordedAt = snapshot.RecordedAt,
                Source = snapshot.Source,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to capture conditions reading for order {OrderId}, stage {Stage}", order.Id, stage);
            return new ManufactureOrderConditionsReading
            {
                ManufactureOrderId = order.Id,
                Stage = stage,
                RecordedAt = _timeProvider.GetUtcNow().DateTime,
                Source = ConditionsReadingSource.Unavailable,
            };
        }
    }
}