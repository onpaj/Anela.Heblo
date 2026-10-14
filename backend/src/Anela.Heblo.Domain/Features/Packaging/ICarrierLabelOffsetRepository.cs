namespace Anela.Heblo.Domain.Features.Packaging;

public interface ICarrierLabelOffsetRepository
{
    Task<IReadOnlyList<CarrierLabelOffset>> GetAllAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(CarrierLabelOffset offset, CancellationToken cancellationToken = default);
}
