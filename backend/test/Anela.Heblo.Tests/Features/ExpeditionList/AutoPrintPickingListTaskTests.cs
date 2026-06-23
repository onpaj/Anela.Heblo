using Anela.Heblo.Application.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
using Anela.Heblo.Application.Features.ExpeditionList.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.ExpeditionList;

public class AutoPrintPickingListTaskTests
{
    private static PrintPickingListOptions Options() => new()
    {
        AutoPrintSourceStateId = 85,
        DesiredStateId = 26,
        ChangeOrderStateByDefault = true,
        SendToPrinterByDefault = true,
    };

    [Fact]
    public async Task ExecuteOnceAsync_PrintsWithTiskRobotSourceState()
    {
        // Arrange
        var service = new Mock<IExpeditionListService>();
        ExpeditionPickingRequest? captured = null;
        IList<string>? capturedEmail = null;
        service
            .Setup(s => s.PrintPickingListAsync(
                It.IsAny<ExpeditionPickingRequest>(),
                It.IsAny<IList<string>?>(),
                It.IsAny<CancellationToken>()))
            .Callback<ExpeditionPickingRequest, IList<string>?, CancellationToken>((req, email, _) =>
            {
                captured = req;
                capturedEmail = email;
            })
            .ReturnsAsync(new ExpeditionPickingResult { TotalCount = 7 });

        // Act
        var totalCount = await AutoPrintPickingListTask.ExecuteOnceAsync(
            service.Object, Options(), CancellationToken.None);

        // Assert
        Assert.Equal(7, totalCount);
        service.Verify(s => s.PrintPickingListAsync(
            It.IsAny<ExpeditionPickingRequest>(),
            It.IsAny<IList<string>?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        Assert.NotNull(captured);
        Assert.Equal(85, captured!.SourceStateId);
        Assert.Equal(26, captured.DesiredStateId);
        Assert.True(captured.ChangeOrderState);
        Assert.True(captured.SendToPrinter);
        Assert.Equal(ExpeditionPickingRequest.DefaultCarriers, captured.Carriers);
        Assert.Null(capturedEmail); // printer-only, no email copies
    }
}
