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
        return await _context.LotLabelCalibrations.FirstOrDefaultAsync(cancellationToken)
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
            existing.Update(calibration.PitchDots, calibration.DriftEveryNLabels, calibration.ModifiedBy ?? string.Empty);
        }
        await _context.SaveChangesAsync(cancellationToken);
    }
}
