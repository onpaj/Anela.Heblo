using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model;
using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Mapping;

public class ShoptetInvoiceMapper
{
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
        var deliveryAddress = src.DeliveryAddress != null
            ? MapAddress(src.DeliveryAddress)
            : billingAddress;

        var currencyCode = src.Price?.CurrencyCode ?? "CZK";
        var items = src.Items.Select(item => MapItem(item, currencyCode)).ToList();

        // Mirror Playwright's IssuedInvoiceMapping price calculation:
        //   WithVat    = sum of TotalWithVat for taxable items only (VatRate != "none")
        //              — mirrors Playwright's PriceHighSum which excludes zero-VAT items (e.g. billing fees)
        //              — the REST price.withVat total includes zero-rate items; PriceHighSum does not
        //   WithoutVat = sum of per-unit prices for ALL items (ItemPrice.WithoutVat = unitWithoutVat)
        //              — Playwright sums ItemPrice.WithoutVat (per-unit) not TotalWithoutVat (line total)
        //   Vat        = WithVat - WithoutVat (derived, consistent with Playwright)
        var price = MapInvoicePrice(src.Price);
        price.WithVat = items.Where(i => i.ItemPrice.VatRate != "none").Sum(i => i.ItemPrice.TotalWithVat);
        price.WithoutVat = items.Sum(i => i.ItemPrice.WithoutVat);
        price.Vat = items.Sum(i => i.ItemPrice.Vat);

        return new IssuedInvoiceDetail
        {
            Code = src.OrderCode ?? string.Empty,
            OrderCode = src.Code,
            VarSymbol = src.VarSymbol,
            CreationTime = ParseDate(src.CreationTime),
            DueDate = ParseDate(src.DueDate),
            TaxDate = ParseDate(src.TaxDate),
            BillingMethod = _billingMapper.Map(src.BillingMethod?.Name),
            ShippingMethod = ResolveShippingFromItemNames(items.Select(i => i.Name)),
            VatPayer = !string.IsNullOrEmpty(src.BillingAddress?.VatId),
            BillingAddress = billingAddress,
            DeliveryAddress = deliveryAddress,
            Customer = MapCustomer(src.BillingAddress),
            Price = price,
            Items = items,
        };
    }

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
        unitWithoutVat *= ratio;
        unitWithVat *= ratio;
        unitVat *= ratio;

        return new IssuedInvoiceDetailItem
        {
            Code = src.Code ?? string.Empty,
            Name = src.Name ?? string.Empty,
            Amount = amount,
            AmountUnit = src.AmountUnit,
            ItemPrice = new InvoicePrice
            {
                WithoutVat = unitWithoutVat,
                Vat = unitVat,
                WithVat = unitWithVat,
                TotalWithoutVat = Math.Round(amount * unitWithoutVat, 2),
                TotalWithVat = Math.Round(amount * unitWithVat, 2),
                VatRate = MapVatRate(src.UnitPrice?.VatRate),
                CurrencyCode = currencyCode,
            },
        };
    }
}
