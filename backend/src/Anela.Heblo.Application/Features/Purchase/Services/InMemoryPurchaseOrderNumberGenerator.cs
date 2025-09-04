using Anela.Heblo.Domain.Features.Purchase;

namespace Anela.Heblo.Application.Features.Purchase.Services;

public class InMemoryPurchaseOrderNumberGenerator : IPurchaseOrderNumberGenerator
{
    private static long _counter = 0;

    public async Task<string> GenerateOrderNumberAsync(DateTime orderDate, CancellationToken cancellationToken = default)
    {
        var year = orderDate.Year;
        var month = orderDate.Month;
        var day = orderDate.Day;
        var hour = DateTime.Now.Hour;
        var minute = DateTime.Now.Minute;

        // Format: POyyyyMMdd-HHmm (same as PurchaseOrderNumberGenerator)
        var orderNumber = $"PO{year:D4}{month:D2}{day:D2}-{hour:D2}{minute:D2}";

        return await Task.FromResult(orderNumber);
    }
}