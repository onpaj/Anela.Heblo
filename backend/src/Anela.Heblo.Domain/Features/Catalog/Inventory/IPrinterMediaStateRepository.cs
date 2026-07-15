namespace Anela.Heblo.Domain.Features.Catalog.Inventory;

public interface IPrinterMediaStateRepository
{
    Task<PrinterMediaState> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(PrinterMediaState state, CancellationToken cancellationToken = default);
}
