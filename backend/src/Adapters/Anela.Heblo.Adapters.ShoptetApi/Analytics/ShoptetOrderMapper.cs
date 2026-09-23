using Anela.Heblo.Adapters.ShoptetApi.Analytics.Model;
using Anela.Heblo.Persistence.ShoptetOrders.Entities;

namespace Anela.Heblo.Adapters.ShoptetApi.Analytics;

/// <summary>
/// Turns one GET /api/orders/{code} response into the shoptet_raw row set.
/// Pure and static so the product-set rules are testable without HTTP or a database.
/// </summary>
public static class ShoptetOrderMapper
{
    public const string ItemTypeProduct = "product";
    public const string ItemTypeProductSet = "product-set";
    public const string ItemTypeProductSetItem = "product-set-item";
    public const string ItemTypeShipping = "shipping";
    public const string ItemTypeBilling = "billing";
    public const string ItemTypeDiscountCoupon = "discount-coupon";
    public const string ItemTypeVolumeDiscount = "volume-discount";
    public const string ItemTypeService = "service";

    public const string SourceArrayItems = "items";
    public const string SourceArrayCompletion = "completion";

    public static ShoptetOrder Map(
        ShoptetOrderDetailDto dto,
        string rawJson,
        TimeZoneInfo storeTimeZone,
        DateTimeOffset syncedAt)
    {
        var creationTime = dto.CreationTime ?? syncedAt;

        // Npgsql refuses to write a DateTimeOffset whose offset is not zero into a
        // 'timestamp with time zone' column, and Shoptet stamps everything in Prague local time
        // (+01:00 / +02:00). The instant is preserved; only the representation is normalised, and
        // OrderDate is derived from the original value so the day still means the store's day.

        var order = new ShoptetOrder
        {
            Code = dto.Code,
            Guid = dto.Guid,
            ExternalCode = dto.ExternalCode,
            CreationTime = creationTime.ToUniversalTime(),
            ChangeTime = dto.ChangeTime?.ToUniversalTime(),
            OrderDate = ToStoreDate(creationTime, storeTimeZone),
            StatusId = dto.Status?.Id ?? 0,
            StatusName = dto.Status?.Name,
            IsPaid = dto.Paid ?? false,
            CustomerGuid = NullIfBlank(dto.CustomerGuid),
            CustomerEmail = NormalizeEmail(dto.Email),
            BillingCompany = NullIfBlank(dto.BillingAddress?.Company),
            BillingCompanyId = NullIfBlank(dto.BillingAddress?.CompanyId),
            BillingCity = NullIfBlank(dto.BillingAddress?.City),
            BillingZip = NullIfBlank(dto.BillingAddress?.Zip),
            BillingCountryCode = NullIfBlank(dto.BillingAddress?.CountryCode),
            CashDeskOrder = dto.CashDeskOrder ?? false,
            SalesChannelGuid = NullIfBlank(dto.SalesChannelGuid),
            SourceId = dto.Source?.Id,
            SourceName = NullIfBlank(dto.Source?.Name),
            ShippingGuid = NullIfBlank(dto.Shipping?.Guid),
            ShippingName = NullIfBlank(dto.Shipping?.Name),
            PaymentMethodGuid = NullIfBlank(dto.PaymentMethod?.Guid),
            PaymentMethodName = NullIfBlank(dto.PaymentMethod?.Name),
            BillingMethodId = dto.BillingMethod?.Id,
            BillingMethodName = NullIfBlank(dto.BillingMethod?.Name),
            CurrencyCode = NullIfBlank(dto.Price?.CurrencyCode),
            ExchangeRate = dto.Price?.ExchangeRate,
            PriceWithVat = dto.Price?.WithVat,
            PriceWithoutVat = dto.Price?.WithoutVat,
            PriceVat = dto.Price?.Vat,
            PriceToPay = dto.Price?.ToPay,
            VatPayer = dto.VatPayer ?? false,
            VatMode = NullIfBlank(dto.VatMode),
            Language = NullIfBlank(dto.Language),
            StockId = dto.StockId,
            Referer = NullIfBlank(dto.Referer),
            RawPayload = rawJson,
            SyncedAt = syncedAt,
        };

        order.Items = MapItems(dto, syncedAt);
        ApplyLineRollups(order);
        return order;
    }

