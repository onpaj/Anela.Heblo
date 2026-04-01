namespace Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;

public class ZeroQuantityItemFilter : IManufactureErrorFilter
{
    public bool CanHandle(Exception exception) =>
        exception is ArgumentException &&
        exception.Message.Contains("Item quantity must be greater than zero");

    public string Transform(Exception exception) =>
        "Položka výrobní zakázky má nulové množství.";
}
