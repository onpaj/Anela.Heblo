using Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxSummary;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Transport;

public class GetTransportBoxSummaryHandlerTests
{
    private readonly Mock<ITransportBoxRepository> _repositoryMock;
    private readonly GetTransportBoxSummaryHandler _handler;

    public GetTransportBoxSummaryHandlerTests()
    {
        _repositoryMock = new Mock<ITransportBoxRepository>();
        _handler = new GetTransportBoxSummaryHandler(_repositoryMock.Object);
    }

    [Fact]
    public async Task Handle_ReturnsTotalsFromSqlAggregation()
    {
        // Arrange
        var summary = new Dictionary<TransportBoxState, int>
        {
            { TransportBoxState.New, 2 },
            { TransportBoxState.Opened, 3 },
            { TransportBoxState.InTransit, 1 },
            { TransportBoxState.Closed, 4 },
        };

        _repositoryMock
            .Setup(r => r.GetStateSummaryAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);

        // Act
        var result = await _handler.Handle(new GetTransportBoxSummaryRequest(), CancellationToken.None);

        // Assert
        result.TotalBoxes.Should().Be(10);
        result.ActiveBoxes.Should().Be(6); // New + Opened + InTransit
        result.StatesCounts["New"].Should().Be(2);
        result.StatesCounts["Opened"].Should().Be(3);
        result.StatesCounts["InTransit"].Should().Be(1);
        result.StatesCounts["Closed"].Should().Be(4);
    }

    [Fact]
    public async Task Handle_StatesMissingFromDb_DefaultToZero()
    {
        // Arrange — only some states present in DB
        var summary = new Dictionary<TransportBoxState, int>
        {
            { TransportBoxState.Opened, 5 },
        };

        _repositoryMock
            .Setup(r => r.GetStateSummaryAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);

        // Act
        var result = await _handler.Handle(new GetTransportBoxSummaryRequest(), CancellationToken.None);

        // Assert
        result.TotalBoxes.Should().Be(5);
        result.ActiveBoxes.Should().Be(5);
        result.StatesCounts["Closed"].Should().Be(0);
        result.StatesCounts["New"].Should().Be(0);
        result.StatesCounts.Should().ContainKeys(
            Enum.GetValues<TransportBoxState>().Select(s => s.ToString()));
    }

    [Fact]
    public async Task Handle_ForwardsFiltersToRepository()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetStateSummaryAsync("B001", "PROD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<TransportBoxState, int> { { TransportBoxState.Opened, 1 } });

        var request = new GetTransportBoxSummaryRequest { Code = "B001", ProductCode = "PROD1" };

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _repositoryMock.Verify(
            r => r.GetStateSummaryAsync("B001", "PROD1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_EmptyDb_ReturnsAllZeros()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetStateSummaryAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<TransportBoxState, int>());

        // Act
        var result = await _handler.Handle(new GetTransportBoxSummaryRequest(), CancellationToken.None);

        // Assert
        result.TotalBoxes.Should().Be(0);
        result.ActiveBoxes.Should().Be(0);
        result.StatesCounts.Values.Should().AllBeEquivalentTo(0);
    }
}
