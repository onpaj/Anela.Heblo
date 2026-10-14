using System.Net;
using System.Text;
using Anela.Heblo.Application.Features.Packaging.Contracts;
using Anela.Heblo.Application.Features.Packaging.UseCases.GetPackageLabelPdf;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Features.Packaging;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Anela.Heblo.Tests.Application.Packaging;

public class GetPackageLabelPdfHandlerTests
{
    private const string OrderCode = "0001234";
    private const string PackageName = "PKG-1";
    private const int PackageNumber = 1;
    private const string LabelUrl = "https://cdn.carrier.test/labels/PKG-1.pdf";
    private const string ShippingGuid = "shipping-guid-1";

    private readonly Mock<IShipmentClient> _shipmentClient = new();
    private readonly Mock<HttpMessageHandler> _httpMessageHandler = new(MockBehavior.Strict);
    private readonly Mock<IHttpClientFactory> _httpClientFactory = new();
    private readonly Mock<ILogger<GetPackageLabelPdfHandler>> _logger = new();
    private readonly Mock<ICarrierLabelOffsetRepository> _labelOffsets = new();
    private readonly Mock<IPackingOrderShippingSource> _orderShippingSource = new();
    private readonly Mock<IShippingMethodCatalog> _shippingCatalog = new();

    public GetPackageLabelPdfHandlerTests()
    {
        SetupCarrier(Carriers.Zasilkovna);
        SetupOffsets();
        _httpClientFactory
            .Setup(f => f.CreateClient(GetPackageLabelPdfHandler.HttpClientName))
            .Returns(() => new HttpClient(_httpMessageHandler.Object));
    }

    private GetPackageLabelPdfHandler CreateHandler() =>
        new(
            _shipmentClient.Object,
            _httpClientFactory.Object,
            _orderShippingSource.Object,
            _shippingCatalog.Object,
            _labelOffsets.Object,
            _logger.Object);

    private void SetupCarrier(Carriers? carrier)
    {
        _orderShippingSource
            .Setup(s => s.GetShippingMethodGuidAsync(OrderCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ShippingGuid);
        _shippingCatalog
            .Setup(c => c.ResolveCarrierByShippingGuid(ShippingGuid))
            .Returns(carrier);
    }

    private static GetPackageLabelPdfRequest Request() => new()
    {
        OrderCode = OrderCode,
        PackageNumber = PackageNumber,
    };

    private void VerifyLogged(LogLevel level, Times times) =>
        _logger.Verify(
            l => l.Log(
                level,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()),
            times);

    private void SetupShipmentLabels(params ShipmentLabel[] labels)
    {
        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync(OrderCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(labels);
    }

    private void SetupHttpResponse(HttpResponseMessage message)
    {
        _httpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(message);
    }

    [Fact]
    public async Task Handle_NoLabelsForOrder_ReturnsPackageLabelNotFound()
    {
        SetupShipmentLabels();

        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.PackageLabelNotFound);
        response.Content.Should().BeNull();
    }

    [Fact]
    public async Task Handle_PackageNumberOutOfRange_ReturnsPackageLabelNotFound()
    {
        // Only one label exists, but package number 2 (index 1) is requested.
        SetupShipmentLabels(new ShipmentLabel
        {
            ShipmentGuid = Guid.NewGuid(),
            OrderCode = OrderCode,
            PackageName = PackageName,
            LabelUrl = LabelUrl,
        });

        var response = await CreateHandler().Handle(
            new GetPackageLabelPdfRequest { OrderCode = OrderCode, PackageNumber = 2 },
            CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.PackageLabelNotFound);
    }

    [Fact]
    public async Task Handle_LabelUrlIsNull_ReturnsPackageLabelNotFound()
    {
        SetupShipmentLabels(new ShipmentLabel
        {
            ShipmentGuid = Guid.NewGuid(),
            OrderCode = OrderCode,
            PackageName = PackageName,
            LabelUrl = null,
        });

        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.PackageLabelNotFound);
    }

