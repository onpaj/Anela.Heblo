using Anela.Heblo.Application.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.Domain.Features.Logistics.Picking;
using Anela.Heblo.Xcc.Services.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.ExpeditionList;

public class ExpeditionListServiceOrderStateTests
{
    private readonly Mock<IPickingListSource> _pickingListSource = new();
    private readonly Mock<IEmailSender> _emailSender = new();
    private readonly Mock<IPrintQueueSink> _printQueueSink = new();

    private static readonly List<int> DefaultOrderIds = new() { 1001, 1002 };
    private static readonly List<string> DefaultFiles = new();

    private ExpeditionListService CreateService() => new ExpeditionListService(
        _pickingListSource.Object,
        _emailSender.Object,
        TimeProvider.System,
        Options.Create(new PrintPickingListOptions { EmailSender = "test@test.com" }),
        _printQueueSink.Object,
        NullLogger<ExpeditionListService>.Instance);

    private void SetupPickingListSource()
    {
        _pickingListSource
            .Setup(x => x.CreatePickingList(It.IsAny<PrintPickingListRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PrintPickingListResult
            {
                ExportedFiles = DefaultFiles,
                TotalCount = 2,
                OrderIds = DefaultOrderIds,
            });
    }

    [Fact]
    public async Task PrintPickingListAsync_WhenEmailThrows_StateChangeIsNotCalled()
    {
        // Arrange
        SetupPickingListSource();
        _emailSender
            .Setup(x => x.SendEmailAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("SMTP failure"));

        var request = new PrintPickingListRequest
        {
            ChangeOrderState = true,
            SendToPrinter = false,
        };
        var svc = CreateService();

        // Act
        await Assert.ThrowsAsync<Exception>(() =>
            svc.PrintPickingListAsync(request, emailList: new[] { "user@example.com" }));

        // Assert
        _pickingListSource.Verify(
            x => x.ChangeOrderState(It.IsAny<IList<int>>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PrintPickingListAsync_WhenPrintQueueThrows_StateChangeIsNotCalled()
    {
        // Arrange
        SetupPickingListSource();
        _printQueueSink
            .Setup(x => x.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Print queue failure"));

        var request = new PrintPickingListRequest
        {
            ChangeOrderState = true,
            SendToPrinter = true,
        };
        var svc = CreateService();

        // Act
        await Assert.ThrowsAsync<Exception>(() => svc.PrintPickingListAsync(request));

        // Assert
        _pickingListSource.Verify(
            x => x.ChangeOrderState(It.IsAny<IList<int>>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PrintPickingListAsync_WhenAllSucceed_StateChangeIsCalledLast()
    {
        // Arrange
        SetupPickingListSource();
        var callOrder = new List<string>();

        _emailSender
            .Setup(x => x.SendEmailAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("email"))
            .Returns(Task.CompletedTask);

        _printQueueSink
            .Setup(x => x.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("print"))
            .Returns(Task.CompletedTask);

        _pickingListSource
            .Setup(x => x.ChangeOrderState(It.IsAny<IList<int>>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("stateChange"))
            .Returns(Task.CompletedTask);

        var request = new PrintPickingListRequest
        {
            ChangeOrderState = true,
            SendToPrinter = true,
        };
        var svc = CreateService();

        // Act
        await svc.PrintPickingListAsync(request, emailList: new[] { "user@example.com" });

        // Assert: state change called after email and print
        Assert.Equal(new[] { "email", "print", "stateChange" }, callOrder);
        _pickingListSource.Verify(
            x => x.ChangeOrderState(DefaultOrderIds, request.SourceStateId, request.DesiredStateId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PrintPickingListAsync_WhenChangeOrderStateFalse_StateChangeIsNotCalled()
    {
        // Arrange
        SetupPickingListSource();

        var request = new PrintPickingListRequest { ChangeOrderState = false };
        var svc = CreateService();

        // Act
        await svc.PrintPickingListAsync(request);

        // Assert
        _pickingListSource.Verify(
            x => x.ChangeOrderState(It.IsAny<IList<int>>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PrintPickingListAsync_WhenStateChangeFails_CleanupStillRuns()
    {
        // Arrange
        SetupPickingListSource();

        // Write a real temp file so cleanup can actually delete it
        var tmpFile = Path.GetTempFileName();
        _pickingListSource
            .Setup(x => x.CreatePickingList(It.IsAny<PrintPickingListRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PrintPickingListResult
            {
                ExportedFiles = new[] { tmpFile },
                TotalCount = 1,
                OrderIds = DefaultOrderIds,
            });

        _pickingListSource
            .Setup(x => x.ChangeOrderState(It.IsAny<IList<int>>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("State change failure"));

        var request = new PrintPickingListRequest { ChangeOrderState = true };
        var svc = CreateService();

        // Act
        await Assert.ThrowsAsync<Exception>(() => svc.PrintPickingListAsync(request));

        // Assert: temp file was deleted despite state change failure
        Assert.False(File.Exists(tmpFile));
    }
}
