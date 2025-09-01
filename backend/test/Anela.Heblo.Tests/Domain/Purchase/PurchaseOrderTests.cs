using Anela.Heblo.Domain.Features.Purchase;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Domain.Purchase;

public class PurchaseOrderTests
{
    private const string ValidOrderNumber = "PO-2024-001";
    private const string ValidSupplierName = "Test Supplier";
    private static readonly DateTime ValidOrderDate = DateTime.UtcNow.Date;
    private static readonly DateTime? ValidExpectedDeliveryDate = DateTime.UtcNow.Date.AddDays(14);
    private const string ValidNotes = "Test purchase order notes";
    private const string ValidCreatedBy = "test@example.com";
    private const string ValidMaterialId = "MAT001";
    private const string ValidCode = "CODE001";
    private const string ValidName = "Test Material Name";

    [Fact]
    public void Constructor_WithValidParameters_ShouldCreatePurchaseOrder()
    {
        var purchaseOrder = new PurchaseOrder(
            ValidOrderNumber,
            ValidSupplierName,
            ValidOrderDate,
            ValidExpectedDeliveryDate,
            null,
            ValidNotes,
            ValidCreatedBy);

        purchaseOrder.Id.Should().Be(0); // For new entities, EF will set this to 0 until saved
        purchaseOrder.OrderNumber.Should().Be(ValidOrderNumber);
        purchaseOrder.SupplierName.Should().Be(ValidSupplierName);
        purchaseOrder.OrderDate.Should().Be(ValidOrderDate);
        purchaseOrder.ExpectedDeliveryDate.Should().Be(ValidExpectedDeliveryDate);
        purchaseOrder.Status.Should().Be(PurchaseOrderStatus.Draft);
        purchaseOrder.Notes.Should().Be(ValidNotes);
        purchaseOrder.CreatedBy.Should().Be(ValidCreatedBy);
        purchaseOrder.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        purchaseOrder.UpdatedBy.Should().BeNull();
        purchaseOrder.UpdatedAt.Should().BeNull();
        purchaseOrder.Lines.Should().BeEmpty();
        purchaseOrder.History.Should().HaveCount(1);
        purchaseOrder.TotalAmount.Should().Be(0);
    }

    [Fact]
    public void Constructor_WithNullOrderNumber_ShouldThrowArgumentNullException()
    {
        var action = () => new PurchaseOrder(
            null!,
            ValidSupplierName,
            ValidOrderDate,
            ValidExpectedDeliveryDate,
            null,
            ValidNotes,
            ValidCreatedBy);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("orderNumber");
    }

    [Fact]
    public void Constructor_WithNullCreatedBy_ShouldThrowArgumentNullException()
    {
        var action = () => new PurchaseOrder(
            ValidOrderNumber,
            ValidSupplierName,
            ValidOrderDate,
            ValidExpectedDeliveryDate,
            null,
            ValidNotes,
            null!);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("createdBy");
    }