    [Fact]
    public async Task Handle_NotReady_WhenPoll_LogsAtDebugNotWarning()
    {
        // Automated readiness polls must not flood the logs: expected 404s log at Debug.
        SetupShipmentLabels();

        await CreateHandler().Handle(
            new GetPackageLabelPdfRequest { OrderCode = OrderCode, PackageNumber = PackageNumber, IsPoll = true },
            CancellationToken.None);

        VerifyLogged(LogLevel.Warning, Times.Never());
        VerifyLogged(LogLevel.Debug, Times.Once());
    }

    [Fact]
    public async Task Handle_NotReady_WhenNotPoll_LogsAtWarning()
    {
        // The final (post-timeout) confirmation request is not a poll: its 404 is a real warning.
        SetupShipmentLabels();

        await CreateHandler().Handle(
            new GetPackageLabelPdfRequest { OrderCode = OrderCode, PackageNumber = PackageNumber, IsPoll = false },
            CancellationToken.None);

        VerifyLogged(LogLevel.Warning, Times.Once());
    }

    [Fact]
    public async Task Handle_CarrierReturnsError_ReturnsPackageLabelDownloadFailed()
    {
        SetupShipmentLabels(new ShipmentLabel
        {
            ShipmentGuid = Guid.NewGuid(),
            OrderCode = OrderCode,
            PackageName = PackageName,
            LabelUrl = LabelUrl,
        });
        SetupHttpResponse(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.PackageLabelDownloadFailed);
    }

