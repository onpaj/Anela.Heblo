namespace Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;

public class InsufficientIngredientStockFilter : IManufactureErrorFilter
{
    public bool CanHandle(Exception exception) =>
        exception.Message.Contains("Insufficient stock for ingredients");

    public string Transform(Exception exception)
    {
        var afterPrefix = ManufactureErrorParsingHelpers.ExtractAfter(
            exception.Message,
            "Insufficient stock for ingredients: ",
            null);

        return $"Nedostatečné zásoby pro výrobu. Chybějící ingredience: {afterPrefix}";
    }
}
