using Anela.Heblo.Domain.Features.InvoiceClassification.Rules;
using Anela.Heblo.Tests.Features.InvoiceClassification.TestHelpers;
using System.Globalization;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.InvoiceClassification.Rules;

public class AmountClassificationRuleTests
{
    private readonly AmountClassificationRule _sut = new();

    [Theory]
    // >=
    [InlineData(">=100", 100, true)]   // equal to threshold
    [InlineData(">=100", 101, true)]   // above threshold
    [InlineData(">=100", 99, false)]   // below threshold
    // <=
    [InlineData("<=100", 100, true)]   // equal to threshold
    [InlineData("<=100", 99, true)]    // below threshold
    [InlineData("<=100", 101, false)]  // above threshold
    // >
    [InlineData(">100", 101, true)]    // strictly above
    [InlineData(">100", 100, false)]   // equal — must be false
    [InlineData(">100", 99, false)]    // below
    // <
    [InlineData("<100", 99, true)]     // strictly below
    [InlineData("<100", 100, false)]   // equal — must be false
    [InlineData("<100", 101, false)]   // above
    // =
    [InlineData("=100", 100, true)]    // equal
    [InlineData("=100", 101, false)]   // above
    [InlineData("=100", 99, false)]    // below
    // bare literal (no operator) → equivalent to "="
    [InlineData("100", 100, true)]
    [InlineData("100", 99, false)]
    [InlineData("100", 101, false)]
    public void Evaluate_OperatorBoundary_ReturnsExpected(string pattern, int amount, bool expected)
    {
        // Arrange
        var invoice = InvoiceClassificationFixtures.CreateInvoice(totalAmount: amount);

        // Act
        var result = _sut.Evaluate(invoice, pattern);

        // Assert
        result.Should().Be(expected);
    }

    private static void WithCulture(string name, Action action)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            CultureInfo.CurrentUICulture = new CultureInfo(name);
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Theory]
    // >=
    [InlineData(">=1000.50", "1000.50", true)]
    [InlineData(">=1000.50", "1000.51", true)]
    [InlineData(">=1000.50", "1000.49", false)]
    // <=
    [InlineData("<=1000.50", "1000.50", true)]
    [InlineData("<=1000.50", "1000.49", true)]
    [InlineData("<=1000.50", "1000.51", false)]
    // >
    [InlineData(">1000.50", "1000.51", true)]
    [InlineData(">1000.50", "1000.50", false)]
    [InlineData(">1000.50", "1000.49", false)]
    // <
    [InlineData("<1000.50", "1000.49", true)]
    [InlineData("<1000.50", "1000.50", false)]
    [InlineData("<1000.50", "1000.51", false)]
    // =
    [InlineData("=1000.50", "1000.50", true)]
    [InlineData("=1000.50", "1000.51", false)]
    // bare literal
    [InlineData("1000.50", "1000.50", true)]
    [InlineData("1000.50", "1000.49", false)]
    public void Evaluate_DecimalDotPatternUnderCzechCulture_ReturnsExpected(string pattern, string amount, bool expected)
    {
        WithCulture("cs-CZ", () =>
        {
            // Arrange
            var invoice = InvoiceClassificationFixtures.CreateInvoice(
                totalAmount: decimal.Parse(amount, CultureInfo.InvariantCulture));

            // Act
            var result = _sut.Evaluate(invoice, pattern);

            // Assert
            result.Should().Be(expected);
        });
    }

    [Fact]
    public void Evaluate_DecimalCommaPatternUnderCzechCulture_ReturnsFalse()
    {
        WithCulture("cs-CZ", () =>
        {
            // Arrange
            var invoice = InvoiceClassificationFixtures.CreateInvoice(totalAmount: 1000.50m);

            // Act
            var result = _sut.Evaluate(invoice, ">=1000,50");

            // Assert
            result.Should().BeFalse();
        });
    }

    [Fact]
    public void Evaluate_PatternWithWhitespaceAfterOperator_StillMatches()
    {
        WithCulture("cs-CZ", () =>
        {
            // Arrange
            var invoice = InvoiceClassificationFixtures.CreateInvoice(totalAmount: 100m);

            // Act
            var result = _sut.Evaluate(invoice, ">= 100");

            // Assert
            result.Should().BeTrue();
        });
    }

    [Fact]
    public void Evaluate_EmptyPattern_ReturnsFalse()
    {
        // Arrange
        var invoice = InvoiceClassificationFixtures.CreateInvoice(totalAmount: 100m);

        // Act
        var result = _sut.Evaluate(invoice, string.Empty);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_WhitespacePattern_ReturnsFalse()
    {
        // Arrange
        var invoice = InvoiceClassificationFixtures.CreateInvoice(totalAmount: 100m);

        // Act
        var result = _sut.Evaluate(invoice, "   ");

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(">=abc")]
    [InlineData("<=abc")]
    [InlineData(">abc")]
    [InlineData("<abc")]
    [InlineData("=abc")]
    public void Evaluate_OperatorWithNonNumericBody_ReturnsFalse(string pattern)
    {
        // Arrange
        // decimal.TryParse returns false (does NOT throw) — the early-exit path,
        // not the unreachable catch block.
        var invoice = InvoiceClassificationFixtures.CreateInvoice(totalAmount: 100m);

        // Act
        var result = _sut.Evaluate(invoice, pattern);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_BareNonNumericPattern_ReturnsFalse()
    {
        // Arrange
        var invoice = InvoiceClassificationFixtures.CreateInvoice(totalAmount: 100m);

        // Act
        var result = _sut.Evaluate(invoice, "abc");

        // Assert
        result.Should().BeFalse();
    }
}
