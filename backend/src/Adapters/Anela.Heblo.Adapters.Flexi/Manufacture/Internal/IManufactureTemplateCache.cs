using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Adapters.Flexi.Manufacture.Internal;

internal interface IManufactureTemplateCache
{
    Task<ManufactureTemplate?> GetOrFetchAsync(
        string productCode,
        Func<CancellationToken, Task<ManufactureTemplate?>> fetch,
        CancellationToken cancellationToken);

    void Invalidate(string productCode);
}
