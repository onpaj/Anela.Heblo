using Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxById;
using Anela.Heblo.Application.Features.Logistics.UseCases.UpdateTransportBoxDescription;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Transport;

public class UpdateTransportBoxDescriptionHandlerTests
{
    private readonly Mock<ITransportBoxRepository> _repositoryMock;
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<ILogger<UpdateTransportBoxDescriptionHandler>> _loggerMock;
    private readonly UpdateTransportBoxDescriptionHandler _handler;

    public UpdateTransportBoxDescriptionHandlerTests()
    {
        _repositoryMock = new Mock<ITransportBoxRepository>();
        _mediatorMock = new Mock<IMediator>();
        _loggerMock = new Mock<ILogger<UpdateTransportBoxDescriptionHandler>>();

        _handler = new UpdateTransportBoxDescriptionHandler(
            _repositoryMock.Object,
            _mediatorMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_BoxNotFound_ReturnsTransportBoxNotFoundError()
    {
        // Arrange
        var request = new UpdateTransportBoxDescriptionRequest { BoxId = 999, Description = "New description" };

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(999))
            .ReturnsAsync((TransportBox?)null);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.TransportBoxNotFound);
        result.Params.Should().ContainKey("BoxId").WhoseValue.Should().Be("999");
        result.UpdatedBox.Should().BeNull();

        _repositoryMock.Verify(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _mediatorMock.Verify(x => x.Send(It.IsAny<GetTransportBoxByIdRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_RepositoryThrows_ReturnsTransportBoxStateChangeError()
    {
        // Arrange
        var request = new UpdateTransportBoxDescriptionRequest { BoxId = 42, Description = "New description" };

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(42))
            .ThrowsAsync(new InvalidOperationException("simulated repository failure"));

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.TransportBoxStateChangeError);
        result.Params.Should().ContainKey("boxId").WhoseValue.Should().Be("42");
        result.UpdatedBox.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ValidRequest_UpdatesDescriptionAndReturnsUpdatedBox()
    {
        // Arrange
        var box = CreateBox(id: 7, description: "Old description");
        var request = new UpdateTransportBoxDescriptionRequest { BoxId = 7, Description = "New description" };
        var mediatorResponse = new GetTransportBoxByIdResponse();

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(7))
            .ReturnsAsync(box);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _mediatorMock
            .Setup(x => x.Send(It.Is<GetTransportBoxByIdRequest>(r => r.Id == 7), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mediatorResponse);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.UpdatedBox.Should().BeSameAs(mediatorResponse);
        box.Description.Should().Be("New description");

        _repositoryMock.Verify(x => x.UpdateAsync(box, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _mediatorMock.Verify(
            x => x.Send(It.Is<GetTransportBoxByIdRequest>(r => r.Id == 7), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static TransportBox CreateBox(int id, string? description = null)
    {
        var box = new TransportBox
        {
            Id = id,
            Description = description
        };

        return box;
    }
}
