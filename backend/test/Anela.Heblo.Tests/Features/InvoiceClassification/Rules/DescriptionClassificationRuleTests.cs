using Anela.Heblo.Domain.Features.InvoiceClassification.Rules;
using Anela.Heblo.Tests.Features.InvoiceClassification.TestHelpers;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.InvoiceClassification.Rules;

public class DescriptionClassificationRuleTests
{
    private readonly DescriptionClassificationRule _sut = new();

    [Fact]
    public void Evaluate_RegexMatchesDescription_ReturnsTrue()
    {
        // Arrange
        var invoice = InvoiceClassificationFixtures.CreateInvoice(description: "Monthly hosting invoice");

        // Act
        var result = _sut.Evaluate(invoice, "hosting");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_RegexMatchesCaseInsensitive_ReturnsTrue()
    {
        // Arrange
        var invoice = InvoiceClassificationFixtures.CreateInvoice(description: "Monthly HOSTING invoice");

        // Act
        var result = _sut.Evaluate(invoice, "hosting");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_ValidRegexDoesNotMatch_ReturnsFalse()
    {
        // Arrange
        var invoice = InvoiceClassificationFixtures.CreateInvoice(description: "Monthly hosting invoice");

        // Act
        var result = _sut.Evaluate(invoice, "consulting");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_InvalidRegexThatIsSubstring_FallsBackToContains_ReturnsTrue()
    {
        // Arrange
        // "[" is an unclosed character class — Regex.IsMatch throws ArgumentException,
        // the only exception type the rule catches, triggering the Contains fallback.
        var invoice = InvoiceClassificationFixtures.CreateInvoice(description: "Note [archived]");

        // Act
        var result = _sut.Evaluate(invoice, "[");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_InvalidRegexThatIsNotSubstring_FallsBackToContains_ReturnsFalse()
    {
        // Arrange
        var invoice = InvoiceClassificationFixtures.CreateInvoice(description: "Plain description");

        // Act
        var result = _sut.Evaluate(invoice, "[");

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Evaluate_NullOrWhitespaceDescription_ReturnsFalse(string? description)
    {
        // Arrange
        var invoice = InvoiceClassificationFixtures.CreateInvoice(description: description!);

        // Act
        var result = _sut.Evaluate(invoice, "hosting");

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
        var invoice = InvoiceClassificationFixtures.CreateInvoice(description: "Monthly hosting invoice");

        // Act
        var result = _sut.Evaluate(invoice, pattern!);

        // Assert
        result.Should().BeFalse();
    }
}
