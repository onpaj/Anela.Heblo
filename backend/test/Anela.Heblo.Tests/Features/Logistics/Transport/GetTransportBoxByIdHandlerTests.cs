using Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxById;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Transport;

public class GetTransportBoxByIdHandlerTests
{
    private readonly Mock<ITransportBoxRepository> _repositoryMock;
    private readonly Mock<ILogger<GetTransportBoxByIdHandler>> _loggerMock;
    private readonly GetTransportBoxByIdHandler _handler;

    public GetTransportBoxByIdHandlerTests()
    {
        _repositoryMock = new Mock<ITransportBoxRepository>();
        _loggerMock = new Mock<ILogger<GetTransportBoxByIdHandler>>();
        _handler = new GetTransportBoxByIdHandler(_loggerMock.Object, _repositoryMock.Object);
    }

    [Fact]
    public async Task Handle_QuarantineBox_SetsIsInQuarantineTrue()
    {
        // Arrange — use public API to get box into Quarantine state
        var box = new TransportBox();
        box.Open("B001", DateTime.UtcNow, "user");
        box.ToQuarantine(DateTime.UtcNow, "user");

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(1))
            .ReturnsAsync(box);

        // Act
        var result = await _handler.Handle(
            new GetTransportBoxByIdRequest { Id = 1 }, CancellationToken.None);

        // Assert
        result.TransportBox.Should().NotBeNull();
        result.TransportBox!.IsInQuarantine.Should().BeTrue();
        result.TransportBox.IsInReserve.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_QuarantineBox_AllowedTransitionsIncludeVKaranteneLabel()
    {
        // Arrange
        var box = new TransportBox();
        box.Open("B001", DateTime.UtcNow, "user");
        // The Quarantine transition is available from Opened state (before calling ToQuarantine)
        // Test the label by checking the allowed transitions on an Opened box

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(1))
            .ReturnsAsync(box);

        // Act
        var result = await _handler.Handle(
            new GetTransportBoxByIdRequest { Id = 1 }, CancellationToken.None);

        // Assert — Quarantine transition should have Czech label
        var quarantineTransition = result.TransportBox!.AllowedTransitions
            .FirstOrDefault(t => t.NewState == "Quarantine");
        quarantineTransition.Should().NotBeNull();
        quarantineTransition!.Label.Should().Be("V karanténě");
    }
}
