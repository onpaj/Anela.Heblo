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

    // A run of identical failures (401/403/429, Shoptet down) is systemic: stop instead of burning the
    // shared rate budget on every remaining order.
    private const int MaxConsecutiveFailures = 5;

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

        if (_settings.MissingInvoiceMaxPerRun <= 0)
            throw new InvalidOperationException(
                $"CreateMissingInvoices: ShoptetOrders:MissingInvoiceMaxPerRun must be positive, got {_settings.MissingInvoiceMaxPerRun}.");

        if (missing.Count > _settings.MissingInvoiceMaxPerRun)
        {
            _logger.LogWarning(
                "CreateMissingInvoices: {Missing} order(s) need an invoice, capped at {Max} this run; the rest follow next run.",
                missing.Count, _settings.MissingInvoiceMaxPerRun);
            missing = missing.Take(_settings.MissingInvoiceMaxPerRun).ToList();
        }

        var outcome = await CreateInvoicesAsync(missing, cancellationToken);

        _logger.LogInformation(
            "CreateMissingInvoices: created {Created} of {Missing} invoice(s), {Skipped} already invoiced meanwhile, {WithoutBillingMethod} without a billing method, {Failed} failed (retried next run).",
            outcome.Created, missing.Count, outcome.Skipped, outcome.WithoutBillingMethod.Count, outcome.Failures.Count);

        if (outcome.WithoutBillingMethod.Count > 0)
            _logger.LogWarning(
                "CreateMissingInvoices: {Count} order(s) have no billing method, so Shoptet cannot invoice them through the API; set a payment method and invoice them by hand in Shoptet: {OrderCodes}.",
                outcome.WithoutBillingMethod.Count, string.Join(", ", outcome.WithoutBillingMethod));

        // Surface failures as a failed Hangfire run — a warning log alone hides an order failing every night.
        if (outcome.Failures.Count > 0)
            throw new InvalidOperationException(
                $"CreateMissingInvoices: {outcome.Failures.Count} of {missing.Count} invoice(s) failed (retried next run): {DescribeFailures(outcome.Failures)}");
    }

    private async Task<CreationOutcome> CreateInvoicesAsync(IReadOnlyList<EshopOrderSummary> orders, CancellationToken cancellationToken)
    {
        var outcome = new CreationOutcome(0, 0, [], []);
        var consecutiveFailures = 0;
        for (var i = 0; i < orders.Count; i++)
        {
            var order = orders[i];
            if (i > 0 && _settings.MissingInvoiceCreationDelayMs > 0)
                await Task.Delay(_settings.MissingInvoiceCreationDelayMs, cancellationToken);

            try
            {
                outcome = await CreateInvoiceAsync(order, outcome, cancellationToken);
                consecutiveFailures = 0;
            }
            // Filter on the job's own token: an HTTP timeout is also an OperationCanceledException
            // and must only skip this order, not abort the run.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                outcome = outcome with { Failures = [.. outcome.Failures, new FailedOrder(order.Code, ex.Message)] };
                _logger.LogWarning(ex,
                    "CreateMissingInvoices: failed to create an invoice for order {OrderCode}: {Error}. Will retry next run.",
                    order.Code, ex.Message);

                if (++consecutiveFailures >= MaxConsecutiveFailures)
                    throw new InvalidOperationException(
                        $"CreateMissingInvoices: {consecutiveFailures} consecutive invoice failures — aborting the run after {outcome.Created} created: {DescribeFailures(outcome.Failures)}",
                        ex);
            }
        }

        return outcome;
    }

    private async Task<CreationOutcome> CreateInvoiceAsync(
        EshopOrderSummary order, CreationOutcome outcome, CancellationToken cancellationToken)
    {
        // Re-check per order: the bulk listing is minutes old by now and an invoice may have been
        // issued by hand meanwhile — invoices cannot be deleted through the API.
        if (await _orderClient.HasInvoiceAsync(order.Code, cancellationToken))
        {
            _logger.LogInformation(
                "CreateMissingInvoices: order {OrderCode} already has an invoice, skipping.", order.Code);
            return outcome with { Skipped = outcome.Skipped + 1 };
        }

        // Shoptet invoices with the order's billing method; an order keyed in the admin without one is
        // rejected with 422 "Unknown billingMethodId provided". Picking a payment method is a human call.
        var detail = await _orderClient.GetOrderDetailAsync(order.Code, cancellationToken)
            ?? throw new InvalidOperationException($"Shoptet order {order.Code} no longer exists.");
        if (!detail.HasBillingMethod)
        {
            _logger.LogInformation(
                "CreateMissingInvoices: order {OrderCode} has no billing method, skipping.",
                order.Code);
            return outcome with { WithoutBillingMethod = [.. outcome.WithoutBillingMethod, order.Code] };
        }

        await _orderClient.CreateInvoiceAsync(order.Code, cancellationToken);
        _logger.LogInformation(
            "CreateMissingInvoices: invoice created for order {OrderCode} (total {PriceWithVat}).",
            order.Code, order.PriceWithVat);
        return outcome with { Created = outcome.Created + 1 };
    }

    private static string DescribeFailures(IEnumerable<FailedOrder> failures) =>
        string.Join("; ", failures.Select(f => $"order {f.OrderCode}: {f.Error}"));

    private sealed record FailedOrder(string OrderCode, string Error);

    private sealed record CreationOutcome(
        int Created, int Skipped, IReadOnlyList<string> WithoutBillingMethod, IReadOnlyList<FailedOrder> Failures);

    private static bool HasPrice(EshopOrderSummary order) => order.PriceWithVat is > 0m;
}
