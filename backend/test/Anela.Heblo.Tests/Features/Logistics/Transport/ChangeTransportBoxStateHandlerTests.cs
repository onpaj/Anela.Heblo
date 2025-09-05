using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.Transport.UseCases;
using Anela.Heblo.Application.Features.Transport.UseCases.ChangeTransportBoxState;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Transport;

public class ChangeTransportBoxStateHandlerTests
{
    private readonly Mock<ITransportBoxRepository> _repositoryMock;
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<ILogger<ChangeTransportBoxStateHandler>> _loggerMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly ChangeTransportBoxStateHandler _handler;

    public ChangeTransportBoxStateHandlerTests()
    {
        _repositoryMock = new Mock<ITransportBoxRepository>();
        _mediatorMock = new Mock<IMediator>();
        _loggerMock = new Mock<ILogger<ChangeTransportBoxStateHandler>>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _timeProviderMock = new Mock<TimeProvider>();

        // Setup default returns for the new dependencies
        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("test-user", "Test User", "test@example.com", true));

        _timeProviderMock
            .Setup(x => x.GetUtcNow())
            .Returns(new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero));

        _handler = new ChangeTransportBoxStateHandler(
            _repositoryMock.Object,
            _mediatorMock.Object,
            _loggerMock.Object,
            _currentUserServiceMock.Object,
            _timeProviderMock.Object);
    }

    [Fact]
    public async Task Handle_BoxNotFound_ReturnsFailureResponse()
    {
        // Arrange
        var request = new ChangeTransportBoxStateRequest
        {
            BoxId = 999,
            NewState = TransportBoxState.Opened
        };

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(999))
            .ReturnsAsync((TransportBox?)null);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.TransportBoxNotFound);
        result.Params.Should().ContainKey("BoxId");
        result.UpdatedBox.Should().BeNull();
    }


    [Fact]
    public async Task Handle_ValidTransition_NewToOpened_ReturnsSuccess()
    {
        // Arrange
        var box = CreateTestBox(TransportBoxState.New);
        var request = new ChangeTransportBoxStateRequest
        {
            BoxId = 1,
            NewState = TransportBoxState.Opened,
            BoxCode = "B999"
        };

        var updatedBoxResponse = new GetTransportBoxByIdResponse();

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(1))
            .ReturnsAsync(box);

        _repositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<GetTransportBoxByIdRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedBoxResponse);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.ErrorCode.Should().BeNull();
        result.UpdatedBox.Should().Be(updatedBoxResponse);

        _repositoryMock.Verify(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()), Times.Once);
        _mediatorMock.Verify(x => x.Send(It.IsAny<GetTransportBoxByIdRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

   

    [Fact]
    public async Task Handle_OpenedToInTransit_WithItems_ReturnsSuccess()
    {
        // Arrange
        var box = CreateTestBoxWithItems(TransportBoxState.Opened);
        var request = new ChangeTransportBoxStateRequest
        {
            BoxId = 1,
            NewState = TransportBoxState.InTransit
        };

        var updatedBoxResponse = new GetTransportBoxByIdResponse();

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(1))
            .ReturnsAsync(() => box);

        _repositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<GetTransportBoxByIdRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedBoxResponse);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.ErrorCode.Should().BeNull();
        result.UpdatedBox.Should().Be(updatedBoxResponse);

        _repositoryMock.Verify(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("InTransit", "Received")]
    [InlineData("Stocked", "Closed")]
    public async Task Handle_ValidTransitionChain_ReturnsSuccess(string fromState, string toState)
    {
        // Arrange
        var initialState = Enum.Parse<TransportBoxState>(fromState);
        var newState = Enum.Parse<TransportBoxState>(toState);
        var box = CreateTestBox(initialState);

        var request = new ChangeTransportBoxStateRequest
        {
            BoxId = 1,
            NewState = newState
        };

        var updatedBoxResponse = new GetTransportBoxByIdResponse();

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(1))
            .ReturnsAsync(box);

        _repositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<GetTransportBoxByIdRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedBoxResponse);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.ErrorCode.Should().BeNull();
        result.UpdatedBox.Should().Be(updatedBoxResponse);

        _repositoryMock.Verify(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ExceptionThrown_ReturnsFailureResponse()
    {
        // Arrange
        var request = new ChangeTransportBoxStateRequest
        {
            BoxId = 1,
            NewState = TransportBoxState.Opened,
        };

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(1))
            .ThrowsAsync(new Exception("Database connection failed"));

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.TransportBoxStateChangeError);
        result.UpdatedBox.Should().BeNull();
    }

    private TransportBox CreateTestBox(TransportBoxState state)
    {
        var box = new TransportBox();

        if (state == TransportBoxState.New)
        {
            // For New state, set the code using reflection since it needs to be set for the transition
            var codeField = typeof(TransportBox).GetField("<Code>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            codeField?.SetValue(box, "TEST-BOX-001");
        }
        else
        {
            // For other states, we need to simulate having gone through proper state transitions
            // Use reflection to set both state and code
            var stateProperty = typeof(TransportBox).GetProperty("State");
            stateProperty?.SetValue(box, state);

            var codeField = typeof(TransportBox).GetField("<Code>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            codeField?.SetValue(box, "TEST-BOX-001");
        }

        return box;
    }

    private TransportBox CreateTestBoxWithoutCode(TransportBoxState state)
    {
        var box = new TransportBox();

        // Use reflection to set the private state without code for testing condition failures
        var stateProperty = typeof(TransportBox).GetProperty("State");
        stateProperty?.SetValue(box, state);

        return box;
    }

    private TransportBox CreateTestBoxWithItems(TransportBoxState state)
    {
        var box = CreateTestBox(state);

        // Add a test item to the box using reflection
        var itemsField = typeof(TransportBox).GetField("_items", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (itemsField != null)
        {
            var items = (List<TransportBoxItem>)itemsField.GetValue(box)!;

            // Create a test item using reflection since constructor might be internal/private
            var itemType = typeof(TransportBoxItem);
            var item = Activator.CreateInstance(itemType,
                "TEST-PRODUCT",
                "Test Product",
                1.0,
                DateTime.Now,
                "TestUser");

            if (item != null)
            {
                items.Add((TransportBoxItem)item);
            }
        }

        return box;
    }
}