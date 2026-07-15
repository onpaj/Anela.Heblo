using Anela.Heblo.Application.Shared.Printing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Cups;

public class CupsLabelPrintingService : ILabelPrintingService
{
    private readonly ICupsPrintingService _cups;
    private readonly IOptions<CupsOptions> _options;
    private readonly ILogger<CupsLabelPrintingService> _logger;

    public CupsLabelPrintingService(
        ICupsPrintingService cups, IOptions<CupsOptions> options, ILogger<CupsLabelPrintingService> logger)
    {
        _cups = cups;
        _options = options;
        _logger = logger;
    }

    public async Task PrintZplAsync(string zpl, CancellationToken cancellationToken = default)
    {
        var printer = _options.Value.LabelPrinterName;
        if (string.IsNullOrWhiteSpace(printer))
            throw new InvalidOperationException("CupsOptions.LabelPrinterName is not configured.");

        var tempPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempPath, zpl, cancellationToken);
            // Send as raw so CUPS passes the ZPL straight to the printer without invoking a
            // driver/filter chain. With "application/octet-stream" CUPS auto-types the ASCII
            // ZPL as text and a driver-based queue rasterizes the source as printed text
            // instead of interpreting it. "application/vnd.cups-raw" forces passthrough and
            // works with both raw and driver-based queues.
            await _cups.PrintAsync(tempPath, printer, "application/vnd.cups-raw", cancellationToken);
            _logger.LogInformation("Sent ZPL label batch to printer {Printer}", printer);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
