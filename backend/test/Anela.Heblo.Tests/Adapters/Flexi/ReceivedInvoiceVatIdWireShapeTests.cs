using Anela.Heblo.Adapters.Flexi.Accounting.InvoiceClassification;
using Anela.Heblo.Application.Common;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json;
using Rem.FlexiBeeSDK.Client.Clients.ReceivedInvoices;
using Rem.FlexiBeeSDK.Model.Invoices;
using Xunit;

namespace Anela.Heblo.Tests.Adapters.Flexi;

/// <summary>
/// Pins the wire shape of a Flexi received invoice carrying a supplier DIČ (`dic`) against the
/// SDK DTO and our mapping. Verbatim-style JSON: the shape Flexi returns for
/// POST /c/{firma}/faktura-prijata.json with the ReceivedInvoiceRequest detail projection.
/// Replace the row with a captured production row (with amounts redacted) once Task 2 ran.
/// </summary>
public class ReceivedInvoiceVatIdWireShapeTests
{
    private const string MetaRow = """
        {"id":123456,"datVyst":"2026-08-03+02:00","kod":"PF2608012","nazFirmy":"Meta Platforms Ireland Limited",
         "cisDosle":"FBADS-123","varSym":"123","stredisko":[{"id":3,"kod":"MARKETING"}],"datSplat":"2026-08-17+02:00",
         "sumZklCelkemMen":0.0,"sumZklCelkem":218986.0,"mena":[{"kod":"CZK","id":1}],"sumCelkemMen":0.0,"sumCelkem":218986.0,
         "juhSum":0.0,"stavUhrK":"stavUhr.uhrazeno","juhSumMen":0.0,"storno":false,"popis":"Facebook Ads 08/2026",
         "zuctovano":true,"datUcto":"2026-08-03+02:00","typUcOp":[{"nazev":"Služby","kod":"SLUZBY","id":7}],
         "bezPolozek":true,"firma":"code:META","ic":"","dic":"IE9692928F","stitky":""}
        """;

    private static IMapper Mapper() =>
        new MapperConfiguration(cfg => cfg.AddProfile<FlexiReceivedInvoiceMappingProfile>(), NullLoggerFactory.Instance).CreateMapper();

    [Fact]
    public void Dto_BindsDicStornoBaseAndAccountingDate()
    {
        var dto = JsonConvert.DeserializeObject<ReceivedInvoiceFlexiDto>(MetaRow)!;

        dto.VatId.Should().Be("IE9692928F");
        dto.IsCancelled.Should().BeFalse();
        dto.TotalBaseAmount.Should().Be(218986.0);
        dto.AccountingDate.Should().Be(new DateTime(2026, 8, 3));
    }

    [Fact]
    public void Mapping_ExposesSupplierVatIdAndWithoutVatTotal()
    {
        var dto = JsonConvert.DeserializeObject<ReceivedInvoiceFlexiDto>(MetaRow)!;

        var mapped = Mapper().Map<ReceivedInvoice>(dto);

        mapped.SupplierVatId.Should().Be("IE9692928F");
        mapped.TotalAmountWithoutVat.Should().Be(218986m);
        mapped.AccountingDate.Should().Be(new DateTime(2026, 8, 3));
        mapped.IsCancelled.Should().BeFalse();
        mapped.InvoiceNumber.Should().Be("PF2608012");
    }

    [Fact]
    public async Task SearchByVatIdsAsync_BuildsAccountingDateAndDicFilter()
    {
        var sdk = new Mock<IReceivedInvoiceClient>();
        ReceivedInvoiceRequest? captured = null;
        sdk.Setup(c => c.SearchAsync(It.IsAny<ReceivedInvoiceRequest>(), It.IsAny<CancellationToken>()))
           .Callback<ReceivedInvoiceRequest, CancellationToken>((r, _) => captured = r)
           .ReturnsAsync(new List<ReceivedInvoiceFlexiDto> { JsonConvert.DeserializeObject<ReceivedInvoiceFlexiDto>(MetaRow)! });
        var client = new FlexiReceivedInvoicesClient(
            sdk.Object,
            Options.Create(new DataSourceOptions()),
            TimeProvider.System,
            NullLogger<FlexiReceivedInvoicesClient>.Instance,
            Mapper());

        var result = await client.SearchByVatIdsAsync(
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 31), new[] { "IE9692928F", "CZ26168685" }, CancellationToken.None);

        captured!.Filter.Should().Be(
            "((datUcto gte \"2026-08-01\" and datUcto lte \"2026-08-31\") and dic in (\"IE9692928F\",\"CZ26168685\"))");
        result.Should().ContainSingle(i => i.SupplierVatId == "IE9692928F");
    }
}
