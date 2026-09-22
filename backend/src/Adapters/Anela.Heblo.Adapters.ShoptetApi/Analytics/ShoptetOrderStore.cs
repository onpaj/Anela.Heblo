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

        // Lines are replaced rather than merged: an edited order can lose, gain or reorder lines,
        // and there is no stable line identity to diff against (a product-set-item's itemId is the
        // catalogue id, which repeats across orders). Deleting first — in its own SaveChanges —
        // keeps EF from emitting the re-inserts before the deletes and tripping the primary key.
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
            var lines = incoming.Items;
            // Detach the lines from the header before persisting: they are added through the DbSet
            // with their foreign key already set, so EF must not also see them on the navigation.
            incoming.Items = new List<ShoptetOrderItem>();

            if (existing.TryGetValue(incoming.Code, out var current))
                CopyInto(incoming, current);
            else
                _dbContext.Orders.Add(incoming);

            _dbContext.OrderItems.AddRange(lines);
        }

        await _dbContext.SaveChangesAsync(ct);

        // The backfill reuses one scoped context for ~97k orders and ~400k lines. Without this the
        // change tracker would grow for the whole run, costing memory and making every subsequent
        // SaveChanges slower. Nothing outside this method holds on to the tracked instances.
        _dbContext.ChangeTracker.Clear();

        return orders.Count;
    }

    public async Task<int> DeleteAsync(IReadOnlyCollection<string> orderCodes, CancellationToken ct = default)
    {
        if (orderCodes.Count == 0)
            return 0;

        var codes = orderCodes.ToList();

        // Load-and-remove rather than ExecuteDelete: the EF InMemory provider used by the unit
        // tests throws on ExecuteDelete/ExecuteUpdate.
        var lines = await _dbContext.OrderItems.Where(i => codes.Contains(i.OrderCode)).ToListAsync(ct);
        if (lines.Count > 0)
            _dbContext.OrderItems.RemoveRange(lines);

        var headers = await _dbContext.Orders.Where(o => codes.Contains(o.Code)).ToListAsync(ct);
        if (headers.Count > 0)
            _dbContext.Orders.RemoveRange(headers);

        await _dbContext.SaveChangesAsync(ct);
        _dbContext.ChangeTracker.Clear();
        return headers.Count;
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
