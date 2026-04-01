namespace Anela.Heblo.Application.Features.Manufacture.ErrorFilters;

public interface IManufactureErrorTransformer
{
    string Transform(Exception exception);
}
