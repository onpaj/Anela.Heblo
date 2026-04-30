namespace Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;

public class CannotAllocateFullAmountFilter : IManufactureErrorFilter
{
    public bool CanHandle(Exception exception) =>
        exception.Message.Contains("Cannot allocate full amount for ingredient");

    public string Transform(Exception exception)
    {
        var productCode = ManufactureErrorParsingHelpers.ExtractAfter(
            exception.Message,
            "Cannot allocate full amount for ingredient ",
            ":");

        var remaining = productCode == "?"
            ? "?"
            : ManufactureErrorParsingHelpers.ExtractAfter(
                exception.Message,
                $"{productCode}: ",
                " remaining");

        if (productCode == "?")
            return "Nepodařilo se přidělit dostatečné množství šarží pro jednu z ingrediencí. Zkontrolujte, zda má ingredience ve Flexi evidované šarže.";

        return $"Pro ingredienci {productCode} nelze přidělit dostatečné množství šarží (chybí: {remaining})." +
               " Zkontrolujte, zda má ingredience ve Flexi evidované šarže s dostatečným množstvím.";
    }
}
