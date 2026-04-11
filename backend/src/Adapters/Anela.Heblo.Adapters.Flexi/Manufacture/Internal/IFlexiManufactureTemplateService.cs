using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Adapters.Flexi.Manufacture.Internal;

internal interface IFlexiManufactureTemplateService
{
    Task<ManufactureTemplate?> GetManufactureTemplateAsync(string productCode, CancellationToken cancellationToken = default);
}
