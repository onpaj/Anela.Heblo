using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Manufacture.Inventory;

public interface IManufacturedProductInventoryRepository : IRepository<ManufacturedProductInventoryItem, int>
{
    Task<ManufacturedProductInventoryItem?> GetByIdWithLogsAsync(int id, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<ManufacturedProductInventoryItem> Items, int TotalCount)> GetPagedListAsync(
        ManufacturedInventoryFilter filter, CancellationToken cancellationToken = default);
}

public class ManufacturedInventoryFilter
{
    public string? Search { get; set; }
    public bool OnlyWithStock { get; set; } = false;
    public int? ManufactureOrderId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
