using System.Net;
using System.Text;
using Anela.Heblo.Application.Features.FeatureFlags;
using Anela.Heblo.Application.Features.Packaging.Contracts;
using Anela.Heblo.Application.Features.Packaging.UseCases.GetPackageLabelPdf;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics;
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

    private readonly Mock<IShipmentClient> _shipmentClient = new();
    private readonly Mock<HttpMessageHandler> _httpMessageHandler = new(MockBehavior.Strict);
    private readonly Mock<IHttpClientFactory> _httpClientFactory = new();
    private readonly Mock<ILogger<GetPackageLabelPdfHandler>> _logger = new();
    private const string ShippingGuid = "shipping-guid";

    private readonly Mock<IPackingOrderShippingSource> _orderShippingSource = new();
    private readonly Mock<IShippingMethodCatalog> _shippingCatalog = new();
    private readonly Mock<IFeatureFlagChecker> _featureFlags = new();

    public GetPackageLabelPdfHandlerTests()
    {
        _httpClientFactory
            .Setup(f => f.CreateClient(GetPackageLabelPdfHandler.HttpClientName))
            .Returns(() => new HttpClient(_httpMessageHandler.Object));
    }

    private GetPackageLabelPdfHandler CreateHandler() =>
        new(_shipmentClient.Object, _httpClientFactory.Object, _orderShippingSource.Object, _shippingCatalog.Object,
            _featureFlags.Object, _logger.Object);

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

    private void SetupGlsRotationFlag(bool isEnabled) =>
        _featureFlags
            .Setup(f => f.IsEnabledAsync(FeatureFlagKeys.GlsLabelRotation, It.IsAny<CancellationToken>()))
            .ReturnsAsync(isEnabled);

    private void SetupCarrier(Carriers? carrier)
    {
        _orderShippingSource
            .Setup(c => c.GetShippingMethodGuidAsync(OrderCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ShippingGuid);
        _shippingCatalog
            .Setup(c => c.ResolveCarrierByShippingGuid(ShippingGuid))
            .Returns(carrier);
    }

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

    private static byte[] CreatePdf(int pageCount, int pageRotation = 0)
    {
        using var document = new PdfDocument();
        for (var i = 0; i < pageCount; i++)
            document.AddPage().Rotate = pageRotation;

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

    private static IReadOnlyList<int> PageRotations(byte[] pdfBytes)
    {
        using var document = PdfReader.Open(new MemoryStream(pdfBytes), PdfDocumentOpenMode.Import);
        return document.Pages.Cast<PdfPage>().Select(p => p.Rotate).ToList();
    }

    [Fact]
    public async Task Handle_GlsRotationFlagOff_ReturnsPdfUnchangedWithoutCarrierLookup()
    {
        var pdfBytes = CreatePdf(pageCount: 1);
        SetupLabelPdf(pdfBytes);
        SetupGlsRotationFlag(isEnabled: false);

        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        (await ReadContentAsync(response)).Should().BeEquivalentTo(pdfBytes);
        _orderShippingSource.Verify(
            c => c.GetShippingMethodGuidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_GlsRotationFlagOn_GlsOrder_RotatesEveryPageBy180()
    {
        SetupLabelPdf(CreatePdf(pageCount: 2));
        SetupGlsRotationFlag(isEnabled: true);
        SetupCarrier(Carriers.GLS);

        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        response.Success.Should().BeTrue();
        response.ContentType.Should().Be("application/pdf");
        PageRotations(await ReadContentAsync(response)).Should().Equal(180, 180);
    }

    [Fact]
    public async Task Handle_GlsRotationFlagOn_GlsOrder_AddsToExistingPageRotation()
    {
        SetupLabelPdf(CreatePdf(pageCount: 1, pageRotation: 270));
        SetupGlsRotationFlag(isEnabled: true);
        SetupCarrier(Carriers.GLS);

        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        PageRotations(await ReadContentAsync(response)).Should().Equal(90);
    }

    [Theory]
    [InlineData(Carriers.PPL)]
    [InlineData(Carriers.Zasilkovna)]
    [InlineData(null)]
    public async Task Handle_GlsRotationFlagOn_NonGlsOrder_ReturnsPdfUnchanged(Carriers? carrier)
    {
        var pdfBytes = CreatePdf(pageCount: 1);
        SetupLabelPdf(pdfBytes);
        SetupGlsRotationFlag(isEnabled: true);
        SetupCarrier(carrier);

        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        (await ReadContentAsync(response)).Should().BeEquivalentTo(pdfBytes);
    }

    [Fact]
    public async Task Handle_GlsRotationFlagOn_OrderHasNoShippingMethod_ReturnsPdfUnchanged()
    {
        var pdfBytes = CreatePdf(pageCount: 1);
        SetupLabelPdf(pdfBytes);
        SetupGlsRotationFlag(isEnabled: true);
        _orderShippingSource
            .Setup(c => c.GetShippingMethodGuidAsync(OrderCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        (await ReadContentAsync(response)).Should().BeEquivalentTo(pdfBytes);
    }

    [Fact]
    public async Task Handle_GlsRotationFlagOn_CarrierLookupThrows_ReturnsPdfUnchanged()
    {
        // An upside-down label is still usable; failing the print is not.
        var pdfBytes = CreatePdf(pageCount: 1);
        SetupLabelPdf(pdfBytes);
        SetupGlsRotationFlag(isEnabled: true);
        _orderShippingSource
            .Setup(c => c.GetShippingMethodGuidAsync(OrderCode, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("shoptet down"));

        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        response.Success.Should().BeTrue();
        (await ReadContentAsync(response)).Should().BeEquivalentTo(pdfBytes);
        VerifyLogged(LogLevel.Warning, Times.Once());
    }

    [Fact]
    public async Task Handle_GlsRotationFlagOn_LabelIsNotAValidPdf_ReturnsOriginalBytes()
    {
        var notAPdf = Encoding.UTF8.GetBytes("%PDF-1.4 fake");
        SetupLabelPdf(notAPdf);
        SetupGlsRotationFlag(isEnabled: true);
        SetupCarrier(Carriers.GLS);

        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        response.Success.Should().BeTrue();
        (await ReadContentAsync(response)).Should().BeEquivalentTo(notAPdf);
        VerifyLogged(LogLevel.Warning, Times.Once());
    }
}
