using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Domain.Features.InvoiceClassification.Rules;
using Anela.Heblo.Tests.Features.InvoiceClassification.TestHelpers;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.InvoiceClassification.Rules;

public class ItemDescriptionClassificationRuleTests
{
    private readonly ItemDescriptionClassificationRule _sut = new();

    [Fact]
    public void Evaluate_FirstItemNameMatches_ReturnsTrue()
    {
        // Arrange
        var invoice = InvoiceClassificationFixtures.CreateInvoice(itemNames: new[] { "Widget", "Gizmo" });

        // Act
        var result = _sut.Evaluate(invoice, "Widget");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_NonFirstItemNameMatches_ReturnsTrue()
    {
        // Arrange
        var invoice = InvoiceClassificationFixtures.CreateInvoice(itemNames: new[] { "Widget", "Gizmo", "Doohickey" });

        // Act
        var result = _sut.Evaluate(invoice, "Doohickey");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_NoItemNameMatches_ReturnsFalse()
    {
        // Arrange
        var invoice = InvoiceClassificationFixtures.CreateInvoice(itemNames: new[] { "Widget", "Gizmo" });

        // Act
        var result = _sut.Evaluate(invoice, "Sprocket");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_EmptyItemsList_ReturnsFalse()
    {
        // Arrange
        var invoice = InvoiceClassificationFixtures.CreateInvoice();

        // Act
        var result = _sut.Evaluate(invoice, "Widget");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_AllItemNamesNullOrWhitespace_ReturnsFalse()
    {
        // Arrange — bypass the fixture (which only accepts string[]) and construct
        // items with empty/whitespace names directly. ReceivedInvoiceItem.Name
        // defaults to "" and cannot be set to null via the POCO initializer in C#
        // nullable-aware mode, so we use empty and whitespace which take the same
        // guard branch inside EvaluateItemDescription.
        var invoice = new ReceivedInvoice
        {
            Items = new List<ReceivedInvoiceItem>
            {
                new() { Name = string.Empty },
                new() { Name = "   " }
            }
        };

        // Act
        var result = _sut.Evaluate(invoice, "anything");

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Evaluate_NullOrWhitespacePattern_ReturnsFalse(string? pattern)
    {
        // Arrange
        var invoice = InvoiceClassificationFixtures.CreateInvoice(itemNames: new[] { "Widget" });

        // Act
        var result = _sut.Evaluate(invoice, pattern!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_RegexMatchesItemNameCaseInsensitive_ReturnsTrue()
    {
        // Arrange
        var invoice = InvoiceClassificationFixtures.CreateInvoice(itemNames: new[] { "WIDGET" });

        // Act
        var result = _sut.Evaluate(invoice, "widget");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_InvalidRegexFallsBackToContainsPerItem_ReturnsTrue()
    {
        // Arrange
        // "[" throws ArgumentException from Regex.IsMatch; the per-item helper
        // falls back to Contains("[", OrdinalIgnoreCase) on each item name.
        var invoice = InvoiceClassificationFixtures.CreateInvoice(itemNames: new[] { "Plain item", "Bracket [item]" });

        // Act
        var result = _sut.Evaluate(invoice, "[");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_InvalidRegexFallbackMissesAllItems_ReturnsFalse()
    {
        // Arrange
        var invoice = InvoiceClassificationFixtures.CreateInvoice(itemNames: new[] { "Plain item", "Another plain item" });

        // Act
        var result = _sut.Evaluate(invoice, "[");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_MixedNullAndMatchingItems_DoesNotThrow_AndReturnsTrue()
    {
        // Arrange — an empty-named item between two real items: the empty one
        // must be silently skipped, the matching one must still be found.
        var invoice = new ReceivedInvoice
        {
            Items = new List<ReceivedInvoiceItem>
            {
                new() { Name = string.Empty },
                new() { Name = "Widget" },
                new() { Name = "   " }
            }
        };

        // Act
        var result = _sut.Evaluate(invoice, "Widget");

        // Assert
        result.Should().BeTrue();
    }
}
