using Anela.Heblo.Application.Features.Catalog.Inventory.Printing;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.ReprintMaterialContainerLabel;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class ReprintMaterialContainerLabelHandlerTests
{
    private const string Code = "M00000010";

    private readonly Mock<IMaterialContainerRepository> _repo = new();
    private readonly Mock<ILabelPrintingService> _label = new();
    private readonly Mock<IPrinterMediaStateRepository> _media = new();
    private readonly Mock<ICurrentUserService> _user = new();

    public ReprintMaterialContainerLabelHandlerTests()
    {
        _user.Setup(u => u.GetCurrentUser())
            .Returns(new CurrentUser(Id: "1", Name: "admin", Email: "admin@example.com", IsAuthenticated: true));
        _media.Setup(m => m.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(PrinterMediaState.CreateDefault());
        _label.Setup(l => l.PrintZplAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    private ReprintMaterialContainerLabelHandler CreateSut() => new(
        NullLogger<ReprintMaterialContainerLabelHandler>.Instance,
        _repo.Object, _label.Object, _user.Object, _media.Object);

    private void GivenContainer(MaterialContainer? container) =>
        _repo.Setup(r => r.GetByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(container);

    private static MaterialContainer Assigned()
    {
        var c = MaterialContainer.CreateUnassigned(Code, "admin");
        c.Assign("MAT1", "LOT1", 1m, "kg", null, "admin");
        return c;
    }

    private void GivenMediaRequiresChange()
    {
        var state = PrinterMediaState.CreateDefault();
        state.RecordPrint(LabelMediaType.LotRound, "x");
        _media.Setup(m => m.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(state);
    }

    private void VerifyNotPrinted() =>
        _label.Verify(l => l.PrintZplAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

    [Fact]
    public async Task Handle_AssignedContainer_PrintsZplForThatCode_AndRecordsMedia()
    {
        GivenContainer(Assigned());

        var result = await CreateSut().Handle(new ReprintMaterialContainerLabelRequest { Code = Code }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Container!.Code.Should().Be(Code);
        _label.Verify(l => l.PrintZplAsync(
            MaterialContainerLabelZplBuilder.Build(new[] { Code }), It.IsAny<CancellationToken>()), Times.Once);
        _media.Verify(m => m.SaveAsync(
            It.Is<PrinterMediaState>(s => s.LastMediaType == LabelMediaType.MaterialContainer),
            It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<MaterialContainer>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UnassignedContainer_Prints()
    {
        GivenContainer(MaterialContainer.CreateUnassigned(Code, "admin"));

        var result = await CreateSut().Handle(new ReprintMaterialContainerLabelRequest { Code = Code }, CancellationToken.None);

        result.Success.Should().BeTrue();
        _label.Verify(l => l.PrintZplAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CodeWithWhitespace_IsTrimmedBeforeLookup()
    {
        GivenContainer(Assigned());

        await CreateSut().Handle(new ReprintMaterialContainerLabelRequest { Code = " M00000010 " }, CancellationToken.None);

        _repo.Verify(r => r.GetByCodeAsync(Code, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UnknownCode_ReturnsUnknownMaterialContainerCode_AndDoesNotPrint()
    {
        GivenContainer(null);

        var result = await CreateSut().Handle(new ReprintMaterialContainerLabelRequest { Code = Code }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.UnknownMaterialContainerCode);
        result.Params.Should().ContainKey("Code");
        VerifyNotPrinted();
    }

    [Fact]
    public async Task Handle_DiscardedContainer_Prints()
    {
        var container = Assigned();
        container.Discard("admin");
        GivenContainer(container);

        var result = await CreateSut().Handle(new ReprintMaterialContainerLabelRequest { Code = Code }, CancellationToken.None);

        result.Success.Should().BeTrue();
        _label.Verify(l => l.PrintZplAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_MediaChangeRequired_NotConfirmed_ReturnsFlag_AndDoesNotPrint()
    {
        GivenContainer(Assigned());
        GivenMediaRequiresChange();

        var result = await CreateSut().Handle(new ReprintMaterialContainerLabelRequest { Code = Code }, CancellationToken.None);

        result.RequiresMediaChangeConfirmation.Should().BeTrue();
        VerifyNotPrinted();
        _media.Verify(m => m.SaveAsync(It.IsAny<PrinterMediaState>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_MediaChangeRequired_Confirmed_Prints()
    {
        GivenContainer(Assigned());
        GivenMediaRequiresChange();

        var result = await CreateSut().Handle(
            new ReprintMaterialContainerLabelRequest { Code = Code, MediaChangeConfirmed = true }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.RequiresMediaChangeConfirmation.Should().BeFalse();
        _label.Verify(l => l.PrintZplAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_PrinterThrows_DoesNotRecordMedia()
    {
        GivenContainer(Assigned());
        _label.Setup(l => l.PrintZplAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("printer offline"));

        var act = () => CreateSut().Handle(new ReprintMaterialContainerLabelRequest { Code = Code }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _media.Verify(m => m.SaveAsync(It.IsAny<PrinterMediaState>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
