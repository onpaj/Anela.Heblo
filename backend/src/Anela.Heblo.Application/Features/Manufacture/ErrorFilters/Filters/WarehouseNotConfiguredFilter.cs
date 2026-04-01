namespace Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;

public class WarehouseNotConfiguredFilter : IManufactureErrorFilter
{
    public bool CanHandle(Exception exception) =>
        exception.Message.Contains("Pole 'Sklad' musí být vyplněno");

    public string Transform(Exception exception) =>
        "Skladový pohyb nemá nastaven sklad.";
}
