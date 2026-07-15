using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.FeedLotMedia;
using Anela.Heblo.Application.Shared.Printing;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class FeedLotMediaHandlerTests
{
    [Fact]
    public async Task Handle_PrintsBlankLabel_OfRequestedLength()
    {
        var label = new Mock<ILabelPrintingService>();
        label.Setup(l => l.PrintZplAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new FeedLotMediaHandler(
            NullLogger<FeedLotMediaHandler>.Instance, label.Object);

        var result = await sut.Handle(new FeedLotMediaRequest { Dots = 40 }, CancellationToken.None);

        result.Success.Should().BeTrue();
        label.Verify(l => l.PrintZplAsync(
            It.Is<string>(z => z.Contains("^LL40") && !z.Contains("^FD")),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
