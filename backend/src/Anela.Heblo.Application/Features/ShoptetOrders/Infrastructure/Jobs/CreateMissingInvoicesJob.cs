using Anela.Heblo.Application.Features.FeatureFlags;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ShoptetOrders.Infrastructure.Jobs;

public sealed class CreateMissingInvoicesJob : IRecurringJob
{
    // Invoices are listed from a month before the order start date: an invoice can never predate its
    // order, so the margin only guards against a hand-backdated invoice being missed and duplicated.
    private static readonly TimeSpan InvoiceListingMargin = TimeSpan.FromDays(31);

    private readonly IEshopOrderClient _orderClient;
    private readonly ShoptetOrdersSettings _settings;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly IFeatureFlagChecker _featureFlags;
    private readonly ILogger<CreateMissingInvoicesJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "create-missing-invoices",
        Category = RecurringJobCategory.Warehouse,
        DisplayName = "Create Missing Invoices",
        Description = "Creates a Shoptet invoice for every completed ('vyřízena') order with a non-zero total that has none.",
        CronExpression = "0 3 * * *",
        DefaultIsEnabled = true,
    };

    public CreateMissingInvoicesJob(
        IEshopOrderClient orderClient,
        IOptions<ShoptetOrdersSettings> settings,
        IRecurringJobStatusChecker statusChecker,
        IFeatureFlagChecker featureFlags,
        ILogger<CreateMissingInvoicesJob> logger)
    {
        _orderClient = orderClient;
        _settings = settings.Value;
        _statusChecker = statusChecker;
        _featureFlags = featureFlags;
        _logger = logger;
    }

    private const int ConcurrencyLockTimeoutSeconds = 60;

    // The daily cron is the retry: a failed run recomputes the work list from scratch next night.
    // A manual trigger overlapping the scheduled run would read the same work list and invoice twice.
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(ConcurrencyLockTimeoutSeconds)]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }

        var applyChanges = await _featureFlags.IsEnabledAsync(FeatureFlagKeys.MissingInvoiceCreation, cancellationToken);
        if (!applyChanges)
        {
            _logger.LogInformation(
                "CreateMissingInvoices: running in DRY RUN mode (feature flag '{Flag}' disabled) — orders are logged, no invoice is created.",
                FeatureFlagKeys.MissingInvoiceCreation);
        }

        var createdFrom = _settings.MissingInvoiceOrdersCreatedFrom;
        var orders = await _orderClient.ListOrdersByStatusAsync(_settings.CompletedStatusId, createdFrom, cancellationToken);
        // Deliberately not caught: without the invoiced set every order would look uninvoiced.
        var invoiced = await _orderClient.ListInvoicedOrderCodesAsync(createdFrom - InvoiceListingMargin, cancellationToken);

        if (invoiced.Count == 0 && orders.Any(HasPrice))
        {
            // Every priced order looking uninvoiced means the invoice listing came back empty or
            // malformed, not that nothing was ever invoiced — invoicing now would duplicate.
            throw new InvalidOperationException(
                "CreateMissingInvoices: the Shoptet invoice listing returned no invoices while priced completed orders exist. Refusing to create invoices.");
        }

        var zeroPrice = orders.Count(o => !HasPrice(o));
        var alreadyInvoiced = orders.Count(o => HasPrice(o) && invoiced.Contains(o.Code));
        var missing = orders.Where(o => HasPrice(o) && !invoiced.Contains(o.Code)).ToList();

        _logger.LogInformation(
            "CreateMissingInvoices: {Scanned} order(s) in state {StateId} created since {CreatedFrom:yyyy-MM-dd} — {ZeroPrice} with zero total, {AlreadyInvoiced} already invoiced, {Missing} missing an invoice: {OrderCodes}.",
            orders.Count, _settings.CompletedStatusId, createdFrom, zeroPrice, alreadyInvoiced, missing.Count,
            string.Join(", ", missing.Select(o => o.Code)));

        if (!applyChanges)
            return;

        if (missing.Count > _settings.MissingInvoiceMaxPerRun)
        {
            _logger.LogWarning(
                "CreateMissingInvoices: {Missing} order(s) need an invoice, capped at {Max} this run; the rest follow next run.",
                missing.Count, _settings.MissingInvoiceMaxPerRun);
            missing = missing.Take(_settings.MissingInvoiceMaxPerRun).ToList();
        }

        var created = await CreateInvoicesAsync(missing, cancellationToken);

        _logger.LogInformation(
            "CreateMissingInvoices: created {Created} of {Missing} invoice(s), {Failed} failed (retried next run).",
            created, missing.Count, missing.Count - created);
    }

    private async Task<int> CreateInvoicesAsync(IReadOnlyList<EshopOrderSummary> orders, CancellationToken cancellationToken)
    {
        var created = 0;
        for (var i = 0; i < orders.Count; i++)
        {
            var order = orders[i];
            if (i > 0 && _settings.MissingInvoiceCreationDelayMs > 0)
                await Task.Delay(_settings.MissingInvoiceCreationDelayMs, cancellationToken);

            try
            {
                await _orderClient.CreateInvoiceAsync(order.Code, cancellationToken);
                created++;
                _logger.LogInformation(
                    "CreateMissingInvoices: invoice created for order {OrderCode} (total {PriceWithVat}).",
                    order.Code, order.PriceWithVat);
            }
            // Filter on the job's own token: an HTTP timeout is also an OperationCanceledException
            // and must only skip this order, not abort the run.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex,
                    "CreateMissingInvoices: failed to create an invoice for order {OrderCode}. Will retry next run.",
                    order.Code);
            }
        }

        return created;
    }

    private static bool HasPrice(EshopOrderSummary order) => order.PriceWithVat is > 0m;
}
