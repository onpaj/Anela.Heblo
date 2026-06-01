namespace Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;

public class ReferenceTooLongFilter : IManufactureErrorFilter
{
    public bool CanHandle(Exception exception) =>
        exception.Message.Contains("Číslo došlé") &&
        exception.Message.Contains("40 znaků");

    public string Transform(Exception exception) =>
        "Číslo objednávky je příliš dlouhé pro systém Flexi (max. 40 znaků).";
}
