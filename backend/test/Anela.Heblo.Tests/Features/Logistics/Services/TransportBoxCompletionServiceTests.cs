using Anela.Heblo.Application.Features.Logistics.Services;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Services;

public class TransportBoxCompletionServiceTests
{
    private readonly Mock<ILogger<TransportBoxCompletionService>> _loggerMock;
    private readonly Mock<ITransportBoxRepository> _transportBoxRepositoryMock;
    private readonly Mock<IStockUpOperationRepository> _stockUpOperationRepositoryMock;
    private readonly TransportBoxCompletionService _service;

    public TransportBoxCompletionServiceTests()
    {
        _loggerMock = new Mock<ILogger<TransportBoxCompletionService>>();
        _transportBoxRepositoryMock = new Mock<ITransportBoxRepository>();
        _stockUpOperationRepositoryMock = new Mock<IStockUpOperationRepository>();
        _service = new TransportBoxCompletionService(
            _loggerMock.Object,
            _transportBoxRepositoryMock.Object,
            _stockUpOperationRepositoryMock.Object);
    }

    [Fact]
    public async Task CompleteReceivedBoxesAsync_NoReceivedBoxes_DoesNothing()
    {
        // Arrange
        _transportBoxRepositoryMock
            .Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox>());

        // Act
        await _service.CompleteReceivedBoxesAsync(CancellationToken.None);

