namespace Anela.Heblo.Domain.Features.Manufacture;

public interface IManufactureOrderRepository
{
    Task<List<ManufactureOrder>> GetOrdersAsync(
        ManufactureOrderState? state = null,
        DateOnly? dateFrom = null,
        DateOnly? dateTo = null,
        string? responsiblePerson = null,
        string? orderNumber = null,
        string? productCode = null,
        CancellationToken cancellationToken = default);

    Task<ManufactureOrder?> GetOrderByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ManufactureOrder> AddOrderAsync(ManufactureOrder order, CancellationToken cancellationToken = default);
    Task<ManufactureOrder> UpdateOrderAsync(ManufactureOrder order, CancellationToken cancellationToken = default);
    Task DeleteOrderAsync(int id, CancellationToken cancellationToken = default);

    Task<string> GenerateOrderNumberAsync(CancellationToken cancellationToken = default);
}