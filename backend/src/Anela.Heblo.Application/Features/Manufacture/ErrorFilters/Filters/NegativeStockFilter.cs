namespace Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;

public class NegativeStockFilter : IManufactureErrorFilter
{
    public bool CanHandle(Exception exception) =>
        exception.Message.Contains("Není povolen záporný stav skladu");

    public string Transform(Exception exception) =>
        "Operace by způsobila záporný stav skladu.";
}
