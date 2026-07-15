using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.FeedLotMedia;
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class FeedLotMediaHandlerTests
{
    private static Mock<ICurrentUserService> UserMock()
    {
        var user = new Mock<ICurrentUserService>();
        user.Setup(u => u.GetCurrentUser())
            .Returns(new CurrentUser(Id: "1", Name: "admin", Email: "admin@example.com", IsAuthenticated: true));
        return user;
    }

    [Fact]
    public async Task Handle_PrintsBlankLabel_OfRequestedLength()
    {
        var label = new Mock<ILabelPrintingService>();
        label.Setup(l => l.PrintZplAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var media = new Mock<IPrinterMediaStateRepository>();
        media.Setup(m => m.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PrinterMediaState.CreateDefault());

        var sut = new FeedLotMediaHandler(
            NullLogger<FeedLotMediaHandler>.Instance, label.Object, media.Object, UserMock().Object);

        var result = await sut.Handle(new FeedLotMediaRequest { Dots = 40 }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.RequiresMediaChangeConfirmation.Should().BeFalse();
        label.Verify(l => l.PrintZplAsync(
            It.Is<string>(z => z.Contains("^LL40") && !z.Contains("^FD")),
            It.IsAny<CancellationToken>()), Times.Once);
        media.Verify(m => m.SaveAsync(
            It.Is<PrinterMediaState>(s => s.LastMediaType == LabelMediaType.LotRound),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_RequiresConfirmation_WhenSwitchingMedia_AndNotConfirmed_DoesNotFeed()
    {
        var label = new Mock<ILabelPrintingService>();

        var previous = PrinterMediaState.CreateDefault();
        previous.RecordPrint(LabelMediaType.MaterialContainer, "admin");
        var media = new Mock<IPrinterMediaStateRepository>();
        media.Setup(m => m.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(previous);

        var sut = new FeedLotMediaHandler(
            NullLogger<FeedLotMediaHandler>.Instance, label.Object, media.Object, UserMock().Object);

        var result = await sut.Handle(new FeedLotMediaRequest { Dots = 40 }, CancellationToken.None);

        result.RequiresMediaChangeConfirmation.Should().BeTrue();
        label.Verify(l => l.PrintZplAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        media.Verify(m => m.SaveAsync(It.IsAny<PrinterMediaState>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