        // Assert
        _transportBoxRepositoryMock.Verify(
            x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CompleteReceivedBoxesAsync_AllOperationsCompleted_TransitionsBoxToStocked()
    {
        // Arrange
        var box = CreateBox(1, "BOX-001", TransportBoxState.Received);
        _transportBoxRepositoryMock
            .Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { box });

        var operations = new List<StockUpOperation>
        {
            CreateOperation(1, "BOX-000001-PROD1", StockUpOperationState.Completed),
            CreateOperation(2, "BOX-000001-PROD2", StockUpOperationState.Completed)
        };
        _stockUpOperationRepositoryMock
            .Setup(x => x.GetBySourceAsync(
                StockUpSourceType.TransportBox,
                box.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(operations);

        _transportBoxRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _transportBoxRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        await _service.CompleteReceivedBoxesAsync(CancellationToken.None);

        // Assert
        box.State.Should().Be(TransportBoxState.Stocked);
        _transportBoxRepositoryMock.Verify(
            x => x.UpdateAsync(box, It.IsAny<CancellationToken>()),
            Times.Once);
        _transportBoxRepositoryMock.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CompleteReceivedBoxesAsync_AnyOperationFailed_TransitionsBoxToError()
    {
        // Arrange
        var box = CreateBox(1, "BOX-001", TransportBoxState.Received);
        _transportBoxRepositoryMock
            .Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { box });

        var operations = new List<StockUpOperation>
        {
            CreateOperation(1, "BOX-000001-PROD1", StockUpOperationState.Completed),
            CreateOperation(2, "BOX-000001-PROD2", StockUpOperationState.Failed)
        };
        _stockUpOperationRepositoryMock
            .Setup(x => x.GetBySourceAsync(
                StockUpSourceType.TransportBox,
                box.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(operations);

        _transportBoxRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _transportBoxRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        await _service.CompleteReceivedBoxesAsync(CancellationToken.None);

        // Assert
        box.State.Should().Be(TransportBoxState.Error);
    }

    [Fact]
    public async Task CompleteReceivedBoxesAsync_OperationsPending_LeavesBoxInReceived()
    {
        // Arrange
        var box = CreateBox(1, "BOX-001", TransportBoxState.Received);
        _transportBoxRepositoryMock
            .Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { box });

        var operations = new List<StockUpOperation>
        {
            CreateOperation(1, "BOX-000001-PROD1", StockUpOperationState.Completed),
            CreateOperation(2, "BOX-000001-PROD2", StockUpOperationState.Pending)
        };
        _stockUpOperationRepositoryMock
            .Setup(x => x.GetBySourceAsync(
                StockUpSourceType.TransportBox,
                box.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(operations);

        // Act
        await _service.CompleteReceivedBoxesAsync(CancellationToken.None);

        // Assert
        box.State.Should().Be(TransportBoxState.Received); // Unchanged
        _transportBoxRepositoryMock.Verify(
            x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CompleteReceivedBoxesAsync_NoOperationsForBox_TransitionsToError()
    {
        // Arrange
        var box = CreateBox(1, "BOX-001", TransportBoxState.Received);
        _transportBoxRepositoryMock
            .Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { box });

        _stockUpOperationRepositoryMock
            .Setup(x => x.GetBySourceAsync(
                StockUpSourceType.TransportBox,
                box.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockUpOperation>());

        _transportBoxRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _transportBoxRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        await _service.CompleteReceivedBoxesAsync(CancellationToken.None);

        // Assert
        box.State.Should().Be(TransportBoxState.Error);
    }

    [Fact]
    public async Task CompleteReceivedBoxesAsync_MultipleBoxes_ProcessesAll()
    {
        // Arrange
        var box1 = CreateBox(1, "BOX-001", TransportBoxState.Received);
        var box2 = CreateBox(2, "BOX-002", TransportBoxState.Received);
        var box3 = CreateBox(3, "BOX-003", TransportBoxState.Received);

        _transportBoxRepositoryMock
            .Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { box1, box2, box3 });

        // Box1: All completed -> should transition to Stocked
        var operations1 = new List<StockUpOperation>
        {
            CreateOperation(1, "BOX-000001-PROD1", StockUpOperationState.Completed)
        };
        _stockUpOperationRepositoryMock
            .Setup(x => x.GetBySourceAsync(
                StockUpSourceType.TransportBox,
                box1.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(operations1);

        // Box2: Has failed operations -> should transition to Error
        var operations2 = new List<StockUpOperation>
        {
            CreateOperation(2, "BOX-000002-PROD1", StockUpOperationState.Failed)
        };
        _stockUpOperationRepositoryMock
            .Setup(x => x.GetBySourceAsync(
                StockUpSourceType.TransportBox,
                box2.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(operations2);

        // Box3: Still pending -> should remain in Received
        var operations3 = new List<StockUpOperation>
        {
            CreateOperation(3, "BOX-000003-PROD1", StockUpOperationState.Pending)
        };
        _stockUpOperationRepositoryMock
            .Setup(x => x.GetBySourceAsync(
                StockUpSourceType.TransportBox,
                box3.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(operations3);

        _transportBoxRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _transportBoxRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        await _service.CompleteReceivedBoxesAsync(CancellationToken.None);

        // Assert
        box1.State.Should().Be(TransportBoxState.Stocked);
        box2.State.Should().Be(TransportBoxState.Error);
        box3.State.Should().Be(TransportBoxState.Received);

        // Verify box1 and box2 were updated (box3 not)
        _transportBoxRepositoryMock.Verify(
            x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        _transportBoxRepositoryMock.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task CompleteReceivedBoxesAsync_OperationsSubmitted_LeavesBoxInReceived()
    {
        // Arrange
        var box = CreateBox(1, "BOX-001", TransportBoxState.Received);
        _transportBoxRepositoryMock
            .Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { box });

        var operations = new List<StockUpOperation>
        {
            CreateOperation(1, "BOX-000001-PROD1", StockUpOperationState.Completed),
            CreateOperation(2, "BOX-000001-PROD2", StockUpOperationState.Submitted)
        };
        _stockUpOperationRepositoryMock
            .Setup(x => x.GetBySourceAsync(
                StockUpSourceType.TransportBox,
                box.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(operations);

        // Act
        await _service.CompleteReceivedBoxesAsync(CancellationToken.None);

        // Assert
        box.State.Should().Be(TransportBoxState.Received); // Unchanged
        _transportBoxRepositoryMock.Verify(
            x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Helper methods
    private TransportBox CreateBox(int id, string code, TransportBoxState state)
    {
        var box = new TransportBox();
        typeof(TransportBox).GetProperty("Id")!.SetValue(box, id);
        typeof(TransportBox).GetProperty("Code")!.SetValue(box, code);
        typeof(TransportBox).GetProperty("State")!.SetValue(box, state);
        return box;
    }

    private StockUpOperation CreateOperation(int id, string documentNumber, StockUpOperationState state)
    {
        var operation = new StockUpOperation(
            documentNumber,
            "PROD001",
            100,
            StockUpSourceType.TransportBox,
            1);

        typeof(StockUpOperation).GetProperty("Id")!.SetValue(operation, id);
        typeof(StockUpOperation).GetProperty("State")!.SetValue(operation, state);

        return operation;
    }
}
