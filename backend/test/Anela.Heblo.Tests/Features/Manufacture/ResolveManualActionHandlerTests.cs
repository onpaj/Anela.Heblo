using Anela.Heblo.Application.Features.Manufacture.UseCases.ResolveManualAction;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class ResolveManualActionHandlerTests
{
    private readonly Mock<IManufactureOrderRepository> _repositoryMock = new();
    private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
    private readonly Mock<ILogger<ResolveManualActionHandler>> _loggerMock = new();
    private readonly ResolveManualActionHandler _handler;

    public ResolveManualActionHandlerTests()
    {
        _handler = new ResolveManualActionHandler(
            _repositoryMock.Object,
            _currentUserServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WhenOrderNotFound_ReturnsResourceNotFoundError()
    {
        _repositoryMock
            .Setup(r => r.GetOrderByIdAsync(99, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder?)null);

        var result = await _handler.Handle(
            new ResolveManualActionRequest { OrderId = 99 },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        _repositoryMock.Verify(
            r => r.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenOrderFound_ResetsManualActionRequired()
    {
        var order = BuildOrder(manualActionRequired: true);
        SetupFoundOrder(order);
        SetupCurrentUser("Test User");

        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        order.ManualActionRequired.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenSemiproductNumberProvided_UpdatesField()
    {
        var order = BuildOrder();
        SetupFoundOrder(order);
        SetupCurrentUser("Test User");

        await _handler.Handle(
            BuildRequest(erpOrderNumberSemiproduct: "SEMI-001"),
            CancellationToken.None);

        order.ErpOrderNumberSemiproduct.Should().Be("SEMI-001");
    }

    [Fact]
    public async Task Handle_WhenSemiproductNumberOmitted_DoesNotOverwriteField()
    {
        var order = BuildOrder();
        order.ErpOrderNumberSemiproduct = "EXISTING-SEMI";
        SetupFoundOrder(order);
        SetupCurrentUser("Test User");

        await _handler.Handle(
            BuildRequest(erpOrderNumberSemiproduct: null),
            CancellationToken.None);

        order.ErpOrderNumberSemiproduct.Should().Be("EXISTING-SEMI");
    }

    [Fact]
    public async Task Handle_WhenProductNumberProvided_UpdatesField()
    {
        var order = BuildOrder();
        SetupFoundOrder(order);
        SetupCurrentUser("Test User");

        await _handler.Handle(
            BuildRequest(erpOrderNumberProduct: "PROD-001"),
            CancellationToken.None);

        order.ErpOrderNumberProduct.Should().Be("PROD-001");
    }

    [Fact]
    public async Task Handle_WhenProductNumberOmitted_DoesNotOverwriteField()
    {
        var order = BuildOrder();
        order.ErpOrderNumberProduct = "EXISTING-PROD";
        SetupFoundOrder(order);
        SetupCurrentUser("Test User");

        await _handler.Handle(
            BuildRequest(erpOrderNumberProduct: null),
            CancellationToken.None);

        order.ErpOrderNumberProduct.Should().Be("EXISTING-PROD");
    }

    [Fact]
    public async Task Handle_WhenDiscardDocumentProvided_UpdatesFieldAndTimestamp()
    {
        var order = BuildOrder();
        SetupFoundOrder(order);
        SetupCurrentUser("Test User");
        var before = DateTime.UtcNow;

        await _handler.Handle(
            BuildRequest(erpDiscardResidueDocumentNumber: "DISCARD-001"),
            CancellationToken.None);

        order.ErpDiscardResidueDocumentNumber.Should().Be("DISCARD-001");
        order.ErpDiscardResidueDocumentNumberDate.Should().NotBeNull();
        order.ErpDiscardResidueDocumentNumberDate!.Value
            .Should().BeCloseTo(before, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Handle_WhenDiscardDocumentOmitted_DoesNotSetTimestamp()
    {
        var order = BuildOrder();
        SetupFoundOrder(order);
        SetupCurrentUser("Test User");

        await _handler.Handle(
            BuildRequest(erpDiscardResidueDocumentNumber: null),
            CancellationToken.None);

        order.ErpDiscardResidueDocumentNumberDate.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenNoteProvidedAndUserPresent_AddsNoteWithUserName()
    {
        var order = BuildOrder();
        SetupFoundOrder(order);
        SetupCurrentUser("Ondra Novak");

        await _handler.Handle(
            BuildRequest(note: "Please check the batch."),
            CancellationToken.None);

        order.Notes.Should().HaveCount(1);
        order.Notes[0].Text.Should().Be("Please check the batch.");
        order.Notes[0].CreatedByUser.Should().Be("Ondra Novak");
    }

    [Fact]
    public async Task Handle_WhenNoteProvidedAndUserNull_AddsNoteWithUnknownUser()
    {
        var order = BuildOrder();
        SetupFoundOrder(order);
        _currentUserServiceMock
            .Setup(s => s.GetCurrentUser())
            .Returns((CurrentUser?)null!);

        await _handler.Handle(
            BuildRequest(note: "Fallback note."),
            CancellationToken.None);

        order.Notes.Should().HaveCount(1);
        order.Notes[0].Text.Should().Be("Fallback note.");
        order.Notes[0].CreatedByUser.Should().Be("Unknown User");
    }

    [Fact]
    public async Task Handle_WhenNoteOmitted_DoesNotAddNote()
    {
        var order = BuildOrder();
        SetupFoundOrder(order);
        SetupCurrentUser("Test User");

        await _handler.Handle(
            BuildRequest(note: null),
            CancellationToken.None);

        order.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithAllFieldsProvided_ReturnsSuccessAndUpdatesAllFields()
    {
        var order = BuildOrder(manualActionRequired: true);
        SetupFoundOrder(order);
        SetupCurrentUser("Ondra Novak");

        var result = await _handler.Handle(
            new ResolveManualActionRequest
            {
                OrderId = 1,
                ErpOrderNumberSemiproduct = "SEMI-ALL",
                ErpOrderNumberProduct = "PROD-ALL",
                ErpDiscardResidueDocumentNumber = "DISCARD-ALL",
                Note = "All fields set."
            },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        order.ManualActionRequired.Should().BeFalse();
        order.ErpOrderNumberSemiproduct.Should().Be("SEMI-ALL");
        order.ErpOrderNumberProduct.Should().Be("PROD-ALL");
        order.ErpDiscardResidueDocumentNumber.Should().Be("DISCARD-ALL");
        order.ErpDiscardResidueDocumentNumberDate.Should().NotBeNull();
        order.Notes.Should().HaveCount(1);
        order.Notes[0].CreatedByUser.Should().Be("Ondra Novak");
        _repositoryMock.Verify(
            r => r.UpdateOrderAsync(order, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static ManufactureOrder BuildOrder(bool manualActionRequired = false) =>
        new()
        {
            Id = 1,
            OrderNumber = "MO-2026-001",
            StateChangedByUser = "system",
            ManualActionRequired = manualActionRequired
        };

    private static ResolveManualActionRequest BuildRequest(
        string? erpOrderNumberSemiproduct = null,
        string? erpOrderNumberProduct = null,
        string? erpDiscardResidueDocumentNumber = null,
        string? note = null) =>
        new()
        {
            OrderId = 1,
            ErpOrderNumberSemiproduct = erpOrderNumberSemiproduct,
            ErpOrderNumberProduct = erpOrderNumberProduct,
            ErpDiscardResidueDocumentNumber = erpDiscardResidueDocumentNumber,
            Note = note
        };

    private void SetupFoundOrder(ManufactureOrder order)
    {
        _repositoryMock
            .Setup(r => r.GetOrderByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _repositoryMock
            .Setup(r => r.UpdateOrderAsync(order, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
    }

    private void SetupCurrentUser(string name)
    {
        _currentUserServiceMock
            .Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(Id: "u1", Name: name, Email: "test@anela.cz", IsAuthenticated: true));
    }
}
