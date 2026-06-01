using System.Net;
using System.Text;
using Anela.Heblo.Application.Features.Packaging.UseCases.GetPackageLabelPdf;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;

namespace Anela.Heblo.Tests.Application.Packaging;

public class GetPackageLabelPdfHandlerTests
{
    private const string OrderCode = "0001234";
    private const string PackageName = "PKG-1";
    private const string LabelUrl = "https://cdn.carrier.test/labels/PKG-1.pdf";

    private readonly Mock<IShipmentClient> _shipmentClient = new();
    private readonly Mock<HttpMessageHandler> _httpMessageHandler = new(MockBehavior.Strict);
    private readonly Mock<IHttpClientFactory> _httpClientFactory = new();

    public GetPackageLabelPdfHandlerTests()
    {
        _httpClientFactory
            .Setup(f => f.CreateClient(GetPackageLabelPdfHandler.HttpClientName))
            .Returns(() => new HttpClient(_httpMessageHandler.Object));
    }

    private GetPackageLabelPdfHandler CreateHandler() =>
        new(_shipmentClient.Object, _httpClientFactory.Object, new Mock<ILogger<GetPackageLabelPdfHandler>>().Object, TimeSpan.Zero);

    private static GetPackageLabelPdfRequest Request() => new()
    {
        OrderCode = OrderCode,
        PackageName = PackageName,
    };

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
    public async Task Handle_PackageNameDoesNotMatch_ReturnsPackageLabelNotFound()
    {
        SetupShipmentLabels(new ShipmentLabel
        {
            ShipmentGuid = Guid.NewGuid(),
            OrderCode = OrderCode,
            PackageName = "DIFFERENT-PKG",
            LabelUrl = LabelUrl,
        });

        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

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
    public async Task Handle_LabelUrlInitiallyNullThenAvailableOnRetry_ReturnsPdf()
    {
        var shipmentGuid = Guid.NewGuid();
        _shipmentClient
            .SetupSequence(c => c.GetLabelsByOrderCodeAsync(OrderCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = OrderCode, PackageName = PackageName, LabelUrl = null }])
            .ReturnsAsync([new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = OrderCode, PackageName = PackageName, LabelUrl = LabelUrl }]);

        var pdfBytes = Encoding.UTF8.GetBytes("%PDF-1.4 fake");
        var pdfResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(pdfBytes),
        };
        pdfResponse.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        SetupHttpResponse(pdfResponse);

        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        response.Success.Should().BeTrue();
        response.FileName.Should().Be($"{OrderCode}-{PackageName}.pdf");
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
        response.FileName.Should().Be($"{OrderCode}-{PackageName}.pdf");
        response.Content.Should().NotBeNull();

        using var ms = new MemoryStream();
        await response.Content!.CopyToAsync(ms);
        ms.ToArray().Should().BeEquivalentTo(pdfBytes);
    }
}
