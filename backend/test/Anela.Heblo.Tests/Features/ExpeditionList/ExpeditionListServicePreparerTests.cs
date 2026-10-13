using Anela.Heblo.Application.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Xcc.Services.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.ExpeditionList;

public class ExpeditionListServicePreparerTests
{
    private readonly Mock<IExpeditionPickingSource> _pickingSource = new();
    private readonly Mock<IPickingListOrderPreparer> _preparer = new();

    private ExpeditionListService CreateService() => new(
        _pickingSource.Object,
        _preparer.Object,
        new Mock<IEmailSender>().Object,
        TimeProvider.System,
        Options.Create(new PrintPickingListOptions { EmailSender = "test@test.com" }),
        new Mock<IPrintQueueSink>().Object,
        new Mock<ITemporaryFileAccessor>().Object,
        NullLogger<ExpeditionListService>.Instance);

    private readonly List<string> _calls = new();

    private void TrackSource() =>
        _pickingSource
            .Setup(x => x.CreatePickingListAsync(It.IsAny<ExpeditionPickingRequest>(), It.IsAny<Func<IList<string>, Task>>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("build"))
            .ReturnsAsync(new ExpeditionPickingResult { ExportedFiles = new List<string>(), TotalCount = 0 });

    [Fact]
    public async Task PrintPickingListAsync_PreparesOrdersBeforeBuildingList()
    {
        TrackSource();
        _preparer.Setup(p => p.PrepareAsync(It.IsAny<CancellationToken>())).Callback(() => _calls.Add("prepare")).Returns(Task.CompletedTask);

        await CreateService().PrintPickingListAsync(new ExpeditionPickingRequest { SendToPrinter = false });

        Assert.Equal(new[] { "prepare", "build" }, _calls);
    }

    [Fact]
    public async Task PrintPickingListAsync_StillPrints_WhenPreparerThrows()
    {
        TrackSource();
        _preparer.Setup(p => p.PrepareAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("Shoptet down"));

        await CreateService().PrintPickingListAsync(new ExpeditionPickingRequest { SendToPrinter = false });

        Assert.Equal(new[] { "build" }, _calls);
    }
}
