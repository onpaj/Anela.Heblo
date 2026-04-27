using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model;
using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Mapping;

public class ShoptetInvoiceMapper
{
    private static readonly HashSet<string> AggregateDiscountTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "discount-coupon",
        "volume-discount",
        "gift",
    };

    private readonly BillingMethodMapper _billingMapper;
    private readonly ShippingMethodMapper _shippingMapper;

    public ShoptetInvoiceMapper(BillingMethodMapper billingMapper, ShippingMethodMapper shippingMapper)
    {
        _billingMapper = billingMapper;
        _shippingMapper = shippingMapper;
    }

    public IssuedInvoiceDetail Map(ShoptetInvoiceDto src)
    {
        var billingAddress = MapAddress(src.BillingAddress);
        var deliveryAddress = src.DeliveryAddress != null ? MapAddress(src.DeliveryAddress) : billingAddress;

        var currencyCode = src.Price?.CurrencyCode ?? "CZK";

        var products = src.Items
            .Where(i => !IsAggregateDiscount(i.ItemType))
            .Select(item => MapItem(item, currencyCode))
            .ToList();

        var aggregateWithoutVat = src.Items
            .Where(i => IsAggregateDiscount(i.ItemType))
            .Sum(i => ParseDecimal(i.ItemPrice?.WithoutVat ?? i.UnitPrice?.WithoutVat));
        var aggregateWithVat = src.Items
            .Where(i => IsAggregateDiscount(i.ItemType))
            .Sum(i => ParseDecimal(i.ItemPrice?.WithVat ?? i.UnitPrice?.WithVat));

        if ((aggregateWithoutVat != 0m || aggregateWithVat != 0m) && products.Count > 0)
        {
            var baseSum = products.Sum(p => p.ItemPrice.TotalWithoutVat);
            if (baseSum != 0m)
            {
                foreach (var p in products)
                {
                    var weight = p.ItemPrice.TotalWithoutVat / baseSum;
                    var newTotalWithoutVat = Math.Round(p.ItemPrice.TotalWithoutVat + weight * aggregateWithoutVat, 2);
                    var newTotalWithVat = Math.Round(p.ItemPrice.TotalWithVat + weight * aggregateWithVat, 2);

                    p.ItemPrice = new InvoicePrice
                    {
                        TotalWithoutVat = newTotalWithoutVat,
                        TotalWithVat    = newTotalWithVat,
                        Vat             = newTotalWithVat - newTotalWithoutVat,
                        WithoutVat      = p.Amount != 0m ? Math.Round(newTotalWithoutVat / p.Amount, 4) : p.ItemPrice.WithoutVat,
                        WithVat         = p.Amount != 0m ? Math.Round(newTotalWithVat    / p.Amount, 4) : p.ItemPrice.WithVat,
                        VatRate         = p.ItemPrice.VatRate,
                        CurrencyCode    = p.ItemPrice.CurrencyCode,
                    };
                }
            }
        }

        var price = MapInvoicePrice(src.Price);
        price.WithVat = products.Where(i => i.ItemPrice.VatRate != "none").Sum(i => i.ItemPrice.TotalWithVat);
        price.WithoutVat = products.Sum(i => i.ItemPrice.TotalWithoutVat);
        price.Vat = price.WithVat - price.WithoutVat;

        return new IssuedInvoiceDetail
        {
            Code = src.OrderCode ?? string.Empty,
            OrderCode = src.Code,
            VarSymbol = src.VarSymbol,
            CreationTime = ParseDate(src.CreationTime),
            DueDate = ParseDate(src.DueDate),
            TaxDate = ParseDate(src.TaxDate),
            BillingMethod = _billingMapper.Map(src.BillingMethod?.Name),
            ShippingMethod = ResolveShippingFromItemNames(products.Select(i => i.Name)),
            VatPayer = !string.IsNullOrEmpty(src.BillingAddress?.VatId),
            BillingAddress = billingAddress,
            DeliveryAddress = deliveryAddress,
            Customer = MapCustomer(src.BillingAddress),
            Price = price,
            Items = products,
        };
    }

    private static bool IsAggregateDiscount(string? itemType) =>
        itemType is not null && AggregateDiscountTypes.Contains(itemType);

    /// <summary>
    /// Converts a Shoptet REST API numeric VAT rate string (e.g. "21.00") to a Pohoda named rate
    /// (e.g. "high") — matching the format the Playwright/XML adapter produces from rateVAT.
    /// </summary>
    private static string? MapVatRate(string? vatRate)
    {
        if (string.IsNullOrEmpty(vatRate))
            return vatRate;

        if (!decimal.TryParse(vatRate, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var rate))
            return vatRate;

        return rate switch
        {
            21m => "high",
            12m => "low",
            10m => "low",
            15m => "low",
            0m => "none",
            _ => vatRate,
        };
    }

    /// <summary>
    /// Resolves shipping method by scanning item names for keywords — mirrors the Playwright adapter's
    /// ShippingMethodResolver which does the same on Pohoda XML item texts.
    /// </summary>
    private static ShippingMethod ResolveShippingFromItemNames(IEnumerable<string> itemNames)
    {
        var names = itemNames.ToList();

        if (names.Any(n => string.Equals(n, "PPL - ParcelShop", StringComparison.Ordinal)))
            return ShippingMethod.PPLParcelShop;
        if (names.Any(n => n.Contains("PPL", StringComparison.InvariantCultureIgnoreCase)))
            return ShippingMethod.PPL;
        if (names.Any(n => n.Contains("Osobn", StringComparison.InvariantCultureIgnoreCase)))
            return ShippingMethod.PickUp;
        if (names.Any(n => n.Contains("GLS", StringComparison.InvariantCultureIgnoreCase)))
            return ShippingMethod.GLS;
        if (names.Any(n => n.Contains("zásilk", StringComparison.InvariantCultureIgnoreCase)))
            return ShippingMethod.Zasilkovna;

        return ShippingMethod.PickUp;
    }

    private static DateTime ParseDate(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return default;

        return DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var dt)
            ? dt
            : default;
    }

    private static InvoiceAddress MapAddress(ShoptetInvoiceAddressDto? src)
    {
        if (src == null)
        {
            return new InvoiceAddress();
        }

        return new InvoiceAddress
        {
            Company = src.Company ?? string.Empty,
            FullName = src.FullName ?? string.Empty,
            Street = src.Street ?? string.Empty,
            HouseNumber = src.HouseNumber,
            City = src.City ?? string.Empty,
            District = null,
            Zip = src.Zip ?? string.Empty,
            CountryCode = src.CountryCode ?? string.Empty,
        };
    }

    private static InvoiceCustomer MapCustomer(ShoptetInvoiceAddressDto? src)
    {
        if (src == null)
        {
            return new InvoiceCustomer();
        }

        return new InvoiceCustomer
        {
            Company = src.Company ?? string.Empty,
            Name = src.FullName ?? string.Empty,
            VatId = src.VatId ?? string.Empty,
            CompanyId = src.CompanyId ?? string.Empty,
        };
    }

    private static InvoicePrice MapInvoicePrice(ShoptetInvoicePriceDto? src)
    {
        if (src == null)
        {
            return new InvoicePrice();
        }

        _ = decimal.TryParse(src.WithVat, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var withVat);
        _ = decimal.TryParse(src.WithoutVat, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var withoutVat);

        return new InvoicePrice
        {
            WithVat = withVat,
            WithoutVat = withoutVat,
            Vat = withVat - withoutVat,
            CurrencyCode = src.CurrencyCode ?? "CZK",
            ExchangeRate = decimal.TryParse(src.ExchangeRate, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var exchangeRate) ? exchangeRate : 1m,
        };
    }

    private static decimal ParseDecimal(string? value)
    {
        _ = decimal.TryParse(value, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var d);
        return d;
    }

    private static IssuedInvoiceDetailItem MapItem(ShoptetInvoiceItemDto src, string currencyCode)
    {
        _ = decimal.TryParse(src.Amount, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var amount);

        _ = decimal.TryParse(src.UnitPrice?.WithoutVat, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var unitWithoutVat);
        _ = decimal.TryParse(src.UnitPrice?.WithVat, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var unitWithVat);
        _ = decimal.TryParse(src.UnitPrice?.Vat, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var unitVat);

        // Fold per-line discount: priceRatio is the fraction of unitPrice the customer actually paid.
        // priceRatio=0.78 → 22% discount; priceRatio=0.0 → 100% free; priceRatio=1.0 (or null) → no discount.
        // Use >= 0 to include the priceRatio=0.0 free-item case (real-world: invoice 126000039).
        var ratio = src.PriceRatio is { } r && r >= 0m && r < 1m ? r : 1m;
        var discountedWithoutVat = unitWithoutVat * ratio;
        var discountedWithVat = unitWithVat * ratio;
        var discountedVat = unitVat * ratio;

        return new IssuedInvoiceDetailItem
        {
            Code = src.Code ?? string.Empty,
            Name = src.Name ?? string.Empty,
            Amount = amount,
            AmountUnit = src.AmountUnit,
            ItemPrice = new InvoicePrice
            {
                WithoutVat = discountedWithoutVat,
                Vat = discountedVat,
                WithVat = discountedWithVat,
                TotalWithoutVat = Math.Round(amount * discountedWithoutVat, 2),
                TotalWithVat = Math.Round(amount * discountedWithVat, 2),
                VatRate = MapVatRate(src.UnitPrice?.VatRate),
                CurrencyCode = currencyCode,
            },
        };
    }
}
