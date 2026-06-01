using Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.ErrorFilters.Filters;

public class InsufficientRawMaterialFilterTests
{
    private readonly InsufficientRawMaterialFilter _filter = new();

    [Fact]
    public void CanHandle_WhenMessageContainsMaterialWarehouseKeywords_ReturnsTrue()
    {
        var ex = new InvalidOperationException(
            "Failed to finalize issued order 5662: Nelze vytvořit příjemku výrobku 'SER001001M - Bezstarostná krása' kvůli chybějícímu materiálu 'OLE037 - Rýžový olej LZS' na skladu 'MATERIAL - Sklad Materialu' (požadováno: 717,846000, dostupné: 542,077000).");

        _filter.CanHandle(ex).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_WhenMessageContainsPolotovaryWarehouse_ReturnsFalse()
    {
        var ex = new InvalidOperationException(
            "Nelze vytvořit příjemku výrobku 'OCH006030' kvůli chybějícímu materiálu 'OCH0060001M' na skladu 'POLOTOVARY - Nerozplněné produkty' (požadováno: 100, dostupné: 50).");

        _filter.CanHandle(ex).Should().BeFalse();
    }

    [Fact]
    public void Transform_ExtractsMaterialNameAndQuantities()
    {
        var ex = new InvalidOperationException(
            "Failed to finalize issued order 5662: Nelze vytvořit příjemku výrobku 'SER001001M - Bezstarostná krása' kvůli chybějícímu materiálu 'OLE037 - Rýžový olej LZS' na skladu 'MATERIAL - Sklad Materialu' (požadováno: 717,846000, dostupné: 542,077000).");

        var result = _filter.Transform(ex);

        result.Should().Be("Nedostatek materiálu 'OLE037 - Rýžový olej LZS' na skladu MATERIAL (požadováno: 717,846000, dostupné: 542,077000).");
    }
}
