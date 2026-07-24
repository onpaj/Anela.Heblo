using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public interface IManufactureConditionsCaptureService
{
    Task<ManufactureOrderConditionsReading> CaptureAsync(
        ManufactureOrder order,
        ManufactureOrderState stage,
        CancellationToken cancellationToken);
}
