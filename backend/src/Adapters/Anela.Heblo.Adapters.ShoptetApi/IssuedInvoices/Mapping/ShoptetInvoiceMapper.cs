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

        var detail = new IssuedInvoiceDetail
        {
            Code = src.OrderCode ?? string.Empty,
            OrderCode = src.Code,
            VarSymbol = src.VarSymbol,
            BillingMethod = _billingMapper.Map(src.BillingMethod?.Name),
            ShippingMethod = _shippingMapper.Map(null),
            VatPayer = !string.IsNullOrEmpty(src.BillingAddress?.VatId),
            BillingAddress = billingAddress,
            DeliveryAddress = deliveryAddress,
            Customer = MapCustomer(src.BillingAddress),
            Price = MapInvoicePrice(src.Price),
        };

        var currencyCode = src.Price?.CurrencyCode ?? "CZK";
        detail.Items = src.Items
            .Select(item => MapItem(item, currencyCode))
            .ToList();

        return detail;
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
            HouseNumber = src.HouseNumber ?? string.Empty,
            City = src.City ?? string.Empty,
            District = string.Empty,
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
            Company = src.Company,
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
            ExchangeRate = src.ExchangeRate ?? 1m,
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

        return new IssuedInvoiceDetailItem
        {
            Code = src.Code ?? string.Empty,
            Name = src.Name ?? string.Empty,
            Amount = amount,
            AmountUnit = src.AmountUnit ?? string.Empty,
            ItemPrice = new InvoicePrice
            {
                WithoutVat = unitWithoutVat,
                Vat = unitVat,
                WithVat = unitWithVat,
                TotalWithoutVat = amount * unitWithoutVat,
                TotalWithVat = amount * unitWithVat,
                VatRate = src.UnitPrice?.VatRate?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                CurrencyCode = currencyCode,
            },
        };
    }
}
