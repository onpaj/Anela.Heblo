using Anela.Heblo.Application.Features.Logistics.Infrastructure.Jobs;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Jobs;

public class CompleteReceivedBoxesJobTests
{
    private readonly Mock<ILogger<CompleteReceivedBoxesJob>> _logger;
    private readonly Mock<ITransportBoxRepository> _transportBoxRepository;
    private readonly Mock<IStockUpOperationRepository> _stockUpOperationRepository;
    private readonly Mock<IRecurringJobStatusChecker> _statusChecker;
    private readonly CompleteReceivedBoxesJob _job;

    public CompleteReceivedBoxesJobTests()
    {
        _logger = new Mock<ILogger<CompleteReceivedBoxesJob>>();
        _transportBoxRepository = new Mock<ITransportBoxRepository>();
        _stockUpOperationRepository = new Mock<IStockUpOperationRepository>();
        _statusChecker = new Mock<IRecurringJobStatusChecker>();

        // By default, job is enabled
        _statusChecker.Setup(x => x.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _job = new CompleteReceivedBoxesJob(
            _logger.Object,
            _transportBoxRepository.Object,
            _stockUpOperationRepository.Object,
            _statusChecker.Object);
    }

    [Fact]
    public async Task ExecuteAsync_NoReceivedBoxes_DoesNothing()
    {
        // Arrange
        _transportBoxRepository.Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox>());

        // Act
        await _job.ExecuteAsync(CancellationToken.None);

        // Assert
        _transportBoxRepository.Verify(
            x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_AllOperationsCompleted_TransitionsBoxToStocked()
    {
        // Arrange
        var box = CreateBox(1, "B001", TransportBoxState.Received);
        _transportBoxRepository.Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { box });

        var operations = new List<StockUpOperation>
        {
            CreateOperation(1, "BOX-000001-PROD1", StockUpOperationState.Completed),
            CreateOperation(2, "BOX-000001-PROD2", StockUpOperationState.Completed)
        };
        _stockUpOperationRepository.Setup(x => x.GetBySourceAsync(
                StockUpSourceType.TransportBox,
                box.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(operations);

        // Act
        await _job.ExecuteAsync(CancellationToken.None);

        // Assert
        box.State.Should().Be(TransportBoxState.Stocked);
        _transportBoxRepository.Verify(
            x => x.UpdateAsync(box, It.IsAny<CancellationToken>()),
            Times.Once);
        _transportBoxRepository.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_AnyOperationFailed_TransitionsBoxToError()
    {
        // Arrange
        var box = CreateBox(1, "B001", TransportBoxState.Received);
        _transportBoxRepository.Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { box });

        var operations = new List<StockUpOperation>
        {
            CreateOperation(1, "BOX-000001-PROD1", StockUpOperationState.Completed),
            CreateOperation(2, "BOX-000001-PROD2", StockUpOperationState.Failed)
        };
        _stockUpOperationRepository.Setup(x => x.GetBySourceAsync(
                StockUpSourceType.TransportBox,
                box.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(operations);

        // Act
        await _job.ExecuteAsync(CancellationToken.None);

        // Assert
        box.State.Should().Be(TransportBoxState.Error);
        var lastStateLog = box.StateLog.LastOrDefault();
        lastStateLog.Should().NotBeNull();
        lastStateLog!.State.Should().Be(TransportBoxState.Error);
        lastStateLog.Description.Should().Contain("failed");
        _transportBoxRepository.Verify(
            x => x.UpdateAsync(box, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_OperationsPending_LeavesBoxInReceived()
    {
        // Arrange
        var box = CreateBox(1, "B001", TransportBoxState.Received);
        _transportBoxRepository.Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { box });

        var operations = new List<StockUpOperation>
        {
            CreateOperation(1, "BOX-000001-PROD1", StockUpOperationState.Completed),
            CreateOperation(2, "BOX-000001-PROD2", StockUpOperationState.Pending)
        };
        _stockUpOperationRepository.Setup(x => x.GetBySourceAsync(
                StockUpSourceType.TransportBox,
                box.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(operations);

        // Act
        await _job.ExecuteAsync(CancellationToken.None);

        // Assert
        box.State.Should().Be(TransportBoxState.Received); // Unchanged
        _transportBoxRepository.Verify(
            x => x.UpdateAsync(box, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_NoOperationsForBox_TransitionsToError()
    {
        // Arrange
        var box = CreateBox(1, "B001", TransportBoxState.Received);
        _transportBoxRepository.Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { box });

        _stockUpOperationRepository.Setup(x => x.GetBySourceAsync(
                StockUpSourceType.TransportBox,
                box.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockUpOperation>());

        // Act
        await _job.ExecuteAsync(CancellationToken.None);

        // Assert
        box.State.Should().Be(TransportBoxState.Error);
        var lastStateLog = box.StateLog.LastOrDefault();
        lastStateLog.Should().NotBeNull();
        lastStateLog!.State.Should().Be(TransportBoxState.Error);
        lastStateLog.Description.Should().Contain("No stock-up operations");
    }

    [Fact]
    public async Task ExecuteAsync_MultipleBoxes_ProcessesAllCorrectly()
    {
        // Arrange
        var box1 = CreateBox(1, "B001", TransportBoxState.Received);
        var box2 = CreateBox(2, "B002", TransportBoxState.Received);
        var box3 = CreateBox(3, "B003", TransportBoxState.Received);

        _transportBoxRepository.Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { box1, box2, box3 });

        // Box 1: All completed
        var ops1 = new List<StockUpOperation>
        {
            CreateOperation(1, "BOX-000001-PROD1", StockUpOperationState.Completed)
        };
        _stockUpOperationRepository.Setup(x => x.GetBySourceAsync(StockUpSourceType.TransportBox, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ops1);

        // Box 2: Failed
        var ops2 = new List<StockUpOperation>
        {
            CreateOperation(2, "BOX-000002-PROD1", StockUpOperationState.Failed)
        };
        _stockUpOperationRepository.Setup(x => x.GetBySourceAsync(StockUpSourceType.TransportBox, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ops2);

        // Box 3: Still pending
        var ops3 = new List<StockUpOperation>
        {
            CreateOperation(3, "BOX-000003-PROD1", StockUpOperationState.Pending)
        };
        _stockUpOperationRepository.Setup(x => x.GetBySourceAsync(StockUpSourceType.TransportBox, 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ops3);

        // Act
        await _job.ExecuteAsync(CancellationToken.None);

        // Assert
        box1.State.Should().Be(TransportBoxState.Stocked);
        box2.State.Should().Be(TransportBoxState.Error);
        box3.State.Should().Be(TransportBoxState.Received);
    }

    [Fact]
    public async Task ExecuteAsync_JobDisabled_SkipsExecution()
    {
        // Arrange
        _statusChecker.Setup(x => x.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var box = CreateBox(1, "B001", TransportBoxState.Received);
        _transportBoxRepository.Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { box });

        // Act
        await _job.ExecuteAsync(CancellationToken.None);

        // Assert
        _transportBoxRepository.Verify(
            x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Helper methods
    private TransportBox CreateBox(int id, string code, TransportBoxState state)
    {
        var box = new TransportBox();

        // Set Id using reflection
        typeof(TransportBox).GetProperty("Id")!.SetValue(box, id);

        // Set Code using reflection
        var codeField = typeof(TransportBox).GetField("<Code>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        codeField?.SetValue(box, code);

        // Set State using reflection
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
