using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureProtocol;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class ManufactureOrderControllerProtocolTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly ManufactureOrderController _controller;

    public ManufactureOrderControllerProtocolTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _controller = new ManufactureOrderController(_mediatorMock.Object);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        var serviceProvider = serviceCollection.BuildServiceProvider();

        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = serviceProvider;
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    [Fact]
    public async Task GetProtocolPdf_Should_Return_FileResult_With_Pdf_ContentType()
    {
        // Arrange
        var orderId = 42;
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }; // %PDF-
        var fileName = "ManufactureProtocol-MO-2024-042.pdf";

        _mediatorMock
            .Setup(m => m.Send(It.Is<GetManufactureProtocolRequest>(r => r.Id == orderId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetManufactureProtocolResponse
            {
                PdfBytes = pdfBytes,
                FileName = fileName,
            });

        // Act
        var result = await _controller.GetProtocolPdf(orderId, CancellationToken.None);

        // Assert
        var fileResult = result.Result.Should().BeOfType<FileContentResult>().Subject;
        fileResult.ContentType.Should().Be("application/pdf");
        fileResult.FileDownloadName.Should().Be(fileName);
        fileResult.FileContents.Should().BeEquivalentTo(pdfBytes);

        _mediatorMock.Verify(
            m => m.Send(It.Is<GetManufactureProtocolRequest>(r => r.Id == orderId), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetProtocolPdf_Should_Return_BadRequest_When_Order_Not_Completed()
    {
        // Arrange
        var orderId = 1;

        _mediatorMock
            .Setup(m => m.Send(It.Is<GetManufactureProtocolRequest>(r => r.Id == orderId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetManufactureProtocolResponse(
                ErrorCodes.ManufactureOrderNotCompleted,
                new Dictionary<string, string> { { "orderId", "MO-2024-001" }, { "state", "Planned" } }));

        // Act
        var result = await _controller.GetProtocolPdf(orderId, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();

        _mediatorMock.Verify(
            m => m.Send(It.Is<GetManufactureProtocolRequest>(r => r.Id == orderId), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetProtocolPdf_Should_Return_NotFound_When_Order_Not_Found()
    {
        // Arrange
        var orderId = 999;

        _mediatorMock
            .Setup(m => m.Send(It.Is<GetManufactureProtocolRequest>(r => r.Id == orderId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetManufactureProtocolResponse(
                ErrorCodes.OrderNotFound,
                new Dictionary<string, string> { { "orderId", orderId.ToString() } }));

        // Act
        var result = await _controller.GetProtocolPdf(orderId, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }
}
