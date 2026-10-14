using Anela.Heblo.Domain.Features.Packaging;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Repositories.Packaging;

public class CarrierLabelOffsetRepository : ICarrierLabelOffsetRepository
{
    private readonly ApplicationDbContext _context;

    public CarrierLabelOffsetRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<CarrierLabelOffset>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.CarrierLabelOffsets.AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task UpsertAsync(CarrierLabelOffset offset, CancellationToken cancellationToken = default)
    {
        var existing = await _context.CarrierLabelOffsets
            .FirstOrDefaultAsync(o => o.Carrier == offset.Carrier, cancellationToken);

        if (existing is null)
        {
            _context.CarrierLabelOffsets.Add(offset);
        }
        else
        {
            existing.UpdateOffset(offset.OffsetXMm, offset.OffsetYMm, offset.ModifiedBy);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
