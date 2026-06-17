using System.Text.RegularExpressions;
using Anela.Heblo.Domain.Features.InvoiceClassification.Rules;
using Anela.Heblo.Tests.Features.InvoiceClassification.TestHelpers;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.InvoiceClassification.Rules;

public class CompanyNameClassificationRuleTests
{
    private readonly CompanyNameClassificationRule _sut = new();

    [Fact]
    public void Evaluate_RegexMatchesCompanyName_ReturnsTrue()
    {
        // Arrange
        var invoice = InvoiceClassificationFixtures.CreateInvoice(companyName: "ACME s.r.o.");

        // Act
        var result = _sut.Evaluate(invoice, "ACME");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_RegexMatchesCaseInsensitive_ReturnsTrue()
    {
        // Arrange
        var invoice = InvoiceClassificationFixtures.CreateInvoice(companyName: "ACME s.r.o.");

        // Act
        var result = _sut.Evaluate(invoice, "acme");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_ValidRegexDoesNotMatch_ReturnsFalse()
    {
        // Arrange
        var invoice = InvoiceClassificationFixtures.CreateInvoice(companyName: "ACME s.r.o.");

        // Act
        var result = _sut.Evaluate(invoice, "Globex");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_InvalidRegexThatIsSubstring_FallsBackToContains_ReturnsTrue()
    {
        // Arrange
        // "[" is an unclosed character class — Regex.IsMatch throws ArgumentException,
        // which is the ONLY exception type the rule catches. The fallback then runs
        // string.Contains("[", OrdinalIgnoreCase) against the company name.
        var invoice = InvoiceClassificationFixtures.CreateInvoice(companyName: "Company [old]");

        // Act
        var result = _sut.Evaluate(invoice, "[");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_InvalidRegexThatIsNotSubstring_FallsBackToContains_ReturnsFalse()
    {
        // Arrange
        var invoice = InvoiceClassificationFixtures.CreateInvoice(companyName: "Plain Company");

        // Act
        var result = _sut.Evaluate(invoice, "[");

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Evaluate_NullOrWhitespaceCompanyName_ReturnsFalse(string? companyName)
    {
        // Arrange
        var invoice = InvoiceClassificationFixtures.CreateInvoice(companyName: companyName!);

        // Act
        var result = _sut.Evaluate(invoice, "ACME");

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
        var invoice = InvoiceClassificationFixtures.CreateInvoice(companyName: "ACME s.r.o.");

        // Act
        var result = _sut.Evaluate(invoice, pattern!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Sanity_UnclosedCharacterClass_ThrowsArgumentException()
    {
        // Guard: if a future .NET version changes the exception type thrown by
        // Regex.IsMatch for "[", the fallback tests above would silently stop
        // exercising the fallback branch. This sanity check makes that drift loud.
        var act = () => Regex.IsMatch("anything", "[", RegexOptions.IgnoreCase);

        act.Should().Throw<ArgumentException>();
    }
}
