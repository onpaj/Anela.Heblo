using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public interface IManufactureInventoryWriteDownService
{
    Task WriteDownAsync(ManufactureOrder order, string changedByUser, CancellationToken cancellationToken);
}
