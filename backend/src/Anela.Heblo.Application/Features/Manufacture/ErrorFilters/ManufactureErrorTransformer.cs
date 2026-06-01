namespace Anela.Heblo.Application.Features.Manufacture.ErrorFilters;

public class ManufactureErrorTransformer : IManufactureErrorTransformer
{
    private const string FallbackMessage = "Při zpracování výroby došlo k neočekávané chybě. Technické detaily: ";
    private readonly IEnumerable<IManufactureErrorFilter> _filters;

    public ManufactureErrorTransformer(IEnumerable<IManufactureErrorFilter> filters)
    {
        ArgumentNullException.ThrowIfNull(filters);
        _filters = filters;
    }

    public string Transform(Exception exception)
    {
        foreach (var filter in _filters)
        {
            if (filter.CanHandle(exception))
                return filter.Transform(exception);
        }

        return $"{FallbackMessage}{exception.Message}";
    }
}
