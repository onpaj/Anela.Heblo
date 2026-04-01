using Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.ErrorFilters.Filters;

public class InsufficientSemiProductFilterTests
{
    private readonly InsufficientSemiProductFilter _filter = new();

    [Fact]
    public void CanHandle_WhenMessageContainsPolotovaryKeywords_ReturnsTrue()
    {
        var ex = new InvalidOperationException(
            "Failed to finalize issued order 5260: Nelze vytvořit příjemku výrobku 'OCH006030 - Ochráním bradavky 30 ml' kvůli chybějícímu materiálu 'OCH0060001M - Ochráním bradavky - meziprodukt' na skladu 'POLOTOVARY - Nerozplněné produkty' (požadováno: 4 095,000000, dostupné: 4 000,000000).");

        _filter.CanHandle(ex).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_WhenMessageContainsMaterialWarehouse_ReturnsFalse()
    {
        var ex = new InvalidOperationException(
            "Failed to finalize issued order 5662: Nelze vytvořit příjemku výrobku 'SER001001M - Bezstarostná krása' kvůli chybějícímu materiálu 'OLE037 - Rýžový olej LZS' na skladu 'MATERIAL - Sklad Materialu' (požadováno: 717,846000, dostupné: 542,077000).");

        _filter.CanHandle(ex).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_WhenMessageIsUnrelated_ReturnsFalse()
    {
        var ex = new InvalidOperationException("Some other error");

        _filter.CanHandle(ex).Should().BeFalse();
    }

    [Fact]
    public void Transform_ExtractsMaterialNameAndQuantities()
    {
        var ex = new InvalidOperationException(
            "Failed to finalize issued order 5260: Nelze vytvořit příjemku výrobku 'OCH006030 - Ochráním bradavky - mast pro kojicí maminky 30 ml' kvůli chybějícímu materiálu 'OCH0060001M - Ochráním bradavky - meziprodukt' na skladu 'POLOTOVARY - Nerozplněné produkty' (požadováno: 4 095,000000, dostupné: 4 000,000000).");

        var result = _filter.Transform(ex);

        result.Should().Be("Nedostatek meziproduktu 'OCH0060001M - Ochráním bradavky - meziprodukt' na skladu POLOTOVARY (požadováno: 4 095,000000, dostupné: 4 000,000000).");
    }
}
