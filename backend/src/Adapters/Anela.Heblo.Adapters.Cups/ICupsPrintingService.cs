namespace Anela.Heblo.Adapters.Cups;

public interface ICupsPrintingService
{
    Task PrintAsync(string filePath, string? printerName = null, CancellationToken cancellationToken = default);
}
