using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Domain.Features.InvoiceClassification.Rules;
using Anela.Heblo.Tests.Features.InvoiceClassification.TestHelpers;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.InvoiceClassification.Rules;

public class VatClassificationRuleTests
{
    private readonly VatClassificationRule _sut = new();

    [Theory]
    // exact match
    [InlineData("12345678", "12345678", true)]
    // case-insensitive match (synthetic alphanumeric IČO — pure-numeric IČO has no case variance)
    [InlineData("CZ12345678", "cz12345678", true)]
    // non-match
    [InlineData("12345678", "87654321", false)]
    // leading/trailing whitespace — trimmed before comparison
    [InlineData("  12345678  ", "12345678", true)]
    [InlineData("12345678", "  12345678  ", true)]
    [InlineData(" 12345678 ", " 12345678 ", true)]
    // internal whitespace is NOT trimmed away — only outer whitespace is ignored
    [InlineData("1234 5678", "12345678", false)]
    // empty / whitespace-only values
    [InlineData("", "", true)]
    [InlineData("   ", "", true)]
    [InlineData("12345678", "", false)]
    public void Evaluate_MatchScenarios_ReturnsExpected(string companyVat, string pattern, bool expected)
    {
        // Arrange
        var invoice = InvoiceClassificationFixtures.CreateInvoice(companyVat: companyVat);

        // Act
        var result = _sut.Evaluate(invoice, pattern);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void Evaluate_NullPattern_ReturnsFalse()
    {
        // Arrange
        var invoice = InvoiceClassificationFixtures.CreateInvoice(companyVat: "12345678");

        // Act
        var result = _sut.Evaluate(invoice, null!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_NullCompanyVat_ReturnsFalse()
    {
        // Arrange
        var invoice = new ReceivedInvoice { CompanyVat = null! };

        // Act
        var result = _sut.Evaluate(invoice, "12345678");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_NullCompanyVatAndNullPattern_ReturnsTrue()
    {
        // Arrange
        var invoice = new ReceivedInvoice { CompanyVat = null! };

        // Act
        var result = _sut.Evaluate(invoice, null!);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Properties_ReturnExpectedMetadata()
    {
        _sut.Identifier.Should().Be("ICO");
        _sut.DisplayName.Should().Be("IČO");
        _sut.Description.Should().Be("Porovnání IČO firmy");
    }
}
