namespace Anela.Heblo.Application.Features.Ecomail.Services;

public interface IEcomailSyncService
{
    Task<EcomailSyncReport> SyncAllAsync(CancellationToken cancellationToken = default);
}