    /// <summary>
    /// Persists both levels of the set contract:
    /// every entry of items[] (the set header included, priced), plus only the
    /// product-set-item entries of completion[] (the components, unpriced). The rest of
    /// completion[] duplicates items[] and is dropped. See docs/integrations/shoptet-api.md §3.3.
    /// </summary>
    public static List<ShoptetOrderItem> MapItems(ShoptetOrderDetailDto dto, DateTimeOffset syncedAt)
    {
        var lines = new List<ShoptetOrderItem>();
        var lineNo = 0;

        foreach (var item in dto.Items)
            lines.Add(MapItem(dto.Code, lineNo++, SourceArrayItems, item, syncedAt));

        foreach (var component in dto.Completion.Where(IsSetComponent))
            lines.Add(MapItem(dto.Code, lineNo++, SourceArrayCompletion, component, syncedAt));

        return lines;
    }

    private static bool IsSetComponent(ShoptetOrderItemDto item) =>
        string.Equals(item.ItemType, ItemTypeProductSetItem, StringComparison.OrdinalIgnoreCase);

    private static ShoptetOrderItem MapItem(
        string orderCode, int lineNo, string sourceArray, ShoptetOrderItemDto dto, DateTimeOffset syncedAt) =>
        new()
        {
            OrderCode = orderCode,
            LineNo = lineNo,
            SourceArray = sourceArray,
            ItemId = dto.ItemId,
            ParentItemId = dto.ParentProductSetItemId,
            ItemType = dto.ItemType ?? "",
            ProductType = NullIfBlank(dto.ProductType),
            ProductGuid = NullIfBlank(dto.ProductGuid),
            ProductCode = NullIfBlank(dto.Code),
            ProductName = NullIfBlank(dto.Name),
            VariantName = NullIfBlank(dto.VariantName),
            Brand = NullIfBlank(dto.Brand),
            Ean = NullIfBlank(dto.Ean),
            // A product-set-item amount is already the order total (component-per-set × set count).
            // It is stored verbatim and must never be multiplied by the parent's amount.
            Amount = dto.Amount,
            AmountUnit = NullIfBlank(dto.AmountUnit),
            Weight = dto.Weight,
            UnitPriceWithVat = dto.UnitPrice?.WithVat,
            UnitPriceWithoutVat = dto.UnitPrice?.WithoutVat,
            VatRate = dto.ItemPrice?.VatRate ?? dto.UnitPrice?.VatRate,
            LinePriceWithVat = dto.ItemPrice?.WithVat,
            LinePriceWithoutVat = dto.ItemPrice?.WithoutVat,
            LinePriceVat = dto.ItemPrice?.Vat,
            PurchasePriceWithoutVat = dto.PurchasePrice?.WithoutVat,
            SyncedAt = syncedAt,
        };

    /// <summary>
    /// Denormalises the line totals the month-grain views need onto the header, so Metabase never
    /// has to join 400k item rows to answer "revenue by channel".
    /// Only items[] lines are summed — the completion[] components carry no price at all, and
    /// counting their quantities as units would double-count what the set header already sold.
    /// </summary>
    private static void ApplyLineRollups(ShoptetOrder order)
    {
        foreach (var line in order.Items.Where(i => i.SourceArray == SourceArrayItems))
        {
            var withVat = line.LinePriceWithVat ?? 0m;
            var withoutVat = line.LinePriceWithoutVat ?? 0m;

            switch (line.ItemType)
            {
                case ItemTypeProduct:
                case ItemTypeProductSet:
                    order.ProductPriceWithVat += withVat;
                    order.ProductPriceWithoutVat += withoutVat;
                    order.ProductUnits += line.Amount ?? 0m;
                    break;
                // A paid add-on (gift wrapping, an insurance payout) — revenue, but not a unit of
                // merchandise, so it counts towards the money and not towards basket size.
                // Rare: 4 lines in the whole 2018-2026 history, but leaving it unbucketed made the
                // header total and the component rollups disagree on those orders.
                case ItemTypeService:
                    order.ProductPriceWithVat += withVat;
                    order.ProductPriceWithoutVat += withoutVat;
                    break;
                case ItemTypeShipping:
                    order.ShippingPriceWithVat += withVat;
                    order.ShippingPriceWithoutVat += withoutVat;
                    break;
                case ItemTypeBilling:
                    order.BillingPriceWithVat += withVat;
                    order.BillingPriceWithoutVat += withoutVat;
                    break;
                case ItemTypeDiscountCoupon:
                case ItemTypeVolumeDiscount:
                    order.DiscountWithVat += withVat;
                    order.DiscountWithoutVat += withoutVat;
                    break;
            }
        }
    }

    internal static DateOnly ToStoreDate(DateTimeOffset value, TimeZoneInfo storeTimeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(value, storeTimeZone).DateTime);

    private static string? NormalizeEmail(string? email)
    {
        var trimmed = email?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToLowerInvariant();
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
