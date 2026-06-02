namespace Anela.Heblo.Adapters.Cups;

public interface ICupsPrintingService
{
    Task PrintAsync(string filePath, string? printerName = null, string documentFormat = "application/pdf", CancellationToken cancellationToken = default);
}
