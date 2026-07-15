using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.PrintMaterialContainerLabels;
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class PrintMaterialContainerLabelsHandlerTests
{
    private static Mock<ICurrentUserService> UserMock()
    {
        var user = new Mock<ICurrentUserService>();
        user.Setup(u => u.GetCurrentUser())
            .Returns(new CurrentUser(Id: "1", Name: "admin", Email: "admin@example.com", IsAuthenticated: true));
        return user;
    }

    [Fact]
    public async Task Handle_GeneratesCodes_PersistsUnassigned_AndPrintsZpl()
    {
        var generator = new Mock<IMaterialContainerCodeGenerator>();
        generator.Setup(g => g.GenerateAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "M00000010", "M00000011" });

        var repo = new Mock<IMaterialContainerRepository>();
        var label = new Mock<ILabelPrintingService>();

        var media = new Mock<IPrinterMediaStateRepository>();
        media.Setup(m => m.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PrinterMediaState.CreateDefault());

        var calls = new List<string>();
        repo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<MaterialContainer>>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("add"))
            .ReturnsAsync((IEnumerable<MaterialContainer> cs, CancellationToken _) => cs);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("save"))
            .ReturnsAsync(1);
        label.Setup(l => l.PrintZplAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("print"))
            .Returns(Task.CompletedTask);

        var sut = new PrintMaterialContainerLabelsHandler(
            NullLogger<PrintMaterialContainerLabelsHandler>.Instance,
            generator.Object, repo.Object, label.Object, UserMock().Object, media.Object);

        var result = await sut.Handle(new PrintMaterialContainerLabelsRequest { Count = 2 }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.RequiresMediaChangeConfirmation.Should().BeFalse();
        result.Containers.Should().HaveCount(2);
        result.Containers.Should().OnlyContain(c => c.Status == "Unassigned");
        generator.Verify(g => g.GenerateAsync(2, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.AddRangeAsync(
            It.Is<IEnumerable<MaterialContainer>>(cs => cs.Count() == 2), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        label.Verify(l => l.PrintZplAsync(It.Is<string>(z => z.Contains("M00000010") && z.Contains("M00000011")),
            It.IsAny<CancellationToken>()), Times.Once);
        calls.Should().ContainInOrder("save", "print");
        media.Verify(m => m.SaveAsync(
            It.Is<PrinterMediaState>(s => s.LastMediaType == LabelMediaType.MaterialContainer),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_RequiresConfirmation_WhenSwitchingMedia_AndNotConfirmed_CreatesNothing()
    {
        var generator = new Mock<IMaterialContainerCodeGenerator>();
        var repo = new Mock<IMaterialContainerRepository>();
        var label = new Mock<ILabelPrintingService>();

        var previous = PrinterMediaState.CreateDefault();
        previous.RecordPrint(LabelMediaType.LotRound, "admin");
        var media = new Mock<IPrinterMediaStateRepository>();
        media.Setup(m => m.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(previous);

        var sut = new PrintMaterialContainerLabelsHandler(
            NullLogger<PrintMaterialContainerLabelsHandler>.Instance,
            generator.Object, repo.Object, label.Object, UserMock().Object, media.Object);

        var result = await sut.Handle(new PrintMaterialContainerLabelsRequest { Count = 2 }, CancellationToken.None);

        result.RequiresMediaChangeConfirmation.Should().BeTrue();
        result.Containers.Should().BeEmpty();
        generator.Verify(g => g.GenerateAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<MaterialContainer>>(), It.IsAny<CancellationToken>()), Times.Never);
        label.Verify(l => l.PrintZplAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        media.Verify(m => m.SaveAsync(It.IsAny<PrinterMediaState>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PrintsAndRecords_WhenSwitchingMedia_AndConfirmed()
    {
        var generator = new Mock<IMaterialContainerCodeGenerator>();
        generator.Setup(g => g.GenerateAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "M00000010" });

        var repo = new Mock<IMaterialContainerRepository>();
        repo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<MaterialContainer>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<MaterialContainer> cs, CancellationToken _) => cs);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var label = new Mock<ILabelPrintingService>();
        label.Setup(l => l.PrintZplAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var previous = PrinterMediaState.CreateDefault();
        previous.RecordPrint(LabelMediaType.LotRound, "admin");
        var media = new Mock<IPrinterMediaStateRepository>();
        media.Setup(m => m.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(previous);

        var sut = new PrintMaterialContainerLabelsHandler(
            NullLogger<PrintMaterialContainerLabelsHandler>.Instance,
            generator.Object, repo.Object, label.Object, UserMock().Object, media.Object);

        var result = await sut.Handle(
            new PrintMaterialContainerLabelsRequest { Count = 1, MediaChangeConfirmed = true }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.RequiresMediaChangeConfirmation.Should().BeFalse();
        result.Containers.Should().HaveCount(1);
        label.Verify(l => l.PrintZplAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        media.Verify(m => m.SaveAsync(
            It.Is<PrinterMediaState>(s => s.LastMediaType == LabelMediaType.MaterialContainer),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
