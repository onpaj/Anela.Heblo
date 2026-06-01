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
        var day = orderDate.Day;
        var hour = DateTime.Now.Hour;
        var minute = DateTime.Now.Minute;

        // Format: POyyyyMMdd-HHmm
        var orderNumber = $"PO{year:D4}{month:D2}{day:D2}-{hour:D2}{minute:D2}";

        return Task.FromResult(orderNumber);
    }
}