namespace Anela.Heblo.Domain.Features.Purchase;

public interface IPurchaseOrderNumberGenerator
{
    Task<string> GenerateOrderNumberAsync(DateTime orderDate, CancellationToken cancellationToken = default);
}

public class PurchaseOrderNumberGenerator : IPurchaseOrderNumberGenerator
{
    public Task<string> GenerateOrderNumberAsync(DateTime orderDate, CancellationToken cancellationToken = default)
    {
        var year = orderDate.Year;
        var month = orderDate.Month;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var orderNumber = $"PO{year:D4}{month:D2}-{timestamp}";

        return Task.FromResult(orderNumber);
    }
}