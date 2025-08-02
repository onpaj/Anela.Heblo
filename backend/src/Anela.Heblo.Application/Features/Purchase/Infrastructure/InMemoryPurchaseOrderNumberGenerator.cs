using Anela.Heblo.Domain.Features.Purchase;

namespace Anela.Heblo.Application.Features.Purchase.Infrastructure;

public class InMemoryPurchaseOrderNumberGenerator : IPurchaseOrderNumberGenerator
{
    private static long _counter = 0;

    public async Task<string> GenerateOrderNumberAsync(DateTime orderDate, CancellationToken cancellationToken = default)
    {
        var increment = Interlocked.Increment(ref _counter);
        var orderNumber = $"PO-{orderDate:yyyyMMdd}-{increment:D4}";
        return await Task.FromResult(orderNumber);
    }
}