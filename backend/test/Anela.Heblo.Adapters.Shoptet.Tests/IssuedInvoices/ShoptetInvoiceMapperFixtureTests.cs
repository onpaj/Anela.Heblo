using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Mapping;
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Shoptet.Tests.IssuedInvoices;

public class ShoptetInvoiceMapperFixtureTests
{
    [Fact]
    public void Invoice_126000039_TotalsMatchShoptet_AfterDiscountFold()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "IssuedInvoices", "Fixtures", "invoice-126000039.json");
        var json = File.ReadAllText(path);
        var response = JsonSerializer.Deserialize<InvoiceDetailResponse>(json)!;
        var dto = response.Data.Invoice;
        var mapper = BuildSut();

        var result = mapper.Map(dto);

        var expectedWithVat = decimal.Parse(dto.Price!.WithVat!, System.Globalization.CultureInfo.InvariantCulture);
        var expectedWithoutVat = decimal.Parse(dto.Price!.WithoutVat!, System.Globalization.CultureInfo.InvariantCulture);

        result.Price.WithVat.Should().BeApproximately(expectedWithVat, 0.02m,
            "summing post-discount line totals must match Shoptet's invoice-level price.withVat within 2 hellers");
        result.Price.WithoutVat.Should().BeApproximately(expectedWithoutVat, 0.02m,
            "summing post-discount line bases must match Shoptet's invoice-level price.withoutVat within 2 hellers");
        result.Items.Should().NotContain(i => i.Name.Contains("sleva", StringComparison.OrdinalIgnoreCase),
            "aggregate discount rows must be folded into product lines, not appear as separate items");
    }

    private static ShoptetInvoiceMapper BuildSut() =>
        new(new BillingMethodMapper(), new ShippingMethodMapper(Options.Create(new ShoptetApiSettings())));
}
