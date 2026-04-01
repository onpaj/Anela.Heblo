namespace Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;

public class InsufficientRawMaterialFilter : IManufactureErrorFilter
{
    public bool CanHandle(Exception exception) =>
        exception.Message.Contains("Nelze vytvořit příjemku výrobku") &&
        exception.Message.Contains("MATERIAL - Sklad Materialu");

    public string Transform(Exception exception)
    {
        var message = exception.Message;
        var materialName = ManufactureErrorParsingHelpers.ExtractBetweenQuotes(message, "materiálu '");
        var (required, available) = ManufactureErrorParsingHelpers.ExtractQuantities(message);

        return $"Nedostatek materiálu '{materialName}' na skladu MATERIAL (požadováno: {required}, dostupné: {available}).";
    }
}
