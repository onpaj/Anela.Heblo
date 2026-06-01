using Anela.Heblo.Domain.Features.Purchase;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Domain.Purchase;

public class PurchaseOrderLineTests
{
    private const int ValidPurchaseOrderId = 1;
    private const string ValidMaterialId = "MAT001";
    private const string ValidMaterialName = "Test Material Name";
    private const decimal ValidQuantity = 10.5m;
    private const decimal ValidUnitPrice = 25.50m;
    private const string ValidNotes = "Test line notes";

    [Fact]
    public void Constructor_WithValidParameters_ShouldCreatePurchaseOrderLine()
    {
        var line = new PurchaseOrderLine(
            ValidPurchaseOrderId,
            ValidMaterialId,
            ValidMaterialName,
            ValidQuantity,
            ValidUnitPrice,
            ValidNotes);

        line.Id.Should().Be(0); // For new entities, EF will set this to 0 until saved
        line.PurchaseOrderId.Should().Be(ValidPurchaseOrderId);
        line.MaterialId.Should().Be(ValidMaterialId);
        line.MaterialName.Should().Be(ValidMaterialName);
        line.Quantity.Should().Be(ValidQuantity);
        line.UnitPrice.Should().Be(ValidUnitPrice);
        line.Notes.Should().Be(ValidNotes);
        line.LineTotal.Should().Be(267.75m);
    }

    [Fact]
    public void Constructor_WithNullNotes_ShouldCreatePurchaseOrderLine()
    {
        var line = new PurchaseOrderLine(
            ValidPurchaseOrderId,
            ValidMaterialId,
            ValidMaterialName,
            ValidQuantity,
            ValidUnitPrice,
            null);

        line.Notes.Should().BeNull();
        line.LineTotal.Should().Be(267.75m);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    public void Constructor_WithInvalidMaterialId_ShouldThrowArgumentException(string? invalidMaterialId)
    {
        var action = () => new PurchaseOrderLine(
            ValidPurchaseOrderId,
            invalidMaterialId,
            ValidMaterialName,
            ValidQuantity,
            ValidUnitPrice,
            ValidNotes);

        action.Should().Throw<ArgumentException>()
            .WithParameterName("materialId")
            .WithMessage("Material ID cannot be null or empty*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-10.5)]
    public void Constructor_WithInvalidQuantity_ShouldThrowArgumentException(decimal invalidQuantity)
    {
        var action = () => new PurchaseOrderLine(
            ValidPurchaseOrderId,
            ValidMaterialId,
            ValidMaterialName,
            invalidQuantity,
            ValidUnitPrice,
            ValidNotes);

        action.Should().Throw<ArgumentException>()
            .WithParameterName("quantity")
            .WithMessage("Quantity must be greater than zero*");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-25.50)]
    public void Constructor_WithNegativeUnitPrice_ShouldThrowArgumentException(decimal invalidUnitPrice)
    {
        var action = () => new PurchaseOrderLine(
            ValidPurchaseOrderId,
            ValidMaterialId,
            ValidMaterialName,
            ValidQuantity,
            invalidUnitPrice,
            ValidNotes);

        action.Should().Throw<ArgumentException>()
            .WithParameterName("unitPrice")
            .WithMessage("Unit price cannot be negative*");
    }

    [Fact]
    public void Constructor_WithZeroUnitPrice_ShouldCreatePurchaseOrderLine()
    {
        var line = new PurchaseOrderLine(
            ValidPurchaseOrderId,
            ValidMaterialId,
            ValidMaterialName,
            ValidQuantity,
            0,
            ValidNotes);

        line.UnitPrice.Should().Be(0);
        line.LineTotal.Should().Be(0);
    }

    [Fact]
    public void Update_WithValidParameters_ShouldUpdatePropertiesAndRecalculateTotal()
    {
        var line = new PurchaseOrderLine(
            ValidPurchaseOrderId,
            ValidMaterialId,
            ValidMaterialName,
            ValidQuantity,
            ValidUnitPrice,
            ValidNotes);

        const decimal newQuantity = 20;
        const decimal newUnitPrice = 30.00m;
        const string newNotes = "Updated notes";

        line.Update("Updated Material Name", newQuantity, newUnitPrice, newNotes);

        line.Quantity.Should().Be(newQuantity);
        line.UnitPrice.Should().Be(newUnitPrice);
        line.Notes.Should().Be(newNotes);
        line.LineTotal.Should().Be(600.00m);
    }

    [Fact]
    public void Update_WithNullNotes_ShouldUpdateToNullNotes()
    {
        var line = new PurchaseOrderLine(
            ValidPurchaseOrderId,
            ValidMaterialId,
            ValidMaterialName,
            ValidQuantity,
            ValidUnitPrice,
            ValidNotes);

        line.Update(ValidMaterialName, ValidQuantity, ValidUnitPrice, null);

        line.Notes.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-5.5)]
    public void Update_WithInvalidQuantity_ShouldThrowArgumentException(decimal invalidQuantity)
    {
        var line = new PurchaseOrderLine(
            ValidPurchaseOrderId,
            ValidMaterialId,
            ValidMaterialName,
            ValidQuantity,
            ValidUnitPrice,
            ValidNotes);

        var action = () => line.Update(ValidMaterialName, invalidQuantity, ValidUnitPrice, ValidNotes);

        action.Should().Throw<ArgumentException>()
            .WithParameterName("quantity")
            .WithMessage("Quantity must be greater than zero*");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-25.50)]
    public void Update_WithNegativeUnitPrice_ShouldThrowArgumentException(decimal invalidUnitPrice)
    {
        var line = new PurchaseOrderLine(
            ValidPurchaseOrderId,
            ValidMaterialId,
            ValidMaterialName,
            ValidQuantity,
            ValidUnitPrice,
            ValidNotes);

        var action = () => line.Update(ValidMaterialName, ValidQuantity, invalidUnitPrice, ValidNotes);

        action.Should().Throw<ArgumentException>()
            .WithParameterName("unitPrice")
            .WithMessage("Unit price cannot be negative*");
    }

    [Theory]
    [InlineData(1, 10, 10)]
    [InlineData(2.5, 20, 50)]
    [InlineData(10.33, 15.75, 162.6975)]
    [InlineData(100, 0, 0)]
    public void LineTotal_WithDifferentQuantityAndPrice_ShouldCalculateCorrectTotal(
        decimal quantity, decimal unitPrice, decimal expectedTotal)
    {
        var line = new PurchaseOrderLine(
            ValidPurchaseOrderId,
            ValidMaterialId,
            ValidMaterialName,
            quantity,
            unitPrice,
            ValidNotes);

        line.LineTotal.Should().Be(expectedTotal);
    }

    [Fact]
    public void LineTotal_AfterUpdate_ShouldRecalculate()
    {
        var line = new PurchaseOrderLine(
            ValidPurchaseOrderId,
            ValidMaterialId,
            ValidMaterialName,
            ValidQuantity,
            ValidUnitPrice,
            ValidNotes);

        var originalTotal = line.LineTotal;
        originalTotal.Should().Be(267.75m);

        line.Update("Updated Material", 5, 50, ValidNotes);

        line.LineTotal.Should().Be(250m);
        line.LineTotal.Should().NotBe(originalTotal);
    }
}