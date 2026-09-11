using Anela.Heblo.Adapters.Flexi.Price;
using Anela.Heblo.Domain.Features.Catalog.Price;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json.Linq;
using Rem.FlexiBeeSDK.Client;
using Rem.FlexiBeeSDK.Client.Clients.Products.BoM;
using Rem.FlexiBeeSDK.Client.Clients.ReceivedInvoices;
using Rem.FlexiBeeSDK.Client.ResultFilters;
using Xunit;

namespace Anela.Heblo.Tests.Adapters.Flexi;

/// <summary>
/// Pins the wire shape of Flexi user query 41 against <see cref="ProductPriceFlexiDto"/>.
///
/// Every other test in this folder builds the DTO in C#, which proves the mapping arithmetic
/// but says nothing about whether Flexi's JSON ever reaches those properties. That gap is
/// exactly how a live price came back 21% too high: the query returns its columns
/// LOWER-CASED (<c>typcenydphk</c>, not <c>typCenyDphK</c>), so the binding relies on
/// Newtonsoft's case-insensitive fallback. If that ever stops holding — a serializer setting,
/// a DTO rewritten onto System.Text.Json, a renamed column — the price type silently reads as
/// null, the adapter assumes excl-VAT, and every <c>s DPH</c> item is grossed up a second
/// time with nothing failing anywhere.
///
/// The JSON below is a verbatim response from
/// <c>GET /c/{firma}/uzivatelsky-dotaz/41/call.json</c> (anela_cosmetics_test, 2026-09-11).
/// It is deserialized exactly the way the SDK's ResourceClient does it, so this test fails if
/// either side of that contract moves.
/// </summary>
public class ProductPriceFlexiDtoWireShapeTests
{
    /// <summary>A real <c>s DPH</c> item: cena 285.00 IS the with-VAT price.</summary>
    private const string PriceIncludingVatRow = """
        {"winstrom":{"@version":"1.0","DotazView":[
          {"idcenik":"355","kod":"OCH005100","cena":"285.0","cenanakup":"46.600827",
           "typszbdphk":"typSzbDph.dphZakl","typzasobyk":"typZasoby.vyrobek",
           "idkusovnik":"4555","typcenydphk":"typCeny.sDph"}]}}
        """;

    /// <summary>A real <c>bez DPH</c> item: cena 0.0 excludes VAT, and idkusovnik is blank.</summary>
    private const string PriceExcludingVatRow = """
        {"winstrom":{"@version":"1.0","DotazView":[
          {"idcenik":"302","kod":"MAS001","cena":"370.0","cenanakup":"0.195",
           "typszbdphk":"typSzbDph.dphZakl","typzasobyk":"typZasoby.material",
           "idkusovnik":"","typcenydphk":"typCeny.bezDph"}]}}
        """;

    /// <summary>
    /// The adapter under test, wired with inert doubles: these cases never issue a request,
    /// they feed an already-deserialized row through the same mapping the live read uses.
    /// </summary>
    private static ProductPriceErp Map(ProductPriceFlexiDto dto) =>
        new FlexiProductPriceErpClient(
            new FlexiBeeSettings
            {
                Server = "test.flexibee.com",
                Company = "test_company",
                Login = "test_user",
                Password = "test_password",
            },
            Mock.Of<IHttpClientFactory>(),
            Mock.Of<IResultHandler>(),
            Mock.Of<IMemoryCache>(),
            Mock.Of<ILogger<ReceivedInvoiceClient>>(),
            Mock.Of<IBoMClient>(),
            Mock.Of<ILogger<FlexiProductPriceErpClient>>())
        .MapToProductPrices(new[] { dto })
        .Single();

    /// <summary>Deserializes the envelope the way Rem.FlexiBeeSDK's ResourceClient.GetAsync does.</summary>
    private static ProductPriceFlexiDto Deserialize(string json) =>
        ((JArray)JObject.Parse(json).SelectToken("winstrom.DotazView")!)
        .ToObject<List<ProductPriceFlexiDto>>()!
        .Single();

    [Fact]
    public void binds_the_lower_cased_price_type_column_query_41_actually_returns()
    {
        // Act
        var dto = Deserialize(PriceIncludingVatRow);

        // Assert
        dto.TypCenyDphK.Should().Be("typCeny.sDph");
        dto.IsPriceIncludingVat.Should().BeTrue();
    }

    [Fact]
    public void binds_the_remaining_lower_cased_columns()
    {
        // Act
        var dto = Deserialize(PriceIncludingVatRow);

        // Assert: idkusovnik is the other camel-cased JsonProperty relying on the same
        // case-insensitive fallback — a null BoM id silently disables purchase-price recalc.
        dto.ProductId.Should().Be(355);
        dto.ProductCode.Should().Be("OCH005100");
        dto.Price.Should().Be(285.0m);
        dto.VatLevel.Should().Be("typSzbDph.dphZakl");
        dto.ProductType.Should().Be("typZasoby.vyrobek");
        dto.BoMId.Should().Be(4555);
    }

    [Fact]
    public void does_not_gross_up_a_price_the_query_reports_as_including_vat()
    {
        // Arrange: the exact regression — 285.00 rendered as 344.85 on the comparison screen.
        var dto = Deserialize(PriceIncludingVatRow);

        // Act
        var price = Map(dto);

        // Assert
        price.PriceWithVat.Should().Be(285.00m);
        price.ErpPriceType.Should().Be("sDph");
    }

    [Fact]
    public void still_grosses_up_a_price_the_query_reports_as_excluding_vat()
    {
        // Arrange
        var dto = Deserialize(PriceExcludingVatRow);

        // Act
        var price = Map(dto);

        // Assert
        price.PriceWithVat.Should().Be(447.70m);
        price.ErpPriceType.Should().Be("bezDph");
    }
}
