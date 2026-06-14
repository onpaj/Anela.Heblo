using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.OrgChart.Contracts;
using Anela.Heblo.Application.Features.OrgChart.UseCases.GetOrganizationStructure;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.OrgChart;

public class OrgChartControllerTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<ILogger<OrgChartController>> _logger = new();
    private readonly OrgChartController _controller;

    public OrgChartControllerTests()
    {
        _controller = new OrgChartController(_mediator.Object, _logger.Object);
    }

    [Fact]
    public async Task GetOrganizationStructure_ReturnsOk_WhenHandlerSucceeds()
    {
        // Arrange
        var expected = new OrgChartResponse
        {
            Organization = new OrganizationDto { Name = "Anela", Positions = new List<PositionDto>() }
        };
        _mediator
            .Setup(m => m.Send(It.IsAny<GetOrganizationStructureRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _controller.GetOrganizationStructure(CancellationToken.None);

        // Assert
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);
        ok.Value.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task GetOrganizationStructure_Returns500_WithTypedErrorResponse_WhenHandlerThrows()
    {
        // Arrange
        _mediator
            .Setup(m => m.Send(It.IsAny<GetOrganizationStructureRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("upstream blew up"));

        // Act
        var result = await _controller.GetOrganizationStructure(CancellationToken.None);

        // Assert
        var objectResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(500);
        var body = objectResult.Value.Should().BeOfType<OrgChartResponse>().Subject;
        body.Success.Should().BeFalse();
        body.ErrorCode.Should().Be(ErrorCodes.InternalServerError);
    }

    [Fact]
    public async Task GetOrganizationStructure_DoesNotLeakExceptionMessage_WhenHandlerThrows()
    {
        // Arrange — marker simulates the SharePoint URL / internal detail
        // that today's anonymous-object body would expose to the client.
        const string marker = "SECRET-MARKER-http://internal-sharepoint/site/orgchart.json";
        _mediator
            .Setup(m => m.Send(It.IsAny<GetOrganizationStructureRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(
                $"Failed to fetch organizational structure: {marker}"));

        // Act
        var result = await _controller.GetOrganizationStructure(CancellationToken.None);

        // Assert — serialize the body the way ASP.NET would and assert marker is absent.
        var objectResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(objectResult.Value);
        json.Should().NotContain(marker);
        json.Should().NotContain("SECRET-MARKER");
    }

    [Fact]
    public async Task GetOrganizationStructure_LogsExceptionWithFullDetail_WhenHandlerThrows()
    {
        // Arrange
        var ex = new InvalidOperationException("upstream blew up");
        _mediator
            .Setup(m => m.Send(It.IsAny<GetOrganizationStructureRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(ex);

        // Act
        await _controller.GetOrganizationStructure(CancellationToken.None);

        // Assert — verify _logger.LogError(ex, "Error fetching organizational structure") was called once.
        _logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Error fetching organizational structure")),
                ex,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