    [Fact]
    public async Task Handle_CarrierThrows_ReturnsPackageLabelDownloadFailed()
    {
        SetupShipmentLabels(new ShipmentLabel
        {
            ShipmentGuid = Guid.NewGuid(),
            OrderCode = OrderCode,
            PackageName = PackageName,
            LabelUrl = LabelUrl,
        });
        _httpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("network down"));

        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.PackageLabelDownloadFailed);
    }

    [Fact]
    public async Task Handle_Success_ReturnsPdfStreamAndFileName()
    {
        SetupShipmentLabels(new ShipmentLabel
        {
            ShipmentGuid = Guid.NewGuid(),
            OrderCode = OrderCode,
            PackageName = PackageName,
            LabelUrl = LabelUrl,
        });
        var pdfBytes = Encoding.UTF8.GetBytes("%PDF-1.4 fake");
        var pdfResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(pdfBytes),
        };
        pdfResponse.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        SetupHttpResponse(pdfResponse);

        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        response.Success.Should().BeTrue();
        response.ErrorCode.Should().BeNull();
        response.ContentType.Should().Be("application/pdf");
        response.FileName.Should().Be($"{OrderCode}-{PackageNumber}.pdf");
        response.Content.Should().NotBeNull();

        using var ms = new MemoryStream();
        await response.Content!.CopyToAsync(ms);
        ms.ToArray().Should().BeEquivalentTo(pdfBytes);
    }

    private void SetupOffsets(params CarrierLabelOffset[] offsets) =>
        _labelOffsets
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(offsets);

    private static CarrierLabelOffset Offset(Carriers carrier, int xMm, int yMm) => new(carrier, xMm, yMm, "user-1");

    private void SetupLabelPdf(byte[] pdfBytes)
    {
        SetupShipmentLabels(new ShipmentLabel
        {
            ShipmentGuid = Guid.NewGuid(),
            OrderCode = OrderCode,
            PackageName = PackageName,
            LabelUrl = LabelUrl,
        });
        var pdfResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(pdfBytes),
        };
        pdfResponse.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        SetupHttpResponse(pdfResponse);
    }

    private static byte[] CreatePdf(int pageCount)
    {
        using var document = new PdfDocument();
        for (var i = 0; i < pageCount; i++)
            document.AddPage();

        using var ms = new MemoryStream();
        document.Save(ms);
        return ms.ToArray();
    }

    private static async Task<byte[]> ReadContentAsync(GetPackageLabelPdfResponse response)
    {
        using var ms = new MemoryStream();
        await response.Content!.CopyToAsync(ms);
        return ms.ToArray();
    }

    private static double Points(int mm) => Math.Round(mm * 72 / 25.4, 3);

    private static IReadOnlyList<(double X, double Y)> MediaBoxOrigins(byte[] pdfBytes)
    {
        using var document = PdfReader.Open(new MemoryStream(pdfBytes), PdfDocumentOpenMode.Import);
        return document.Pages.Cast<PdfPage>()
            .Select(p => p.Elements.GetRectangle("/MediaBox"))
            .Select(box => (Math.Round(box.X1, 3), Math.Round(box.Y1, 3)))
            .ToList();
    }

    [Fact]
    public async Task Handle_NoOffsetsSaved_ReturnsPdfUnchanged()
    {
        var pdfBytes = CreatePdf(pageCount: 1);
        SetupLabelPdf(pdfBytes);

        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        (await ReadContentAsync(response)).Should().BeEquivalentTo(pdfBytes);
    }

    [Theory]
    [InlineData(5, 0)]
    [InlineData(-5, 0)]
    [InlineData(0, 7)]
    [InlineData(0, -3)]
    [InlineData(4, -6)]
    public async Task Handle_CarrierHasOffset_ShiftsEveryPageByThatManyMm(int xMm, int yMm)
    {
        SetupLabelPdf(CreatePdf(pageCount: 2));
        SetupOffsets(Offset(Carriers.Zasilkovna, xMm, yMm));

        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        response.Success.Should().BeTrue();
        response.ContentType.Should().Be("application/pdf");
        // Content moves by sliding the MediaBox the opposite way.
        var expected = (-Points(xMm), -Points(yMm));
        MediaBoxOrigins(await ReadContentAsync(response)).Should().Equal(expected, expected);
    }

    [Theory]
    [InlineData(Carriers.Zasilkovna, 2, -5)]
    [InlineData(Carriers.PPL, -4, 3)]
    [InlineData(Carriers.GLS, 6, 1)]
    public async Task Handle_SeveralCarriersConfigured_UsesTheOrdersCarrierOffset(Carriers carrier, int xMm, int yMm)
    {
        SetupCarrier(carrier);
        SetupLabelPdf(CreatePdf(pageCount: 1));
        SetupOffsets(Offset(Carriers.Zasilkovna, 2, -5), Offset(Carriers.PPL, -4, 3), Offset(Carriers.GLS, 6, 1));

        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        MediaBoxOrigins(await ReadContentAsync(response)).Should().Equal((-Points(xMm), -Points(yMm)));
    }

    [Fact]
    public async Task Handle_OtherCarrierHasOffset_OrdersCarrierHasNone_ReturnsPdfUnchanged()
    {
        var pdfBytes = CreatePdf(pageCount: 1);
        SetupCarrier(Carriers.PPL);
        SetupLabelPdf(pdfBytes);
        SetupOffsets(Offset(Carriers.Zasilkovna, 0, -5));

        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        (await ReadContentAsync(response)).Should().BeEquivalentTo(pdfBytes);
    }

    [Fact]
    public async Task Handle_OrdersCarrierOffsetIsZero_ReturnsPdfUnchanged()
    {
        var pdfBytes = CreatePdf(pageCount: 1);
        SetupCarrier(Carriers.PPL);
        SetupLabelPdf(pdfBytes);
        SetupOffsets(Offset(Carriers.Zasilkovna, 0, -5), Offset(Carriers.PPL, 0, 0));

        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        (await ReadContentAsync(response)).Should().BeEquivalentTo(pdfBytes);
    }

    [Theory]
    [InlineData(Carriers.Osobak)]
    [InlineData(null)]
    public async Task Handle_CarrierWithoutOffsetOrUnresolvable_ReturnsPdfUnchanged(Carriers? carrier)
    {
        var pdfBytes = CreatePdf(pageCount: 1);
        SetupCarrier(carrier);
        SetupLabelPdf(pdfBytes);
        SetupOffsets(Offset(Carriers.Zasilkovna, 0, -5));

        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        (await ReadContentAsync(response)).Should().BeEquivalentTo(pdfBytes);
    }

    [Fact]
    public async Task Handle_OrderHasNoShippingMethod_ReturnsPdfUnchanged()
    {
        var pdfBytes = CreatePdf(pageCount: 1);
        _orderShippingSource
            .Setup(s => s.GetShippingMethodGuidAsync(OrderCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        SetupLabelPdf(pdfBytes);
        SetupOffsets(Offset(Carriers.Zasilkovna, 0, -5));

        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        (await ReadContentAsync(response)).Should().BeEquivalentTo(pdfBytes);
    }

    [Fact]
    public async Task Handle_CarrierLookupThrows_ReturnsPdfUnchangedAndWarns()
    {
        var pdfBytes = CreatePdf(pageCount: 1);
        _orderShippingSource
            .Setup(s => s.GetShippingMethodGuidAsync(OrderCode, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("shoptet down"));
        SetupLabelPdf(pdfBytes);
        SetupOffsets(Offset(Carriers.Zasilkovna, 0, -5));

        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        response.Success.Should().BeTrue();
        (await ReadContentAsync(response)).Should().BeEquivalentTo(pdfBytes);
        VerifyLogged(LogLevel.Warning, Times.Once());
    }

    [Fact]
    public async Task Handle_CarrierLookupTimesOut_ReturnsPdfUnchangedAndWarns()
    {
        var pdfBytes = CreatePdf(pageCount: 1);
        _orderShippingSource
            .Setup(s => s.GetShippingMethodGuidAsync(OrderCode, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("http timeout"));
        SetupLabelPdf(pdfBytes);
        SetupOffsets(Offset(Carriers.Zasilkovna, 0, -5));

        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        response.Success.Should().BeTrue();
        (await ReadContentAsync(response)).Should().BeEquivalentTo(pdfBytes);
        VerifyLogged(LogLevel.Warning, Times.Once());
    }

    [Fact]
    public async Task Handle_CancelledDuringCarrierLookup_Propagates()
    {
        using var cts = new CancellationTokenSource();
        _orderShippingSource
            .Setup(s => s.GetShippingMethodGuidAsync(OrderCode, It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ThrowsAsync(new OperationCanceledException(cts.Token));
        SetupLabelPdf(CreatePdf(pageCount: 1));
        SetupOffsets(Offset(Carriers.Zasilkovna, 0, -5));

        var act = () => CreateHandler().Handle(Request(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Handle_NoOffsetsSaved_DoesNotLookUpCarrier()
    {
        SetupLabelPdf(CreatePdf(pageCount: 1));

        await CreateHandler().Handle(Request(), CancellationToken.None);

        _orderShippingSource.Verify(
            s => s.GetShippingMethodGuidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AllSavedOffsetsAreZero_DoesNotLookUpCarrier()
    {
        SetupLabelPdf(CreatePdf(pageCount: 1));
        SetupOffsets(Offset(Carriers.Zasilkovna, 0, 0), Offset(Carriers.PPL, 0, 0));

        await CreateHandler().Handle(Request(), CancellationToken.None);

        _orderShippingSource.Verify(
            s => s.GetShippingMethodGuidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_OffsetReadThrows_ReturnsPdfUnchanged()
    {
        // A label without the offset is still usable; failing the print is not.
        var pdfBytes = CreatePdf(pageCount: 1);
        SetupLabelPdf(pdfBytes);
        _labelOffsets
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));

        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        response.Success.Should().BeTrue();
        (await ReadContentAsync(response)).Should().BeEquivalentTo(pdfBytes);
        VerifyLogged(LogLevel.Warning, Times.Once());
    }

    [Fact]
    public async Task Handle_RequestCancelledDuringOffsetRead_Throws()
    {
        using var cts = new CancellationTokenSource();
        SetupLabelPdf(CreatePdf(pageCount: 1));
        _labelOffsets
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cts.Cancel();
                return Task.FromException<IReadOnlyList<CarrierLabelOffset>>(new OperationCanceledException(cts.Token));
            });

        var act = () => CreateHandler().Handle(Request(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _httpMessageHandler.Protected().Verify(
            "SendAsync", Times.Never(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OffsetSaved_LabelIsNotAValidPdf_ReturnsOriginalBytes()
    {
        var notAPdf = Encoding.UTF8.GetBytes("%PDF-1.4 fake");
        SetupLabelPdf(notAPdf);
        SetupOffsets(Offset(Carriers.Zasilkovna, 0, -5));

        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        response.Success.Should().BeTrue();
        (await ReadContentAsync(response)).Should().BeEquivalentTo(notAPdf);
        VerifyLogged(LogLevel.Warning, Times.Once());
    }
}
