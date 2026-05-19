using System.Net;
using System.Net.Http.Headers;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetShipmentLabelPdf;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Application.ShipmentLabels;

public class GetShipmentLabelPdfHandlerTests
{
    private readonly Mock<IShipmentClient> _clientMock = new();
    private readonly Mock<IHttpClientFactory> _httpFactoryMock = new();

    private static HttpClient BuildHttpClient(HttpResponseMessage response)
    {
        var handler = new FakeHttpMessageHandler(response);
        return new HttpClient(handler);
    }

    private GetShipmentLabelPdfHandler CreateHandler(HttpResponseMessage? pdfHttpResponse = null)
    {
        var fakePdfResponse = pdfHttpResponse ?? new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[] { 1, 2, 3 })
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/pdf") }
            }
        };
        _httpFactoryMock
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(BuildHttpClient(fakePdfResponse));

        return new GetShipmentLabelPdfHandler(
            _clientMock.Object,
            _httpFactoryMock.Object,
            NullLogger<GetShipmentLabelPdfHandler>.Instance);
    }

    [Fact]
    public async Task Handle_ValidPackageWithLabelUrl_ReturnsPdfStream()
    {
        // Arrange
        var guid = Guid.NewGuid();
        _clientMock
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShipmentLabel
            {
                ShipmentGuid = guid,
                OrderCode = "0001234",
                PackageName = "Zásilka 1",
                LabelUrl = "https://carrier.example.com/label.pdf",
            }]);

        var pdfBytes = new byte[] { 1, 2, 3 };
        var fakePdfResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(pdfBytes)
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/pdf") }
            }
        };

        var handler = CreateHandler(fakePdfResponse);

        // Act
        var response = await handler.Handle(
            new GetShipmentLabelPdfRequest
            {
                OrderCode = "0001234",
                ShipmentGuid = guid,
                PackageName = "Zásilka 1",
            },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.PdfStream.Should().NotBeNull();
        var bytes = new byte[3];
        _ = await response.PdfStream!.ReadAsync(bytes);
        bytes.Should().Equal(pdfBytes);
    }

    [Fact]
    public async Task Handle_OrderHasNoShipments_ReturnsNotFound()
    {
        // Arrange
        _clientMock
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001111", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var response = await CreateHandler().Handle(
            new GetShipmentLabelPdfRequest
            {
                OrderCode = "0001111",
                ShipmentGuid = Guid.NewGuid(),
                PackageName = "Zásilka 1",
            },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ShipmentLabelPdfNotFound);
    }

    [Fact]
    public async Task Handle_PackageNameNotFound_ReturnsNotFound()
    {
        // Arrange
        var guid = Guid.NewGuid();
        _clientMock
            .Setup(c => c.GetLabelsByOrderCodeAsync("0002222", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShipmentLabel
            {
                ShipmentGuid = guid,
                OrderCode = "0002222",
                PackageName = "Zásilka 1",
                LabelUrl = "https://carrier.example.com/label.pdf",
            }]);

        // Act
        var response = await CreateHandler().Handle(
            new GetShipmentLabelPdfRequest
            {
                OrderCode = "0002222",
                ShipmentGuid = guid,
                PackageName = "Zásilka 99",
            },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ShipmentLabelPdfNotFound);
    }

    [Fact]
    public async Task Handle_PackageHasNoLabelUrl_ReturnsNotFound()
    {
        // Arrange
        var guid = Guid.NewGuid();
        _clientMock
            .Setup(c => c.GetLabelsByOrderCodeAsync("0003333", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShipmentLabel
            {
                ShipmentGuid = guid,
                OrderCode = "0003333",
                PackageName = "Zásilka 1",
                LabelUrl = null,
            }]);

        // Act
        var response = await CreateHandler().Handle(
            new GetShipmentLabelPdfRequest
            {
                OrderCode = "0003333",
                ShipmentGuid = guid,
                PackageName = "Zásilka 1",
            },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ShipmentLabelPdfNotFound);
    }

    [Fact]
    public async Task Handle_ShipmentClientThrows_ReturnsInternalServerError()
    {
        // Arrange
        _clientMock
            .Setup(c => c.GetLabelsByOrderCodeAsync("0004444", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Shoptet unavailable"));

        // Act
        var response = await CreateHandler().Handle(
            new GetShipmentLabelPdfRequest
            {
                OrderCode = "0004444",
                ShipmentGuid = Guid.NewGuid(),
                PackageName = "Zásilka 1",
            },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.InternalServerError);
    }

    [Fact]
    public async Task Handle_PdfDownloadFails_ReturnsInternalServerError()
    {
        // Arrange
        var guid = Guid.NewGuid();
        _clientMock
            .Setup(c => c.GetLabelsByOrderCodeAsync("0005555", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShipmentLabel
            {
                ShipmentGuid = guid,
                OrderCode = "0005555",
                PackageName = "Zásilka 1",
                LabelUrl = "https://carrier.example.com/label.pdf",
            }]);

        var badResponse = new HttpResponseMessage(HttpStatusCode.InternalServerError);
        var handler = CreateHandler(badResponse);

        // Act
        var response = await handler.Handle(
            new GetShipmentLabelPdfRequest
            {
                OrderCode = "0005555",
                ShipmentGuid = guid,
                PackageName = "Zásilka 1",
            },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.InternalServerError);
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public FakeHttpMessageHandler(HttpResponseMessage response) => _response = response;
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_response);
    }
}
