using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;

namespace Anela.Heblo.Application.Features.Manufacture.Infrastructure;

internal sealed class ManufactureCatalogSourceAdapter : ICatalogManufactureSource
{
    private readonly IManufactureOrderRepository _orderRepository;
    private readonly IManufactureHistoryClient _historyClient;
    private readonly IManufacturedProductInventoryRepository _inventoryRepository;

    public ManufactureCatalogSourceAdapter(
        IManufactureOrderRepository orderRepository,
        IManufactureHistoryClient historyClient,
        IManufacturedProductInventoryRepository inventoryRepository)
    {
        _orderRepository = orderRepository;
        _historyClient = historyClient;
        _inventoryRepository = inventoryRepository;
    }

    public Task<Dictionary<string, decimal>> GetPlannedQuantitiesAsync(CancellationToken cancellationToken) =>
        _orderRepository.GetPlannedQuantitiesAsync(cancellationToken);

    public async Task<IReadOnlyList<ManufactureHistoryRecord>> GetManufactureHistoryAsync(
        DateTime dateFrom,
        DateTime dateTo,
        CancellationToken cancellationToken) =>
        await _historyClient.GetHistoryAsync(dateFrom, dateTo, productCode: null, cancellationToken);

    public Task<Dictionary<string, decimal>> GetManufacturedInventoryAsync(CancellationToken cancellationToken) =>
        _inventoryRepository.GetTotalAmountByProductCodeAsync(cancellationToken);
}
