using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderStatus;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class UpdateManufactureOrderStatusHandlerTests
{
    private readonly Mock<IManufactureOrderRepository> _repositoryMock;
    private readonly Mock<ILogger<UpdateManufactureOrderStatusHandler>> _loggerMock;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private readonly UpdateManufactureOrderStatusHandler _handler;

    private const int ValidOrderId = 1;
    private const int NonExistentOrderId = 999;
    private const string TestUserName = "Test User";
    private const string ValidChangeReason = "Moving to next phase";

    public UpdateManufactureOrderStatusHandlerTests()
    {
        _repositoryMock = new Mock<IManufactureOrderRepository>();
        _loggerMock = new Mock<ILogger<UpdateManufactureOrderStatusHandler>>();
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();

        // Setup HTTP context with user
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, TestUserName)
        };
        var identity = new ClaimsIdentity(claims, "test");
        var principal = new ClaimsPrincipal(identity);
        
        var httpContext = new Mock<HttpContext>();
        httpContext.Setup(x => x.User).Returns(principal);
        
        _httpContextAccessorMock
            .Setup(x => x.HttpContext)
            .Returns(httpContext.Object);

        _handler = new UpdateManufactureOrderStatusHandler(
            _repositoryMock.Object,
            _loggerMock.Object,
            _httpContextAccessorMock.Object);
    }

    [Fact]
    public async Task Handle_WithValidTransition_ShouldUpdateStateAndReturnResponse()
    {
        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = ValidOrderId,
            NewState = ManufactureOrderState.Planned,
            ChangeReason = ValidChangeReason
        };

        var existingOrder = CreateOrderInState(ManufactureOrderState.Draft);
        ManufactureOrder? updatedOrder = null;

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken ct) =>
            {
                updatedOrder = order;
                return order;
            });

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.OldState.Should().Be("Draft");
        result.NewState.Should().Be("Planned");
        result.StateChangedByUser.Should().Be(TestUserName);
        result.StateChangedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));

        updatedOrder.Should().NotBeNull();
        updatedOrder!.State.Should().Be(ManufactureOrderState.Planned);
        updatedOrder.StateChangedByUser.Should().Be(TestUserName);
    }

    [Fact]
    public async Task Handle_WithNonExistentOrder_ShouldReturnResourceNotFoundError()
    {
        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = NonExistentOrderId,
            NewState = ManufactureOrderState.Planned
        };

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(NonExistentOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder?)null);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        result.Params.Should().ContainKey("id");
        result.Params!["id"].Should().Be(NonExistentOrderId.ToString());
    }

    [Theory]
    [InlineData(ManufactureOrderState.Draft, ManufactureOrderState.Draft)] // Same state
    [InlineData(ManufactureOrderState.Completed, ManufactureOrderState.Planned)] // Invalid backwards transition
    [InlineData(ManufactureOrderState.Cancelled, ManufactureOrderState.Draft)] // Cannot change from cancelled
    [InlineData(ManufactureOrderState.Draft, ManufactureOrderState.Completed)] // Skip states
    public async Task Handle_WithInvalidTransition_ShouldReturnInvalidOperationError(
        ManufactureOrderState fromState, ManufactureOrderState toState)
    {
        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = ValidOrderId,
            NewState = toState
        };

        var existingOrder = CreateOrderInState(fromState);

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
        result.Params.Should().ContainKey("oldState");
        result.Params.Should().ContainKey("newState");
        result.Params!["oldState"].Should().Be(fromState.ToString());
        result.Params["newState"].Should().Be(toState.ToString());
    }

    [Theory]
    [InlineData(ManufactureOrderState.Draft, ManufactureOrderState.Planned)]
    [InlineData(ManufactureOrderState.Planned, ManufactureOrderState.SemiProductManufactured)]
    [InlineData(ManufactureOrderState.SemiProductManufactured, ManufactureOrderState.Completed)]
    [InlineData(ManufactureOrderState.Completed, ManufactureOrderState.Cancelled)]
    public async Task Handle_WithValidTransitions_ShouldSucceed(
        ManufactureOrderState fromState, ManufactureOrderState toState)
    {
        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = ValidOrderId,
            NewState = toState
        };

        var existingOrder = CreateOrderInState(fromState);

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken ct) => order);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.OldState.Should().Be(fromState.ToString());
        result.NewState.Should().Be(toState.ToString());
    }

    [Fact]
    public async Task Handle_WithChangeReason_ShouldAddAuditLogEntry()
    {
        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = ValidOrderId,
            NewState = ManufactureOrderState.Planned,
            ChangeReason = ValidChangeReason
        };

        var existingOrder = CreateOrderInState(ManufactureOrderState.Draft);
        ManufactureOrder? updatedOrder = null;

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken ct) =>
            {
                updatedOrder = order;
                return order;
            });

        await _handler.Handle(request, CancellationToken.None);

        updatedOrder.Should().NotBeNull();
        updatedOrder!.AuditLog.Should().HaveCount(1);

        var auditEntry = updatedOrder.AuditLog.First();
        auditEntry.Action.Should().Be(ManufactureOrderAuditAction.StateChanged);
        auditEntry.Details.Should().Be(ValidChangeReason);
        auditEntry.OldValue.Should().Be("Draft");
        auditEntry.NewValue.Should().Be("Planned");
        auditEntry.User.Should().Be(TestUserName);
        auditEntry.ManufactureOrderId.Should().Be(ValidOrderId);
    }

    [Fact]
    public async Task Handle_WithoutChangeReason_ShouldNotAddAuditLogEntry()
    {
        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = ValidOrderId,
            NewState = ManufactureOrderState.Planned
            // No ChangeReason provided
        };

        var existingOrder = CreateOrderInState(ManufactureOrderState.Draft);
        ManufactureOrder? updatedOrder = null;

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken ct) =>
            {
                updatedOrder = order;
                return order;
            });

        await _handler.Handle(request, CancellationToken.None);

        updatedOrder.Should().NotBeNull();
        updatedOrder!.AuditLog.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithoutHttpContext_ShouldUseSystemAsUser()
    {
        _httpContextAccessorMock
            .Setup(x => x.HttpContext)
            .Returns((HttpContext?)null);

        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = ValidOrderId,
            NewState = ManufactureOrderState.Planned
        };

        var existingOrder = CreateOrderInState(ManufactureOrderState.Draft);
        ManufactureOrder? updatedOrder = null;

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken ct) =>
            {
                updatedOrder = order;
                return order;
            });

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.StateChangedByUser.Should().Be("System");
        
        updatedOrder.Should().NotBeNull();
        updatedOrder!.StateChangedByUser.Should().Be("System");
    }

    [Fact]
    public async Task Handle_WhenRepositoryGetThrows_ShouldReturnInternalServerError()
    {
        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = ValidOrderId,
            NewState = ManufactureOrderState.Planned
        };

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InternalServerError);
    }

    [Fact]
    public async Task Handle_WhenRepositoryUpdateThrows_ShouldReturnInternalServerError()
    {
        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = ValidOrderId,
            NewState = ManufactureOrderState.Planned
        };

        var existingOrder = CreateOrderInState(ManufactureOrderState.Draft);

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Update failed"));

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InternalServerError);
    }

    [Fact]
    public async Task Handle_WhenExceptionOccurs_ShouldLogError()
    {
        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = ValidOrderId,
            NewState = ManufactureOrderState.Planned
        };

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        await _handler.Handle(request, CancellationToken.None);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Error updating manufacture order status for order {ValidOrderId}")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private static ManufactureOrder CreateOrderInState(ManufactureOrderState state)
    {
        return new ManufactureOrder
        {
            Id = ValidOrderId,
            OrderNumber = "MO-2024-001",
            CreatedDate = DateTime.UtcNow.AddDays(-1),
            CreatedByUser = "Original User",
            ResponsiblePerson = "Test Person",
            SemiProductPlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(7)),
            ProductPlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(14)),
            State = state,
            StateChangedAt = DateTime.UtcNow.AddDays(-1),
            StateChangedByUser = "Original User"
        };
    }
}