    [Fact]
    public void AddLine_ToDraftOrder_ShouldAddLineAndUpdateTotals()
    {
        var purchaseOrder = CreateValidPurchaseOrder();
        const decimal quantity = 10;
        const decimal unitPrice = 25.50m;
        const string notes = "Test line item";

        purchaseOrder.AddLine(ValidMaterialId, ValidName, quantity, unitPrice, notes);

        purchaseOrder.Lines.Should().HaveCount(1);
        var line = purchaseOrder.Lines.First();
        line.MaterialId.Should().Be(ValidMaterialId);
        line.Quantity.Should().Be(quantity);
        line.UnitPrice.Should().Be(unitPrice);
        line.Notes.Should().Be(notes);
        line.LineTotal.Should().Be(255.00m);
        purchaseOrder.TotalAmount.Should().Be(255.00m);
        purchaseOrder.UpdatedBy.Should().Be(ValidCreatedBy);
        purchaseOrder.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void AddLine_ToInTransitOrder_ShouldAddLineSuccessfully()
    {
        var purchaseOrder = CreateValidPurchaseOrder();
        purchaseOrder.ChangeStatus(PurchaseOrderStatus.InTransit, ValidCreatedBy);
        const decimal quantity = 10;
        const decimal unitPrice = 25.50m;
        const string notes = "Test line item";

        purchaseOrder.AddLine(ValidMaterialId, ValidName, quantity, unitPrice, notes);

        purchaseOrder.Lines.Should().HaveCount(1);
        var line = purchaseOrder.Lines.First();
        line.MaterialId.Should().Be(ValidMaterialId);
        line.Quantity.Should().Be(quantity);
        line.UnitPrice.Should().Be(unitPrice);
        line.Notes.Should().Be(notes);
        line.LineTotal.Should().Be(255.00m);
        purchaseOrder.TotalAmount.Should().Be(255.00m);
    }

    [Fact]
    public void AddLine_ToCompletedOrder_ShouldThrowInvalidOperationException()
    {
        var purchaseOrder = CreateValidPurchaseOrder();
        purchaseOrder.ChangeStatus(PurchaseOrderStatus.InTransit, ValidCreatedBy);
        purchaseOrder.ChangeStatus(PurchaseOrderStatus.Completed, ValidCreatedBy);

        var action = () => purchaseOrder.AddLine(ValidMaterialId, ValidName, 10, 25.50m, "notes");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Cannot add lines to completed orders");
    }

    [Fact]
    public void RemoveLine_FromDraftOrder_ShouldRemoveLineAndUpdateTotals()
    {
        var purchaseOrder = CreateValidPurchaseOrder();
        purchaseOrder.AddLine(ValidMaterialId, ValidName, 10, 25.50m, "notes");
        var lineId = purchaseOrder.Lines.First().Id;

        purchaseOrder.RemoveLine(lineId);

        purchaseOrder.Lines.Should().BeEmpty();
        purchaseOrder.TotalAmount.Should().Be(0);
    }

    [Fact]
    public void RemoveLine_FromInTransitOrder_ShouldRemoveLineSuccessfully()
    {
        var purchaseOrder = CreateValidPurchaseOrder();
        purchaseOrder.AddLine(ValidMaterialId, ValidName, 10, 25.50m, "notes");
        var lineId = purchaseOrder.Lines.First().Id;
        purchaseOrder.ChangeStatus(PurchaseOrderStatus.InTransit, ValidCreatedBy);

        purchaseOrder.RemoveLine(lineId);

        purchaseOrder.Lines.Should().BeEmpty();
        purchaseOrder.TotalAmount.Should().Be(0);
    }

    [Fact]
    public void RemoveLine_FromCompletedOrder_ShouldThrowInvalidOperationException()
    {
        var purchaseOrder = CreateValidPurchaseOrder();
        purchaseOrder.AddLine(ValidMaterialId, ValidName, 10, 25.50m, "notes");
        var lineId = purchaseOrder.Lines.First().Id;
        purchaseOrder.ChangeStatus(PurchaseOrderStatus.InTransit, ValidCreatedBy);
        purchaseOrder.ChangeStatus(PurchaseOrderStatus.Completed, ValidCreatedBy);

        var action = () => purchaseOrder.RemoveLine(lineId);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Cannot remove lines from completed orders");
    }

    [Fact]
    public void UpdateLine_InDraftOrder_ShouldUpdateLineAndRecalculateTotals()
    {
        var purchaseOrder = CreateValidPurchaseOrder();
        purchaseOrder.AddLine(ValidMaterialId, ValidName, 10, 25.50m, "old notes");
        var lineId = purchaseOrder.Lines.First().Id;

        purchaseOrder.UpdateLine(lineId, "Updated Material Name", 20, 30.00m, "new notes");

        var line = purchaseOrder.Lines.First();
        line.Quantity.Should().Be(20);
        line.UnitPrice.Should().Be(30.00m);
        line.Notes.Should().Be("new notes");
        line.LineTotal.Should().Be(600.00m);
        purchaseOrder.TotalAmount.Should().Be(600.00m);
    }

    [Fact]
    public void UpdateLine_InInTransitOrder_ShouldUpdateLineSuccessfully()
    {
        var purchaseOrder = CreateValidPurchaseOrder();
        purchaseOrder.AddLine(ValidMaterialId, ValidName, 10, 25.50m, "old notes");
        var lineId = purchaseOrder.Lines.First().Id;
        purchaseOrder.ChangeStatus(PurchaseOrderStatus.InTransit, ValidCreatedBy);

        purchaseOrder.UpdateLine(lineId, "Updated Material Name", 20, 30.00m, "new notes");

        var line = purchaseOrder.Lines.First();
        line.Quantity.Should().Be(20);
        line.UnitPrice.Should().Be(30.00m);
        line.Notes.Should().Be("new notes");
        line.LineTotal.Should().Be(600.00m);
        purchaseOrder.TotalAmount.Should().Be(600.00m);
    }

    [Fact]
    public void UpdateLine_InCompletedOrder_ShouldThrowInvalidOperationException()
    {
        var purchaseOrder = CreateValidPurchaseOrder();
        purchaseOrder.AddLine(ValidMaterialId, ValidName, 10, 25.50m, "notes");
        var lineId = purchaseOrder.Lines.First().Id;
        purchaseOrder.ChangeStatus(PurchaseOrderStatus.InTransit, ValidCreatedBy);
        purchaseOrder.ChangeStatus(PurchaseOrderStatus.Completed, ValidCreatedBy);

        var action = () => purchaseOrder.UpdateLine(lineId, "Updated Material Name", 20, 30.00m, "new notes");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Cannot update lines in completed orders");
    }

    [Fact]
    public void Update_DraftOrder_ShouldUpdateProperties()
    {
        var purchaseOrder = CreateValidPurchaseOrder();
        var newExpectedDeliveryDate = DateTime.UtcNow.Date.AddDays(21);
        const string newNotes = "Updated notes";
        const string updatedBy = "updater@example.com";

        purchaseOrder.Update(ValidSupplierName, newExpectedDeliveryDate, null, newNotes, updatedBy);

        purchaseOrder.ExpectedDeliveryDate.Should().Be(newExpectedDeliveryDate);
        purchaseOrder.Notes.Should().Be(newNotes);
        purchaseOrder.UpdatedBy.Should().Be(updatedBy);
        purchaseOrder.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void Update_InTransitOrder_ShouldUpdateProperties()
    {
        var purchaseOrder = CreateValidPurchaseOrder();
        purchaseOrder.ChangeStatus(PurchaseOrderStatus.InTransit, ValidCreatedBy);
        var newExpectedDeliveryDate = DateTime.UtcNow.Date.AddDays(21);
        const string newNotes = "Updated notes";
        const string updatedBy = "updater@example.com";

        purchaseOrder.Update(ValidSupplierName, newExpectedDeliveryDate, null, newNotes, updatedBy);

        purchaseOrder.ExpectedDeliveryDate.Should().Be(newExpectedDeliveryDate);
        purchaseOrder.Notes.Should().Be(newNotes);
        purchaseOrder.UpdatedBy.Should().Be(updatedBy);
    }

    [Fact]
    public void Update_CompletedOrder_ShouldThrowInvalidOperationException()
    {
        var purchaseOrder = CreateValidPurchaseOrder();
        purchaseOrder.ChangeStatus(PurchaseOrderStatus.InTransit, ValidCreatedBy);
        purchaseOrder.ChangeStatus(PurchaseOrderStatus.Completed, ValidCreatedBy);
        var newExpectedDeliveryDate = DateTime.UtcNow.Date.AddDays(21);

        var action = () => purchaseOrder.Update(ValidSupplierName, newExpectedDeliveryDate, null, "new notes", "updater");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Cannot update completed orders");
    }

    [Fact]
    public void ChangeStatus_FromDraftToInTransit_ShouldUpdateStatusAndHistory()
    {
        var purchaseOrder = CreateValidPurchaseOrder();
        const string changedBy = "changer@example.com";

        purchaseOrder.ChangeStatus(PurchaseOrderStatus.InTransit, changedBy);

        purchaseOrder.Status.Should().Be(PurchaseOrderStatus.InTransit);
        purchaseOrder.UpdatedBy.Should().Be(changedBy);
        purchaseOrder.UpdatedAt.Should().NotBeNull();
        purchaseOrder.History.Should().HaveCount(2);

        var statusChangeEntry = purchaseOrder.History.Last();
        statusChangeEntry.Action.Should().Contain("Status changed");
        statusChangeEntry.OldValue.Should().Be("Draft");
        statusChangeEntry.NewValue.Should().Be("InTransit");
        statusChangeEntry.ChangedBy.Should().Be(changedBy);
    }

    [Fact]
    public void ChangeStatus_FromInTransitToCompleted_ShouldUpdateStatusAndHistory()
    {
        var purchaseOrder = CreateValidPurchaseOrder();
        purchaseOrder.ChangeStatus(PurchaseOrderStatus.InTransit, ValidCreatedBy);
        const string changedBy = "completer@example.com";

        purchaseOrder.ChangeStatus(PurchaseOrderStatus.Completed, changedBy);

        purchaseOrder.Status.Should().Be(PurchaseOrderStatus.Completed);
        purchaseOrder.UpdatedBy.Should().Be(changedBy);
        purchaseOrder.History.Should().HaveCount(3);

        var statusChangeEntry = purchaseOrder.History.Last();
        statusChangeEntry.Action.Should().Contain("Status changed");
        statusChangeEntry.OldValue.Should().Be("InTransit");
        statusChangeEntry.NewValue.Should().Be("Completed");
        statusChangeEntry.ChangedBy.Should().Be(changedBy);
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Draft, PurchaseOrderStatus.Completed)]
    [InlineData(PurchaseOrderStatus.InTransit, PurchaseOrderStatus.Draft)]
    [InlineData(PurchaseOrderStatus.Completed, PurchaseOrderStatus.Draft)]
    [InlineData(PurchaseOrderStatus.Completed, PurchaseOrderStatus.InTransit)]
    public void ChangeStatus_InvalidTransition_ShouldThrowInvalidOperationException(
        PurchaseOrderStatus fromStatus, PurchaseOrderStatus toStatus)
    {
        var purchaseOrder = CreateValidPurchaseOrder();

        if (fromStatus == PurchaseOrderStatus.InTransit)
        {
            purchaseOrder.ChangeStatus(PurchaseOrderStatus.InTransit, ValidCreatedBy);
        }
        else if (fromStatus == PurchaseOrderStatus.Completed)
        {
            purchaseOrder.ChangeStatus(PurchaseOrderStatus.InTransit, ValidCreatedBy);
            purchaseOrder.ChangeStatus(PurchaseOrderStatus.Completed, ValidCreatedBy);
        }

        var action = () => purchaseOrder.ChangeStatus(toStatus, ValidCreatedBy);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage($"Invalid status transition from {fromStatus} to {toStatus}");
    }

    [Fact]
    public void TotalAmount_WithMultipleLines_ShouldCalculateCorrectTotal()
    {
        var purchaseOrder = CreateValidPurchaseOrder();
        purchaseOrder.AddLine(ValidMaterialId, ValidName, 10, 25.50m, "line 1");
        purchaseOrder.AddLine("MAT002", "Material 2", 5, 100.00m, "line 2");
        purchaseOrder.AddLine("MAT003", "Material 3", 3, 33.33m, "line 3");

        purchaseOrder.TotalAmount.Should().Be(854.99m);
    }

    [Fact]
    public void History_OnCreation_ShouldContainCreationEntry()
    {
        var purchaseOrder = CreateValidPurchaseOrder();

        purchaseOrder.History.Should().HaveCount(1);
        var historyEntry = purchaseOrder.History.First();
        historyEntry.Action.Should().Be("Order created");
        historyEntry.OldValue.Should().BeNull();
        historyEntry.NewValue.Should().Be("Draft");
        historyEntry.ChangedBy.Should().Be(ValidCreatedBy);
        historyEntry.ChangedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Constructor_WithContactVia_ShouldSetContactViaProperty()
    {
        var purchaseOrder = new PurchaseOrder(
            ValidOrderNumber,
            ValidSupplierName,
            ValidOrderDate,
            ValidExpectedDeliveryDate,
            ContactVia.Email,
            ValidNotes,
            ValidCreatedBy);

        purchaseOrder.ContactVia.Should().Be(ContactVia.Email);
    }

    [Fact]
    public void Constructor_WithNullContactVia_ShouldSetContactViaToNull()
    {
        var purchaseOrder = new PurchaseOrder(
            ValidOrderNumber,
            ValidSupplierName,
            ValidOrderDate,
            ValidExpectedDeliveryDate,
            null,
            ValidNotes,
            ValidCreatedBy);

        purchaseOrder.ContactVia.Should().BeNull();
    }

    [Fact]
    public void Update_WithContactVia_ShouldUpdateContactViaProperty()
    {
        var purchaseOrder = CreateValidPurchaseOrder();
        var updatedBy = "updated@example.com";

        purchaseOrder.Update(ValidSupplierName, ValidExpectedDeliveryDate, ContactVia.Phone, ValidNotes, updatedBy);

        purchaseOrder.ContactVia.Should().Be(ContactVia.Phone);
        purchaseOrder.UpdatedBy.Should().Be(updatedBy);
        purchaseOrder.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ClearAllLines_InTransitOrder_ShouldClearLinesSuccessfully()
    {
        var purchaseOrder = CreateValidPurchaseOrder();
        purchaseOrder.AddLine(ValidMaterialId, ValidName, 10, 25.50m, "line 1");
        purchaseOrder.AddLine("MAT002", "Material 2", 5, 100.00m, "line 2");
        purchaseOrder.ChangeStatus(PurchaseOrderStatus.InTransit, ValidCreatedBy);

        purchaseOrder.ClearAllLines();

        purchaseOrder.Lines.Should().BeEmpty();
        purchaseOrder.TotalAmount.Should().Be(0);
    }

    [Fact]
    public void ClearAllLines_CompletedOrder_ShouldThrowInvalidOperationException()
    {
        var purchaseOrder = CreateValidPurchaseOrder();
        purchaseOrder.AddLine(ValidMaterialId, ValidName, 10, 25.50m, "line 1");
        purchaseOrder.ChangeStatus(PurchaseOrderStatus.InTransit, ValidCreatedBy);
        purchaseOrder.ChangeStatus(PurchaseOrderStatus.Completed, ValidCreatedBy);

        var action = () => purchaseOrder.ClearAllLines();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Cannot clear lines from completed orders");
    }

    [Fact]
    public void UpdateOrderNumber_InTransitOrder_ShouldUpdateSuccessfully()
    {
        var purchaseOrder = CreateValidPurchaseOrder();
        purchaseOrder.ChangeStatus(PurchaseOrderStatus.InTransit, ValidCreatedBy);
        const string newOrderNumber = "PO-2024-002";
        const string updatedBy = "updater@example.com";

        purchaseOrder.UpdateOrderNumber(newOrderNumber, updatedBy);

        purchaseOrder.OrderNumber.Should().Be(newOrderNumber);
        purchaseOrder.UpdatedBy.Should().Be(updatedBy);
        purchaseOrder.UpdatedAt.Should().NotBeNull();
        purchaseOrder.History.Should().HaveCountGreaterThan(1);

        var historyEntry = purchaseOrder.History.Last();
        historyEntry.Action.Should().Contain("Order number changed");
        historyEntry.OldValue.Should().Be(ValidOrderNumber);
        historyEntry.NewValue.Should().Be(newOrderNumber);
    }

    [Fact]
    public void UpdateOrderNumber_CompletedOrder_ShouldThrowInvalidOperationException()
    {
        var purchaseOrder = CreateValidPurchaseOrder();
        purchaseOrder.ChangeStatus(PurchaseOrderStatus.InTransit, ValidCreatedBy);
        purchaseOrder.ChangeStatus(PurchaseOrderStatus.Completed, ValidCreatedBy);

        var action = () => purchaseOrder.UpdateOrderNumber("PO-2024-002", "updater@example.com");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Cannot update order number for completed orders");
    }

    [Fact]
    public void CanEdit_DraftStatus_ShouldReturnTrue()
    {
        var purchaseOrder = CreateValidPurchaseOrder();

        purchaseOrder.CanEdit.Should().BeTrue();
    }

    [Fact]
    public void CanEdit_InTransitStatus_ShouldReturnTrue()
    {
        var purchaseOrder = CreateValidPurchaseOrder();
        purchaseOrder.ChangeStatus(PurchaseOrderStatus.InTransit, ValidCreatedBy);

        purchaseOrder.CanEdit.Should().BeTrue();
    }

    [Fact]
    public void CanEdit_CompletedStatus_ShouldReturnFalse()
    {
        var purchaseOrder = CreateValidPurchaseOrder();
        purchaseOrder.ChangeStatus(PurchaseOrderStatus.InTransit, ValidCreatedBy);
        purchaseOrder.ChangeStatus(PurchaseOrderStatus.Completed, ValidCreatedBy);

        purchaseOrder.CanEdit.Should().BeFalse();
    }

    private static PurchaseOrder CreateValidPurchaseOrder()
    {
        return new PurchaseOrder(
            ValidOrderNumber,
            ValidSupplierName,
            ValidOrderDate,
            ValidExpectedDeliveryDate,
            null,
            ValidNotes,
            ValidCreatedBy);
    }
}