using Anela.Heblo.Persistence.ShoptetOrders;
using Anela.Heblo.Persistence.ShoptetOrders.Entities;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Adapters.ShoptetApi.Analytics;

public sealed class ShoptetOrderStore : IShoptetOrderStore
{
    private readonly ShoptetOrdersDbContext _dbContext;

    public ShoptetOrderStore(ShoptetOrdersDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<int> UpsertAsync(IReadOnlyList<ShoptetOrder> orders, CancellationToken ct = default)
    {
        if (orders.Count == 0)
            return 0;

        var codes = orders.Select(o => o.Code).ToList();

        try
        {
            // Lines are replaced rather than merged: an edited order can lose, gain or reorder
            // lines, and there is no stable line identity to diff against (a product-set-item's
            // itemId is the catalogue id, which repeats across orders). Deleting first — in its own
            // SaveChanges — keeps EF from emitting the re-inserts before the deletes and tripping
            // the primary key.
            var staleLines = await _dbContext.OrderItems
                .Where(i => codes.Contains(i.OrderCode))
                .ToListAsync(ct);

            if (staleLines.Count > 0)
            {
                _dbContext.OrderItems.RemoveRange(staleLines);
                await _dbContext.SaveChangesAsync(ct);
            }

            var existing = await _dbContext.Orders
                .Where(o => codes.Contains(o.Code))
                .ToDictionaryAsync(o => o.Code, ct);

            foreach (var incoming in orders)
            {
                // The caller's object is left untouched: its Items collection is read, never
                // replaced. Attaching a header that still carries its lines on the navigation
                // would make EF insert them twice, since they go in through the DbSet with their
                // foreign key already set — so a detached copy of the header is persisted instead.
                if (existing.TryGetValue(incoming.Code, out var current))
                    CopyInto(incoming, current);
                else
                    _dbContext.Orders.Add(Detached(incoming));

                _dbContext.OrderItems.AddRange(incoming.Items);
            }

            await _dbContext.SaveChangesAsync(ct);
            return orders.Count;
        }
        finally
        {
            // In a finally, not on the success path: a failed SaveChanges leaves every order and
            // line of the batch tracked as Added, and this context is shared with the watermark
            // repository. The caller's catch then writes LastRunStatus = "FAILED" through that same
            // context, EF replays the failed inserts, and the run dies with its status stuck at
            // "RUNNING" and the real error lost. Clearing here keeps the failure reportable.
            //
            // It also bounds memory: the backfill reuses one scoped context for ~97k orders and
            // ~400k lines. Nothing outside this method holds on to the tracked instances.
            _dbContext.ChangeTracker.Clear();
        }
    }

    public async Task<int> DeleteAsync(IReadOnlyCollection<string> orderCodes, CancellationToken ct = default)
    {
        if (orderCodes.Count == 0)
            return 0;

        var codes = orderCodes.ToList();

        // Load-and-remove rather than ExecuteDelete: the EF InMemory provider used by the unit
        // tests throws on ExecuteDelete/ExecuteUpdate.
        try
        {
            var lines = await _dbContext.OrderItems.Where(i => codes.Contains(i.OrderCode)).ToListAsync(ct);
            if (lines.Count > 0)
                _dbContext.OrderItems.RemoveRange(lines);

            var headers = await _dbContext.Orders.Where(o => codes.Contains(o.Code)).ToListAsync(ct);
            if (headers.Count > 0)
                _dbContext.Orders.RemoveRange(headers);

            await _dbContext.SaveChangesAsync(ct);
            return headers.Count;
        }
        finally
        {
            // Same reasoning as UpsertAsync: a failed delete must not leave the shared context
            // poisoned, or the caller can no longer record why the run failed.
            _dbContext.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// A copy of the header with no lines on its navigation, so persisting it cannot insert the
    /// lines a second time — and the caller's own object is never modified.
    /// </summary>
    private static ShoptetOrder Detached(ShoptetOrder source)
    {
        var copy = new ShoptetOrder { Code = source.Code };
        CopyInto(source, copy);
        return copy;
    }

    private static void CopyInto(ShoptetOrder source, ShoptetOrder target)
    {
        target.Guid = source.Guid;
        target.ExternalCode = source.ExternalCode;
        target.CreationTime = source.CreationTime;
        target.ChangeTime = source.ChangeTime;
        target.OrderDate = source.OrderDate;
        target.StatusId = source.StatusId;
        target.StatusName = source.StatusName;
        target.IsPaid = source.IsPaid;
        target.CustomerGuid = source.CustomerGuid;
        target.CustomerEmail = source.CustomerEmail;
        target.BillingCompany = source.BillingCompany;
        target.BillingCompanyId = source.BillingCompanyId;
        target.BillingCity = source.BillingCity;
        target.BillingZip = source.BillingZip;
        target.BillingCountryCode = source.BillingCountryCode;
        target.CashDeskOrder = source.CashDeskOrder;
        target.SalesChannelGuid = source.SalesChannelGuid;
        target.SourceId = source.SourceId;
        target.SourceName = source.SourceName;
        target.ShippingGuid = source.ShippingGuid;
        target.ShippingName = source.ShippingName;
        target.PaymentMethodGuid = source.PaymentMethodGuid;
        target.PaymentMethodName = source.PaymentMethodName;
        target.BillingMethodId = source.BillingMethodId;
        target.BillingMethodName = source.BillingMethodName;
        target.CurrencyCode = source.CurrencyCode;
        target.ExchangeRate = source.ExchangeRate;
        target.PriceWithVat = source.PriceWithVat;
        target.PriceWithoutVat = source.PriceWithoutVat;
        target.PriceVat = source.PriceVat;
        target.PriceToPay = source.PriceToPay;
        target.ShippingPriceWithVat = source.ShippingPriceWithVat;
        target.ShippingPriceWithoutVat = source.ShippingPriceWithoutVat;
        target.BillingPriceWithVat = source.BillingPriceWithVat;
        target.BillingPriceWithoutVat = source.BillingPriceWithoutVat;
        target.DiscountWithVat = source.DiscountWithVat;
        target.DiscountWithoutVat = source.DiscountWithoutVat;
        target.ProductPriceWithVat = source.ProductPriceWithVat;
        target.ProductPriceWithoutVat = source.ProductPriceWithoutVat;
        target.ProductUnits = source.ProductUnits;
        target.VatPayer = source.VatPayer;
        target.VatMode = source.VatMode;
        target.Language = source.Language;
        target.StockId = source.StockId;
        target.Referer = source.Referer;
        target.RawPayload = source.RawPayload;
        target.SyncedAt = source.SyncedAt;
    }
}
