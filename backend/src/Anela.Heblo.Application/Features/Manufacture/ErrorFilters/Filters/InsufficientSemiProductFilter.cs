namespace Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;

public class InsufficientSemiProductFilter : IManufactureErrorFilter
{
    public bool CanHandle(Exception exception) =>
        exception.Message.Contains("Nelze vytvořit příjemku výrobku") &&
        exception.Message.Contains("POLOTOVARY");

    public string Transform(Exception exception)
    {
        var message = exception.Message;
        var materialName = ManufactureErrorParsingHelpers.ExtractBetweenQuotes(message, "materiálu '");
        var (required, available) = ManufactureErrorParsingHelpers.ExtractQuantities(message);

        return $"Nedostatek meziproduktu '{materialName}' na skladu POLOTOVARY (požadováno: {required}, dostupné: {available}).";
    }
}
