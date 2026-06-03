using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.Contracts.Models;
using Anela.Heblo.Application.Features.Logistics.Services;
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
    private readonly Mock<ILogisticsStockOperationQueryService> _stockOperationQueryServiceMock;
    private readonly TransportBoxCompletionService _service;

    public TransportBoxCompletionServiceTests()
    {
        _loggerMock = new Mock<ILogger<TransportBoxCompletionService>>();
        _transportBoxRepositoryMock = new Mock<ITransportBoxRepository>();
        _stockOperationQueryServiceMock = new Mock<ILogisticsStockOperationQueryService>();
        _service = new TransportBoxCompletionService(
            _loggerMock.Object,
            _transportBoxRepositoryMock.Object,
            _stockOperationQueryServiceMock.Object);
    }

    [Fact]
    public async Task CompleteReceivedBoxesAsync_NoReceivedBoxes_DoesNothing()
    {
        _transportBoxRepositoryMock
            .Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox>());

        await _service.CompleteReceivedBoxesAsync(CancellationToken.None);

        _transportBoxRepositoryMock.Verify(
            x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CompleteReceivedBoxesAsync_AllOperationsCompleted_TransitionsBoxToStocked()
    {
        var box = CreateBox(1, "BOX-001", TransportBoxState.Received);
        _transportBoxRepositoryMock
            .Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { box });

        var operations = new List<LogisticsStockOperationStatus>
        {
            CreateStatus("BOX-000001-PROD1", LogisticsStockOperationState.Completed),
            CreateStatus("BOX-000001-PROD2", LogisticsStockOperationState.Completed),
        };
        SetupQueryReturns(box.Id, operations);

        _transportBoxRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _transportBoxRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await _service.CompleteReceivedBoxesAsync(CancellationToken.None);

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
        var box = CreateBox(1, "BOX-001", TransportBoxState.Received);
        _transportBoxRepositoryMock
            .Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { box });

        var operations = new List<LogisticsStockOperationStatus>
        {
            CreateStatus("BOX-000001-PROD1", LogisticsStockOperationState.Completed),
            CreateStatus("BOX-000001-PROD2", LogisticsStockOperationState.Failed),
        };
        SetupQueryReturns(box.Id, operations);

        _transportBoxRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _transportBoxRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await _service.CompleteReceivedBoxesAsync(CancellationToken.None);

        box.State.Should().Be(TransportBoxState.Error);
    }

    [Fact]
    public async Task CompleteReceivedBoxesAsync_OperationsPending_LeavesBoxInReceived()
    {
        var box = CreateBox(1, "BOX-001", TransportBoxState.Received);
        _transportBoxRepositoryMock
            .Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { box });

        var operations = new List<LogisticsStockOperationStatus>
        {
            CreateStatus("BOX-000001-PROD1", LogisticsStockOperationState.Completed),
            CreateStatus("BOX-000001-PROD2", LogisticsStockOperationState.Pending),
        };
        SetupQueryReturns(box.Id, operations);

        await _service.CompleteReceivedBoxesAsync(CancellationToken.None);

        box.State.Should().Be(TransportBoxState.Received);
        _transportBoxRepositoryMock.Verify(
            x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CompleteReceivedBoxesAsync_NoOperationsForBox_TransitionsToError()
    {
        var box = CreateBox(1, "BOX-001", TransportBoxState.Received);
        _transportBoxRepositoryMock
            .Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { box });

        SetupQueryReturns(box.Id, new List<LogisticsStockOperationStatus>());

        _transportBoxRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _transportBoxRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await _service.CompleteReceivedBoxesAsync(CancellationToken.None);

        box.State.Should().Be(TransportBoxState.Error);
    }

    [Fact]
    public async Task CompleteReceivedBoxesAsync_MultipleBoxes_ProcessesAll()
    {
        var box1 = CreateBox(1, "BOX-001", TransportBoxState.Received);
        var box2 = CreateBox(2, "BOX-002", TransportBoxState.Received);
        var box3 = CreateBox(3, "BOX-003", TransportBoxState.Received);

        _transportBoxRepositoryMock
            .Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { box1, box2, box3 });

        SetupQueryReturns(box1.Id, new List<LogisticsStockOperationStatus>
        {
            CreateStatus("BOX-000001-PROD1", LogisticsStockOperationState.Completed),
        });
        SetupQueryReturns(box2.Id, new List<LogisticsStockOperationStatus>
        {
            CreateStatus("BOX-000002-PROD1", LogisticsStockOperationState.Failed),
        });
        SetupQueryReturns(box3.Id, new List<LogisticsStockOperationStatus>
        {
            CreateStatus("BOX-000003-PROD1", LogisticsStockOperationState.Pending),
        });

        _transportBoxRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _transportBoxRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await _service.CompleteReceivedBoxesAsync(CancellationToken.None);

        box1.State.Should().Be(TransportBoxState.Stocked);
        box2.State.Should().Be(TransportBoxState.Error);
        box3.State.Should().Be(TransportBoxState.Received);

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
        var box = CreateBox(1, "BOX-001", TransportBoxState.Received);
        _transportBoxRepositoryMock
            .Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { box });

        var operations = new List<LogisticsStockOperationStatus>
        {
            CreateStatus("BOX-000001-PROD1", LogisticsStockOperationState.Completed),
            CreateStatus("BOX-000001-PROD2", LogisticsStockOperationState.Submitted),
        };
        SetupQueryReturns(box.Id, operations);

        await _service.CompleteReceivedBoxesAsync(CancellationToken.None);

        box.State.Should().Be(TransportBoxState.Received);
        _transportBoxRepositoryMock.Verify(
            x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private void SetupQueryReturns(int sourceId, IReadOnlyList<LogisticsStockOperationStatus> operations)
    {
        _stockOperationQueryServiceMock
            .Setup(x => x.GetOperationsBySourceAsync(
                LogisticsStockOperationSource.TransportBox,
                sourceId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(operations);
    }

    private static TransportBox CreateBox(int id, string code, TransportBoxState state)
    {
        var box = new TransportBox();
        typeof(TransportBox).GetProperty("Id")!.SetValue(box, id);
        typeof(TransportBox).GetProperty("Code")!.SetValue(box, code);
        typeof(TransportBox).GetProperty("State")!.SetValue(box, state);
        return box;
    }

    private static LogisticsStockOperationStatus CreateStatus(string documentNumber, LogisticsStockOperationState state)
        => new()
        {
            DocumentNumber = documentNumber,
            State = state,
        };
}
