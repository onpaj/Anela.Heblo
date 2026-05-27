using Anela.Heblo.Application.Features.OrgChart.Contracts;
using Anela.Heblo.Application.Features.OrgChart.Services;
using Anela.Heblo.Application.Features.OrgChart.UseCases.GetOrganizationStructure;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.OrgChart;

public class GetOrganizationStructureHandlerTests
{
    private readonly Mock<IOrgChartService> _orgChartServiceMock;
    private readonly Mock<ILogger<GetOrganizationStructureHandler>> _loggerMock;
    private readonly GetOrganizationStructureHandler _handler;

    public GetOrganizationStructureHandlerTests()
    {
        _orgChartServiceMock = new Mock<IOrgChartService>();
        _loggerMock = new Mock<ILogger<GetOrganizationStructureHandler>>();
        _handler = new GetOrganizationStructureHandler(_orgChartServiceMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ReturnsServiceResponse_WhenServiceSucceeds()
    {
        // Arrange
        var request = new GetOrganizationStructureRequest();
        var expected = new OrgChartResponse
        {
            Organization = new OrganizationDto { Name = "Anela" }
        };

        _orgChartServiceMock
            .Setup(x => x.GetOrganizationStructureAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().BeSameAs(expected);
        _orgChartServiceMock.Verify(
            x => x.GetOrganizationStructureAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_PropagatesException_WhenServiceThrows()
    {
        // Arrange
        var request = new GetOrganizationStructureRequest();
        var thrown = new InvalidOperationException("boom");

        _orgChartServiceMock
            .Setup(x => x.GetOrganizationStructureAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(thrown);

        // Act
        var act = async () => await _handler.Handle(request, CancellationToken.None);

        // Assert: the exception propagates UNMODIFIED (same instance, same type, same message).
        var caught = await act.Should().ThrowAsync<InvalidOperationException>();
        caught.Which.Should().BeSameAs(thrown);

        // Assert: the handler does NOT emit its own LogError — the controller owns failure logging.
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}
