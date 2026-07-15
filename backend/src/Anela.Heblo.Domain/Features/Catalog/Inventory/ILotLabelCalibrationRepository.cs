namespace Anela.Heblo.Domain.Features.Catalog.Inventory;

public interface ILotLabelCalibrationRepository
{
    Task<LotLabelCalibration> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(LotLabelCalibration calibration, CancellationToken cancellationToken = default);
}
