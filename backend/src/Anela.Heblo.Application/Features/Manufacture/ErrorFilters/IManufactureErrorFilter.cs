namespace Anela.Heblo.Application.Features.Manufacture.ErrorFilters;

public interface IManufactureErrorFilter
{
    bool CanHandle(Exception exception);
    string Transform(Exception exception);
}
