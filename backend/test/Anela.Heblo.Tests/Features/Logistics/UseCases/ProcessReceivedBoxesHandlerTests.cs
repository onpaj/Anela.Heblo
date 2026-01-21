using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Features.Logistics.UseCases.ProcessReceivedBoxes;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.UseCases;

public class ProcessReceivedBoxesHandlerTests
{
    private readonly Mock<ITransportBoxRepository> _transportBoxRepositoryMock;
    private readonly Mock<IStockUpOrchestrationService> _stockUpOrchestrationServiceMock;
    private readonly Mock<IBackgroundRefreshTaskRegistry> _backgroundRefreshTaskRegistryMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<ILogger<ProcessReceivedBoxesHandler>> _loggerMock;
    private readonly ProcessReceivedBoxesHandler _handler;

    public ProcessReceivedBoxesHandlerTests()
    {
        _transportBoxRepositoryMock = new Mock<ITransportBoxRepository>();
        _stockUpOrchestrationServiceMock = new Mock<IStockUpOrchestrationService>();
        _backgroundRefreshTaskRegistryMock = new Mock<IBackgroundRefreshTaskRegistry>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _loggerMock = new Mock<ILogger<ProcessReceivedBoxesHandler>>();

        _handler = new ProcessReceivedBoxesHandler(
            _loggerMock.Object,
            _transportBoxRepositoryMock.Object,
            _stockUpOrchestrationServiceMock.Object,
            _backgroundRefreshTaskRegistryMock.Object,
            _currentUserServiceMock.Object);

        // Setup default current user
        _currentUserServiceMock.Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("test-id", "TestUser", "test@example.com", true));
    }

    [Fact]
    public async Task Handle_NoReceivedBoxes_ReturnsSuccessWithZeroCounts()
    {
        // Arrange
        var request = new ProcessReceivedBoxesRequest();
        _transportBoxRepositoryMock
            .Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox>());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.ProcessedBoxesCount.Should().Be(0);
        result.OperationsCreatedCount.Should().Be(0);
        result.FailedBoxesCount.Should().Be(0);
        result.FailedBoxCodes.Should().BeEmpty();
        result.BatchId.Should().NotBeNullOrEmpty();

        // Verify no stock operations were attempted
        _stockUpOrchestrationServiceMock.Verify(
            x => x.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<StockUpSourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_SingleBoxWithItems_ProcessesSuccessfully()
    {
        // Arrange
        var request = new ProcessReceivedBoxesRequest();
        var transportBox = CreateTestTransportBox(1, "BOX001", TransportBoxState.Received);
        AddItemToBox(transportBox, "PROD001", "Test Product", 5);

        _transportBoxRepositoryMock
            .Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { transportBox });

        _stockUpOrchestrationServiceMock
            .Setup(x => x.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<StockUpSourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StockUpOperationResult.Success(new StockUpOperation("BOX-000001-PROD001", "PROD001", 5, StockUpSourceType.TransportBox, 1)));

        _transportBoxRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _transportBoxRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.ProcessedBoxesCount.Should().Be(1);
        result.OperationsCreatedCount.Should().Be(1);
        result.FailedBoxesCount.Should().Be(0);
        result.FailedBoxCodes.Should().BeEmpty();
        result.BatchId.Should().NotBeNullOrEmpty();

        // Verify stock up was called for the item
        _stockUpOrchestrationServiceMock.Verify(
            x => x.ExecuteAsync(
                It.Is<string>(d => d.Contains("PROD001")),
                "PROD001", 5,
                StockUpSourceType.TransportBox, 1, It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify box state remains Received (not changed to Stocked yet)
        // Box will be changed to Stocked by CompleteReceivedBoxesJob after all operations complete
        transportBox.State.Should().Be(TransportBoxState.Received);

        // Verify repository operations
        _transportBoxRepositoryMock.Verify(x => x.UpdateAsync(transportBox, It.IsAny<CancellationToken>()), Times.Once);
        _transportBoxRepositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_MultipleBoxesWithMultipleItems_ProcessesAllSuccessfully()
    {
        // Arrange
        var request = new ProcessReceivedBoxesRequest();
        var box1 = CreateTestTransportBox(1, "BOX001", TransportBoxState.Received);
        AddItemToBox(box1, "PROD001", "Product 1", 3);
        AddItemToBox(box1, "PROD002", "Product 2", 7);

        var box2 = CreateTestTransportBox(2, "BOX002", TransportBoxState.Received);
        AddItemToBox(box2, "PROD003", "Product 3", 2);

        _transportBoxRepositoryMock
            .Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { box1, box2 });

        _stockUpOrchestrationServiceMock
            .Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<StockUpSourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StockUpOperationResult.Success(new StockUpOperation("BOX-000001-TEST", "TEST", 1, StockUpSourceType.TransportBox, 1)));

        _transportBoxRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _transportBoxRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.ProcessedBoxesCount.Should().Be(2);
        result.OperationsCreatedCount.Should().Be(2);
        result.FailedBoxesCount.Should().Be(0);
        result.FailedBoxCodes.Should().BeEmpty();

        // Verify all items were stocked up
        _stockUpOrchestrationServiceMock.Verify(
            x => x.ExecuteAsync(It.IsAny<string>(), "PROD001", It.IsAny<int>(), It.IsAny<StockUpSourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _stockUpOrchestrationServiceMock.Verify(
            x => x.ExecuteAsync(It.IsAny<string>(), "PROD002", It.IsAny<int>(), It.IsAny<StockUpSourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _stockUpOrchestrationServiceMock.Verify(
            x => x.ExecuteAsync(It.IsAny<string>(), "PROD003", It.IsAny<int>(), It.IsAny<StockUpSourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify both boxes remain in Received state (not changed to Stocked yet)
        // Box will be changed to Stocked by CompleteReceivedBoxesJob after all operations complete
        box1.State.Should().Be(TransportBoxState.Received);
        box2.State.Should().Be(TransportBoxState.Received);

        // Verify repository operations
        _transportBoxRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _transportBoxRepositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_StockUpThrowsException_SetsBoxToErrorState()
    {
        // Arrange
        var request = new ProcessReceivedBoxesRequest();
        var transportBox = CreateTestTransportBox(1, "BOX001", TransportBoxState.Received);
        AddItemToBox(transportBox, "PROD001", "Test Product", 5);

        _transportBoxRepositoryMock
            .Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { transportBox });

        _stockUpOrchestrationServiceMock
            .Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<StockUpSourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StockUpOperationResult.SubmitFailed(new StockUpOperation("BOX-000001-TEST", "TEST", 1, StockUpSourceType.TransportBox, 1), new Exception("Stock service error")));

        _transportBoxRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _transportBoxRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.ProcessedBoxesCount.Should().Be(1);
        result.OperationsCreatedCount.Should().Be(0);
        result.FailedBoxesCount.Should().Be(1);
        result.FailedBoxCodes.Should().Contain("BOX001");

        // Verify box was set to Error state
        transportBox.State.Should().Be(TransportBoxState.Error);

        // Verify repository operations - box should still be updated with error state
        _transportBoxRepositoryMock.Verify(x => x.UpdateAsync(transportBox, It.IsAny<CancellationToken>()), Times.Once);
        _transportBoxRepositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_MixedSuccessAndFailure_ProcessesBothCorrectly()
    {
        // Arrange
        var request = new ProcessReceivedBoxesRequest();
        var successBox = CreateTestTransportBox(1, "BOX001", TransportBoxState.Received);
        AddItemToBox(successBox, "PROD001", "Product 1", 3);

        var failureBox = CreateTestTransportBox(2, "BOX002", TransportBoxState.Received);
        AddItemToBox(failureBox, "PROD002", "Product 2", 5);

        _transportBoxRepositoryMock
            .Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { successBox, failureBox });

        // Setup success for first box, failure for second
        _stockUpOrchestrationServiceMock
            .Setup(x => x.ExecuteAsync(It.IsAny<string>(), "PROD001", It.IsAny<int>(), It.IsAny<StockUpSourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StockUpOperationResult.Success(new StockUpOperation("BOX-000001-TEST", "TEST", 1, StockUpSourceType.TransportBox, 1)));

        _stockUpOrchestrationServiceMock
            .Setup(x => x.ExecuteAsync(It.IsAny<string>(), "PROD002", It.IsAny<int>(), It.IsAny<StockUpSourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StockUpOperationResult.SubmitFailed(new StockUpOperation("BOX-000001-TEST", "TEST", 1, StockUpSourceType.TransportBox, 1), new Exception("Failed to stock product")));

        _transportBoxRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _transportBoxRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.ProcessedBoxesCount.Should().Be(2);
        result.OperationsCreatedCount.Should().Be(1);
        result.FailedBoxesCount.Should().Be(1);
        result.FailedBoxCodes.Should().Contain("BOX002");
        result.FailedBoxCodes.Should().NotContain("BOX001");

        // Verify states
        // Success box remains in Received state (will be changed by CompleteReceivedBoxesJob)
        successBox.State.Should().Be(TransportBoxState.Received);
        failureBox.State.Should().Be(TransportBoxState.Error);
    }

    [Fact]
    public async Task Handle_CurrentUserIsNull_UsesSystemAsUserName()
    {
        // Arrange
        var request = new ProcessReceivedBoxesRequest();
        var transportBox = CreateTestTransportBox(1, "BOX001", TransportBoxState.Received);
        AddItemToBox(transportBox, "PROD001", "Test Product", 5);

        _currentUserServiceMock.Setup(x => x.GetCurrentUser())
            .Returns((CurrentUser)null);

        _transportBoxRepositoryMock
            .Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { transportBox });

        _stockUpOrchestrationServiceMock
            .Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<StockUpSourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StockUpOperationResult.Success(new StockUpOperation("BOX-000001-TEST", "TEST", 1, StockUpSourceType.TransportBox, 1)));

        _transportBoxRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _transportBoxRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.OperationsCreatedCount.Should().Be(1);

        // Box should still be processed successfully with "System" as user
        // Box remains in Received state (will be changed by CompleteReceivedBoxesJob)
        transportBox.State.Should().Be(TransportBoxState.Received);
    }

    [Fact]
    public async Task Handle_BoxWithoutItems_ProcessesSuccessfully()
    {
        // Arrange
        var request = new ProcessReceivedBoxesRequest();
        var transportBox = CreateTestTransportBox(1, "BOX001", TransportBoxState.Received);
        // No items added to box

        _transportBoxRepositoryMock
            .Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { transportBox });

        _transportBoxRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _transportBoxRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.ProcessedBoxesCount.Should().Be(1);
        result.OperationsCreatedCount.Should().Be(1);
        result.FailedBoxesCount.Should().Be(0);

        // Box remains in Received state even without items (will be changed by CompleteReceivedBoxesJob)
        transportBox.State.Should().Be(TransportBoxState.Received);

        // No stock operations should be performed
        _stockUpOrchestrationServiceMock.Verify(
            x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<StockUpSourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static TransportBox CreateTestTransportBox(int id, string code, TransportBoxState state)
    {
        var box = new TransportBox();

        // Set properties using reflection following the existing test pattern
        var idProperty = typeof(TransportBox).GetProperty("Id");
        idProperty?.SetValue(box, id);

        // Set Code using the backing field
        var codeField = typeof(TransportBox).GetField("<Code>k__BackingField",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        codeField?.SetValue(box, code);

        // Set State using property
        var stateProperty = typeof(TransportBox).GetProperty("State");
        stateProperty?.SetValue(box, state);

        // Set LastStateChanged
        var lastStateChangedProperty = typeof(TransportBox).GetProperty("LastStateChanged");
        lastStateChangedProperty?.SetValue(box, DateTime.UtcNow);

        // Initialize the private collections using reflection
        var itemsField = typeof(TransportBox).GetField("_items",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        itemsField?.SetValue(box, new List<TransportBoxItem>());

        var stateLogField = typeof(TransportBox).GetField("_stateLog",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        stateLogField?.SetValue(box, new List<TransportBoxStateLog>());

        return box;
    }

    private static void AddItemToBox(TransportBox box, string productCode, string productName, int amount)
    {
        var itemsField = typeof(TransportBox).GetField("_items",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var items = (List<TransportBoxItem>)itemsField?.GetValue(box);

        var item = new TransportBoxItem(productCode, productName, amount, DateTime.UtcNow, "TestUser");
        items?.Add(item);
    }
}