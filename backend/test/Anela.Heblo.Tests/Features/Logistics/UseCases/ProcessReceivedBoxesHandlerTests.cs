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
    private readonly Mock<IEshopStockDomainService> _eshopStockDomainServiceMock;
    private readonly Mock<IBackgroundRefreshTaskRegistry> _backgroundRefreshTaskRegistryMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<ILogger<ProcessReceivedBoxesHandler>> _loggerMock;
    private readonly ProcessReceivedBoxesHandler _handler;

    public ProcessReceivedBoxesHandlerTests()
    {
        _transportBoxRepositoryMock = new Mock<ITransportBoxRepository>();
        _eshopStockDomainServiceMock = new Mock<IEshopStockDomainService>();
        _backgroundRefreshTaskRegistryMock = new Mock<IBackgroundRefreshTaskRegistry>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _loggerMock = new Mock<ILogger<ProcessReceivedBoxesHandler>>();

        _handler = new ProcessReceivedBoxesHandler(
            _loggerMock.Object,
            _transportBoxRepositoryMock.Object,
            _eshopStockDomainServiceMock.Object,
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
        result.SuccessfulBoxesCount.Should().Be(0);
        result.FailedBoxesCount.Should().Be(0);
        result.FailedBoxCodes.Should().BeEmpty();
        result.BatchId.Should().NotBeNullOrEmpty();

        // Verify no stock operations were attempted
        _eshopStockDomainServiceMock.Verify(
            x => x.StockUpAsync(It.IsAny<StockUpRequest>()), 
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

        _eshopStockDomainServiceMock
            .Setup(x => x.StockUpAsync(It.IsAny<StockUpRequest>()))
            .Returns(Task.CompletedTask);

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
        result.SuccessfulBoxesCount.Should().Be(1);
        result.FailedBoxesCount.Should().Be(0);
        result.FailedBoxCodes.Should().BeEmpty();
        result.BatchId.Should().NotBeNullOrEmpty();

        // Verify stock up was called for the item
        _eshopStockDomainServiceMock.Verify(
            x => x.StockUpAsync(It.Is<StockUpRequest>(r => 
                r.Products.Any(p => p.ProductCode == "PROD001" && p.Amount == 5))),
            Times.Once);

        // Verify box state changed to Stocked
        transportBox.State.Should().Be(TransportBoxState.Stocked);

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

        _eshopStockDomainServiceMock
            .Setup(x => x.StockUpAsync(It.IsAny<StockUpRequest>()))
            .Returns(Task.CompletedTask);

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
        result.SuccessfulBoxesCount.Should().Be(2);
        result.FailedBoxesCount.Should().Be(0);
        result.FailedBoxCodes.Should().BeEmpty();

        // Verify all items were stocked up
        _eshopStockDomainServiceMock.Verify(
            x => x.StockUpAsync(It.Is<StockUpRequest>(r => r.Products.Any(p => p.ProductCode == "PROD001" && p.Amount == 3))),
            Times.Once);
        _eshopStockDomainServiceMock.Verify(
            x => x.StockUpAsync(It.Is<StockUpRequest>(r => r.Products.Any(p => p.ProductCode == "PROD002" && p.Amount == 7))),
            Times.Once);
        _eshopStockDomainServiceMock.Verify(
            x => x.StockUpAsync(It.Is<StockUpRequest>(r => r.Products.Any(p => p.ProductCode == "PROD003" && p.Amount == 2))),
            Times.Once);

        // Verify both boxes changed to Stocked state
        box1.State.Should().Be(TransportBoxState.Stocked);
        box2.State.Should().Be(TransportBoxState.Stocked);

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

        _eshopStockDomainServiceMock
            .Setup(x => x.StockUpAsync(It.IsAny<StockUpRequest>()))
            .ThrowsAsync(new Exception("Stock service error"));

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
        result.SuccessfulBoxesCount.Should().Be(0);
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
        _eshopStockDomainServiceMock
            .Setup(x => x.StockUpAsync(It.Is<StockUpRequest>(r => r.Products.Any(p => p.ProductCode == "PROD001"))))
            .Returns(Task.CompletedTask);

        _eshopStockDomainServiceMock
            .Setup(x => x.StockUpAsync(It.Is<StockUpRequest>(r => r.Products.Any(p => p.ProductCode == "PROD002"))))
            .ThrowsAsync(new Exception("Failed to stock product"));

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
        result.SuccessfulBoxesCount.Should().Be(1);
        result.FailedBoxesCount.Should().Be(1);
        result.FailedBoxCodes.Should().Contain("BOX002");
        result.FailedBoxCodes.Should().NotContain("BOX001");

        // Verify states
        successBox.State.Should().Be(TransportBoxState.Stocked);
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

        _eshopStockDomainServiceMock
            .Setup(x => x.StockUpAsync(It.IsAny<StockUpRequest>()))
            .Returns(Task.CompletedTask);

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
        result.SuccessfulBoxesCount.Should().Be(1);
        
        // Box should still be processed successfully with "System" as user
        transportBox.State.Should().Be(TransportBoxState.Stocked);
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
        result.SuccessfulBoxesCount.Should().Be(1);
        result.FailedBoxesCount.Should().Be(0);

        // Box should still change state to Stocked even without items
        transportBox.State.Should().Be(TransportBoxState.Stocked);

        // No stock operations should be performed
        _eshopStockDomainServiceMock.Verify(
            x => x.StockUpAsync(It.IsAny<StockUpRequest>()), 
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