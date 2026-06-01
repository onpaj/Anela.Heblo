namespace Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;

public class ProductCodeNotFoundFilter : IManufactureErrorFilter
{
    public bool CanHandle(Exception exception) =>
        exception.Message.Contains("musí identifikovat objekt");

    public string Transform(Exception exception)
    {
        var code = ManufactureErrorParsingHelpers.ExtractBetweenQuotes(
            exception.Message,
            "code:");

        return $"Produkt s kódem '{code}' nebyl nalezen v systému Flexi.";
    }
}
