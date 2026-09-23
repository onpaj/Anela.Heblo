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
/// SDK DTO and our mapping.
///
/// <c>MetaRow</c> is a verbatim capture (field names, casing and value types straight off the
/// wire) of the real Meta/Facebook invoice <c>PF260878</c>, one of the 87 <c>faktura-prijata</c>
/// rows returned by the live, read-only August-2026 query documented in
/// <c>.superpowers/sdd/2026-09-18-marketing-performance/task-2-report.md</c> (production, DIČ
/// filter verified 2026-09-18): <c>kod</c>, <c>datUcto</c>, <c>nazFirmy</c>, <c>ic</c>, <c>dic</c>,
/// <c>sumZklCelkem</c>, <c>sumCelkem</c> and <c>storno</c> are the exact values that report and
/// its live query captured for this invoice. The only two fields NOT part of that capture are
/// <c>stitky</c> and <c>typUcOp</c>: task 2's query did not project them, so they are included
/// here only as neutral, empty placeholders — required because
/// <c>FlexiReceivedInvoiceMappingProfile</c> calls <c>ReceivedInvoiceFlexiDto.AccountingTemplate</c>
/// (backed by <c>typUcOp</c>) and <c>.Labels.Split(...)</c> (backed by <c>stitky</c>), and the SDK's
/// own convenience getters throw <see cref="ArgumentNullException"/> if the underlying JSON array
/// is absent rather than an empty array. Neither placeholder is asserted on below.
/// </summary>
public class ReceivedInvoiceVatIdWireShapeTests
{
    private const string MetaRow = """
        {"kod":"PF260878","datUcto":"2026-08-02+02:00","nazFirmy":"Meta Platforma Ireland Limited",
         "sumZklCelkem":20000.0,"sumCelkem":20000.0,"storno":false,"ic":"","dic":"IE9692928F",
         "stitky":"","typUcOp":[]}
        """;

    /// <summary>
    /// The instant <c>datUcto</c> denotes, expressed in the host's own timezone.
    ///
    /// The wire value carries an offset — <c>"2026-08-02+02:00"</c> — and Newtonsoft binds an
    /// offset-bearing string into a <see cref="DateTime"/> by converting it to LOCAL time. On the
    /// production container (<c>ENV TZ=Europe/Prague</c> in the Dockerfile) and on a Czech
    /// developer's laptop that lands on 2026-08-02 00:00, so asserting the bare date passed. On a
    /// UTC host — every GitHub Actions runner — the same value is 2026-08-01 22:00 and the
    /// assertion failed, which is what made this test red on CI while green everywhere else.
    ///
    /// Comparing against the converted instant pins the binding (the offset IS honoured) without
    /// smuggling in an assumption about where the test happens to run.
    /// </summary>
    private static readonly DateTime ExpectedAccountingDate =
        new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.FromHours(2)).LocalDateTime;

    private static IMapper Mapper() =>
        new MapperConfiguration(cfg => cfg.AddProfile<FlexiReceivedInvoiceMappingProfile>(), NullLoggerFactory.Instance).CreateMapper();

    [Fact]
    public void Dto_BindsDicStornoBaseAndAccountingDate()
    {
        var dto = JsonConvert.DeserializeObject<ReceivedInvoiceFlexiDto>(MetaRow)!;

        dto.VatId.Should().Be("IE9692928F");
        dto.IsCancelled.Should().BeFalse();
        dto.TotalBaseAmount.Should().Be(20000.0);
        dto.AccountingDate.Should().Be(ExpectedAccountingDate);
    }

    [Fact]
    public void Mapping_ExposesSupplierVatIdAndWithoutVatTotal()
    {
        var dto = JsonConvert.DeserializeObject<ReceivedInvoiceFlexiDto>(MetaRow)!;

        var mapped = Mapper().Map<ReceivedInvoice>(dto);

        mapped.SupplierVatId.Should().Be("IE9692928F");
        mapped.TotalAmountWithoutVat.Should().Be(20000m);
        mapped.AccountingDate.Should().Be(ExpectedAccountingDate);
        mapped.IsCancelled.Should().BeFalse();
        mapped.InvoiceNumber.Should().Be("PF260878");
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
