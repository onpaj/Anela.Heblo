using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Logistics.GiftPackageManufacture;

public interface IGiftPackageManufactureRepository : IRepository<GiftPackageManufactureLog, int>
{
    Task<List<GiftPackageManufactureLog>> GetRecentManufactureLogsAsync(int count = 10, CancellationToken cancellationToken = default);
}