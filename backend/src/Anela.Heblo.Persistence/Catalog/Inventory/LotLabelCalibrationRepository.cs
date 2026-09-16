using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Catalog.Inventory;

public sealed class LotLabelCalibrationRepository : ILotLabelCalibrationRepository
{
    private readonly ApplicationDbContext _context;

    public LotLabelCalibrationRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<LotLabelCalibration> GetAsync(CancellationToken cancellationToken = default)
    {
        // Detached on purpose: SaveAsync re-reads the row and mutates the tracked entity in
        // place, so a tracked read would hand callers an instance that silently changes
        // under them on the next save. Callers that derive a new value from the current one
        // (the nudge wizard, which logs both) need a stable snapshot, and every caller of
        // this method only reads.
        return await _context.LotLabelCalibrations.AsNoTracking().FirstOrDefaultAsync(cancellationToken)
            ?? LotLabelCalibration.CreateDefault();
    }

    public async Task SaveAsync(LotLabelCalibration calibration, CancellationToken cancellationToken = default)
    {
        var existing = await _context.LotLabelCalibrations.FirstOrDefaultAsync(cancellationToken);
        if (existing is null)
        {
            _context.LotLabelCalibrations.Add(calibration);
        }
        else
        {
            existing.Update(calibration.PitchDots, calibration.DriftDotsPer100Labels, calibration.ModifiedBy ?? string.Empty);
        }
        await _context.SaveChangesAsync(cancellationToken);
    }
}
