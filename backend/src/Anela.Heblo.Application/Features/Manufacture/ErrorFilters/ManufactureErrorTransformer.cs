namespace Anela.Heblo.Application.Features.Manufacture.ErrorFilters;

public class ManufactureErrorTransformer : IManufactureErrorTransformer
{
    private readonly IEnumerable<IManufactureErrorFilter> _filters;

    public ManufactureErrorTransformer(IEnumerable<IManufactureErrorFilter> filters)
    {
        _filters = filters;
    }

    public string Transform(Exception exception)
    {
        foreach (var filter in _filters)
        {
            if (filter.CanHandle(exception))
                return filter.Transform(exception);
        }

        return $"Při zpracování výroby došlo k neočekávané chybě. Technické detaily: {exception.Message}";
    }
}
