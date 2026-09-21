using System.Net;
using Anela.Heblo.Application.Common;
using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockMovement;
using Rem.FlexiBeeSDK.Model.Products.StockMovement;

namespace Anela.Heblo.Adapters.Flexi.Manufacture;

public class FlexiManufactureHistoryClient : IManufactureHistoryClient
{
    private readonly IStockItemsMovementClient _stockItemsMovementClient;
    private readonly ILogger<FlexiManufactureHistoryClient> _logger;
    private readonly IOptions<DataSourceOptions> _options;
    private readonly ResiliencePipeline _pipeline;

    public FlexiManufactureHistoryClient(
        IStockItemsMovementClient stockItemsMovementClient,
        IOptions<DataSourceOptions> options,
        ILogger<FlexiManufactureHistoryClient> logger)
    {
        _stockItemsMovementClient = stockItemsMovementClient;
        _options = options;
        _logger = logger;
        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>(ex =>
                        ex.StatusCode is HttpStatusCode.BadGateway
                                      or HttpStatusCode.ServiceUnavailable
                                      or HttpStatusCode.GatewayTimeout),
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                OnRetry = args =>
                {
                    var statusCode = (args.Outcome.Exception as HttpRequestException)?.StatusCode;
                    _logger.LogWarning(args.Outcome.Exception,
                        "FlexiBee skladovy-pohyb-polozka transient failure {StatusCode}. " +
                        "Retry attempt {Attempt} after {DelayMs} ms.",
                        statusCode, args.AttemptNumber + 1, args.RetryDelay.TotalMilliseconds);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    public async Task<List<ManufactureHistoryRecord>> GetHistoryAsync(DateTime dateFrom, DateTime dateTo, string? productCode = null,
        CancellationToken cancellationToken = default)
    {
        var movements = await FetchMovementsAsync(dateFrom, dateTo, productCode, cancellationToken);

        var query = movements.AsQueryable();

        // Filtrovat podle produktového kódu, pokud je zadán
        if (!string.IsNullOrEmpty(productCode))
        {
            query = query.Where(m => m.ProductCode != null && m.ProductCode.Contains(productCode));
        }

        // Seskupit podle data a produktového kódu a spočítat celkové množství
        var statistics = query
            .Where(m => m.Date != default && !string.IsNullOrEmpty(m.ProductCode))
            .GroupBy(m => new
            {
                Date = m.Date.Date, // Pouze datum bez času
                ProductCode = m.ProductCode!.RemoveCodePrefix()
            })
            .Select(g => new ManufactureHistoryRecord
            {
                Date = g.Key.Date,
                ProductCode = g.Key.ProductCode,
                PricePerPiece = (decimal)g.Average(a => a.PricePerUnit),
                PriceTotal = (decimal)g.Sum(s => s.TotalSum),
                Amount = g.Sum(m => m.Amount)
            })
            .OrderBy(s => s.Date)
            .ThenBy(s => s.ProductCode)
            .ToList();

        return statistics;
    }

    /// <summary>
    /// Fetches the stock movements for every configured manufacture receipt document type
    /// and returns them as one flat list.
    /// </summary>
    private async Task<List<StockItemMovementFlexiDto>> FetchMovementsAsync(
        DateTime dateFrom, DateTime dateTo, string? productCode, CancellationToken cancellationToken)
    {
        // Distinct because ConfigurationBinder merges arrays index-wise instead of replacing
        // them: an environment override of [65, 67] on top of the appsettings [54, 56, 65, 67]
        // binds to [65, 67, 65, 67]. Without this, each duplicated id would be fetched twice
        // and the group-by below would silently double that product's manufactured amount.
        var documentTypeIds = _options.Value.ManufactureDocumentTypeIds.Distinct().ToArray();
        if (documentTypeIds.Length == 0)
        {
            throw new InvalidOperationException(
                "No manufacture document types configured. Set " +
                $"{DataSourceOptions.ConfigKey}:{nameof(DataSourceOptions.ManufactureDocumentTypeIds)} " +
                "to the FlexiBee typ-skladovy-pohyb ids of manufacture receipts (see appsettings.json). " +
                "Failing loudly because an empty set would silently produce no manufacture history.");
        }

        try
        {
            // Fetched in parallel because GetManufactureOutputHandler calls this on the request
            // path, where sequential round trips would multiply the endpoint's latency by the
            // number of configured types. The trade-off is that a failure costs one request per
            // type rather than one overall, which is accepted: the set is small and operator-set.
            var perDocumentType = await Task.WhenAll(documentTypeIds.Select(documentTypeId =>
                _pipeline.ExecuteAsync(
                    async ct => await _stockItemsMovementClient.GetAsync(
                        dateFrom,
                        dateTo,
                        StockMovementDirection.In,
                        documentTypeId: documentTypeId,
                        cancellationToken: ct),
                    cancellationToken).AsTask()));

            return perDocumentType.SelectMany(m => m).ToList();
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "FlexiBee uzivatelsky-dotaz request timed out (internal HttpClient timeout). " +
                "DateFrom: {DateFrom}, DateTo: {DateTo}",
                dateFrom, dateTo);
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "FlexiBee uzivatelsky-dotaz request was canceled by the caller (client abort). " +
                "DateFrom: {DateFrom}, DateTo: {DateTo}",
                dateFrom, dateTo);
            throw;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.BadGateway
                                                             or HttpStatusCode.ServiceUnavailable
                                                             or HttpStatusCode.GatewayTimeout)
        {
            _logger.LogWarning(ex,
                "FlexiBee skladovy-pohyb-polozka returned transient {StatusCode} after retries. " +
                "DateFrom: {DateFrom}, DateTo: {DateTo}, ProductCode: {ProductCode}",
                ex.StatusCode, dateFrom, dateTo, productCode);
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "FlexiBee skladovy-pohyb-polozka returned {StatusCode}. " +
                "DateFrom: {DateFrom}, DateTo: {DateTo}, ProductCode: {ProductCode}",
                ex.StatusCode?.ToString() ?? "unknown", dateFrom, dateTo, productCode);
            throw;
        }
    }
}
