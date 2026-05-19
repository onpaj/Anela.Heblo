using Anela.Heblo.Application.Features.MarketingInvoices.Services;
using Anela.Heblo.Domain.Features.MarketingInvoices;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MarketingInvoices.UseCases.ImportMarketingInvoices;

public class ImportMarketingInvoicesHandler
    : IRequestHandler<ImportMarketingInvoicesRequest, ImportMarketingInvoicesResponse>
{
    private readonly IEnumerable<IMarketingTransactionSource> _sources;
    private readonly MarketingInvoiceImportService _importService;
    private readonly ILogger<ImportMarketingInvoicesHandler> _logger;

    public ImportMarketingInvoicesHandler(
        IEnumerable<IMarketingTransactionSource> sources,
        MarketingInvoiceImportService importService,
        ILogger<ImportMarketingInvoicesHandler> logger)
    {
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _importService = importService ?? throw new ArgumentNullException(nameof(importService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ImportMarketingInvoicesResponse> Handle(
        ImportMarketingInvoicesRequest request,
        CancellationToken cancellationToken)
    {
        var matches = _sources.Where(s => s.Platform == request.Platform).ToList();

        if (matches.Count == 0)
        {
            throw new ArgumentException(
                $"No marketing transaction source is registered for platform '{request.Platform}'.",
                nameof(request));
        }

        if (matches.Count > 1)
        {
            throw new InvalidOperationException(
                $"Multiple marketing transaction sources are registered for platform '{request.Platform}'.");
        }

        var source = matches[0];

        _logger.LogInformation(
            "Importing marketing invoices for {Platform} from {From:yyyy-MM-dd} to {To:yyyy-MM-dd}",
            request.Platform, request.From, request.To);

        // Import-time exceptions are intentionally NOT caught here — they must
        // propagate to the job's catch-log-rethrow so Hangfire can retry.
        var result = await _importService.ImportAsync(source, request.From, request.To, cancellationToken);

        return new ImportMarketingInvoicesResponse
        {
            Platform = request.Platform,
            Imported = result.Imported,
            Skipped = result.Skipped,
            Failed = result.Failed,
        };
    }
}
