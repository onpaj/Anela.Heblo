using Anela.Heblo.Application.Features.Manufacture.Infrastructure.Exceptions;

namespace Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;

public class ErpCircuitOpenFilter : IManufactureErrorFilter
{
    public bool CanHandle(Exception exception) =>
        exception is ManufactureErpUnavailableException;

    public string Transform(Exception exception) =>
        "FlexiBee je aktuálně nedostupný nebo neodpovídá včas (opakované chyby). "
        + "Zkuste akci zopakovat za chvíli; pokud problém přetrvává, kontaktujte správce systému.";
}
