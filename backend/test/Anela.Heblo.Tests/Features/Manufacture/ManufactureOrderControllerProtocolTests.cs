using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureProtocol;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
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
        var configMock = new Mock<IConfiguration>();
        var serviceMock = new Mock<IManufactureOrderApplicationService>();

        _controller = new ManufactureOrderController(_mediatorMock.Object, configMock.Object, serviceMock.Object);

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
        var result = await _controller.GetProtocolPdf(orderId);

        // Assert
        var fileResult = result.Should().BeOfType<FileContentResult>().Subject;
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
        var errorMessage = "Manufacture order MO-2024-001 must be completed before generating a protocol. Current state: Planned.";

        _mediatorMock
            .Setup(m => m.Send(It.Is<GetManufactureProtocolRequest>(r => r.Id == orderId), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(errorMessage));

        // Act
        var result = await _controller.GetProtocolPdf(orderId);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var body = badRequest.Value.Should().BeAssignableTo<object>().Subject;
        body.ToString().Should().Contain("message");

        _mediatorMock.Verify(
            m => m.Send(It.Is<GetManufactureProtocolRequest>(r => r.Id == orderId), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetProtocolPdf_Should_Return_BadRequest_When_Order_Not_Found()
    {
        // Arrange
        var orderId = 999;

        _mediatorMock
            .Setup(m => m.Send(It.Is<GetManufactureProtocolRequest>(r => r.Id == orderId), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException($"Manufacture order with id {orderId} was not found."));

        // Act
        var result = await _controller.GetProtocolPdf(orderId);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
