using Anela.Heblo.Domain.Features.Logistics;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Logistics.CarrierCooling;

public class CarrierCoolingRepository : ICarrierCoolingRepository
{
    private readonly ApplicationDbContext _context;

    public CarrierCoolingRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<CarrierCoolingSetting>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.CarrierCoolingSettings.ToListAsync(cancellationToken);
    }

    public async Task UpsertAsync(CarrierCoolingSetting setting, CancellationToken cancellationToken = default)
    {
        var existing = await _context.CarrierCoolingSettings
            .FirstOrDefaultAsync(
                s => s.Carrier == setting.Carrier && s.DeliveryHandling == setting.DeliveryHandling,
                cancellationToken);

        if (existing is null)
        {
            _context.CarrierCoolingSettings.Add(setting);
        }
        else
        {
            existing.UpdateCooling(setting.Cooling, setting.ModifiedBy);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
