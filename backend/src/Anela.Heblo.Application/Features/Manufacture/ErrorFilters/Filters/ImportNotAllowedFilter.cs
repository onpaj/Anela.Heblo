namespace Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;

public class ImportNotAllowedFilter : IManufactureErrorFilter
{
    public bool CanHandle(Exception exception) =>
        exception.Message.Contains("importNotAllowed") ||
        exception.Message.Contains("Import není povolen");

    public string Transform(Exception exception) =>
        "FlexiBee odmítl vytvoření skladového dokladu (import není povolen). Doklad nebyl vytvořen. "
        + "Zkuste akci zopakovat; pokud problém přetrvává, kontaktujte správce systému.";
}
