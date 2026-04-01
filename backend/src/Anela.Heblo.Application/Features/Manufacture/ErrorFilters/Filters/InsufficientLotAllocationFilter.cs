namespace Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;

public class InsufficientLotAllocationFilter : IManufactureErrorFilter
{
    public bool CanHandle(Exception exception) =>
        exception.Message.Contains("Could not allocate sufficient lots");

    public string Transform(Exception exception)
    {
        var ingredientName = ManufactureErrorParsingHelpers.ExtractBetweenQuotes(
            exception.Message,
            "for ingredient '");

        var missing = ManufactureErrorParsingHelpers.ExtractAfter(
            exception.Message,
            "Missing: ",
            null);

        return $"Nedostatek šarží pro ingredienci '{ingredientName}' (chybí: {missing}).";
    }
}
