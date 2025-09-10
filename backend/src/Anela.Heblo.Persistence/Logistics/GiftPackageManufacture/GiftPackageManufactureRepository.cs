using Anela.Heblo.Domain.Features.Logistics.GiftPackageManufacture;
using Anela.Heblo.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Logistics.GiftPackageManufacture;

public class GiftPackageManufactureRepository : BaseRepository<GiftPackageManufactureLog, int>, IGiftPackageManufactureRepository
{
    public GiftPackageManufactureRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<List<GiftPackageManufactureLog>> GetRecentManufactureLogsAsync(int count = 10, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(x => x.ConsumedItems)
            .OrderByDescending(x => x.CreatedAt)
            .Take(count)
            .ToListAsync(cancellationToken);
    }
}