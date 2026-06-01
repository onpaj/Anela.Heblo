using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpIpp;
using SharpIpp.Models.Requests;
using SharpIpp.Protocol.Models;

namespace Anela.Heblo.Adapters.Cups;

public class CupsPrintingService : ICupsPrintingService
{
    private readonly ISharpIppClient _sharpIppClient;
    private readonly IOptions<CupsOptions> _options;
    private readonly ILogger<CupsPrintingService> _logger;

    public CupsPrintingService(
        ISharpIppClient sharpIppClient,
        IOptions<CupsOptions> options,
        ILogger<CupsPrintingService> logger)
    {
        _sharpIppClient = sharpIppClient;
        _options = options;
        _logger = logger;
    }

    public async Task PrintAsync(string filePath, string? printerName = null, CancellationToken cancellationToken = default)
    {
        var opts = _options.Value;

        if (string.IsNullOrWhiteSpace(opts.ServerUrl))
            throw new InvalidOperationException("CupsOptions.ServerUrl is not configured.");

        var resolvedPrinter = string.IsNullOrWhiteSpace(printerName)
            ? opts.PrinterName
            : printerName;

        if (string.IsNullOrWhiteSpace(resolvedPrinter))
            throw new InvalidOperationException(
                "No printer name provided and CupsOptions.PrinterName is not configured.");

        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path must not be null or empty.", nameof(filePath));

        using var fileStream = File.OpenRead(filePath);

        var request = new PrintJobRequest
        {
            Document = fileStream,
            OperationAttributes = new PrintJobOperationAttributes
            {
                PrinterUri = new Uri($"{opts.ServerUrl.TrimEnd('/')}/printers/{resolvedPrinter}"),
                DocumentFormat = "application/pdf"
            }
        };

        var response = await _sharpIppClient.PrintJobAsync(request, cancellationToken);

        if (response.StatusCode != IppStatusCode.SuccessfulOk)
            throw new InvalidOperationException(
                $"CUPS print job failed with status: {response.StatusCode}");

        _logger.LogDebug("CUPS print job submitted. JobId: {JobId}, Printer: {Printer}",
            response.JobAttributes?.JobId, resolvedPrinter);
    }
}
