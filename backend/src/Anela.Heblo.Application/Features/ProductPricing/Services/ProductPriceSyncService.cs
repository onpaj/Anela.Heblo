using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.ProductPricing;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.ProductPricing.Services;

public class ProductPriceSyncService : IProductPriceSyncService
{
    private const string SeedModifiedBy = "price-sync";

    /// <summary>Assumption A3: only sellable types carry a retail price.</summary>
    private static readonly ProductType[] PricedProductTypes =
    {
        ProductType.Product, ProductType.Goods, ProductType.Set,
    };

    private readonly IProductPriceRepository _repository;
    private readonly IEshopPriceListClient _eshopClient;
    private readonly IErpPriceWriter _erpWriter;
    private readonly IProductPriceErpClient _erpReader;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILogger<ProductPriceSyncService> _logger;

    public ProductPriceSyncService(
        IProductPriceRepository repository,
        IEshopPriceListClient eshopClient,
        IErpPriceWriter erpWriter,
        IProductPriceErpClient erpReader,
        ICatalogRepository catalogRepository,
        ILogger<ProductPriceSyncService> logger)
    {
        _repository = repository;
        _eshopClient = eshopClient;
        _erpWriter = erpWriter;
        _erpReader = erpReader;
        _catalogRepository = catalogRepository;
        _logger = logger;
    }

    public async Task<PriceSyncRunResult> SyncAsync(CancellationToken ct)
    {
        var result = new PriceSyncRunResult();

        // Our own master table and the in-scope product list are inputs we must trust to
        // reconcile anything at all; if either read fails, there is nothing safe to compare
        // against, so letting the exception abort the whole run is the correct behavior here
        // (unlike a single target's remote-price read, which is isolated below).
        var prices = (await _repository.GetAllAsync(ct)).ToDictionary(p => p.ProductCode, StringComparer.OrdinalIgnoreCase);
        var inScope = await ReadInScopeProductCodesAsync(ct);

        var (erpPrices, erpAvailable) = await TryReadErpPricesAsync(ct);

        var context = new PriceSyncContext
        {
            Prices = prices,
            ErpPrices = erpPrices,
            InScopeProductCodes = inScope,
            ErpAvailable = erpAvailable,
            Result = result,
        };

        await SyncTargetAsync(PriceSyncTarget.Shoptet, context, ct);

        if (erpAvailable)
        {
            await SyncTargetAsync(PriceSyncTarget.Flexi, context, ct);
        }
        else
        {
            _logger.LogWarning("Price sync skipped for Flexi: ERP price read failed");
        }

        await _repository.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Price sync finished: {Pushed} pushed, {Conflicts} conflicts, {Failed} failed, {Seeded} seeded, {Unchanged} unchanged",
            result.Pushed, result.Conflicts, result.Failed, result.Seeded, result.Unchanged);

        return result;
    }

    private async Task<IReadOnlySet<string>> ReadInScopeProductCodesAsync(CancellationToken ct)
    {
        var catalog = await _catalogRepository.GetAllAsync(ct);

        return catalog
            .Where(p => PricedProductTypes.Contains(p.Type))
            .Select(p => p.ProductCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<(IReadOnlyDictionary<string, ProductPriceErp> ErpPrices, bool Available)> TryReadErpPricesAsync(CancellationToken ct)
    {
        try
        {
            var erpPrices = await _erpReader.GetAllAsync(forceReload: false, ct);
            var byCode = erpPrices
                .Where(p => !string.IsNullOrWhiteSpace(p.ProductCode))
                .GroupBy(p => p.ProductCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            return (byCode, true);
        }
        catch (Exception ex)
        {
            // Flexi's prices come entirely from this read, and seeding needs its VAT rate,
            // so a failure here is handled by the caller: skip Flexi outright and defer any
            // Shoptet seed rather than guess a VAT rate.
            _logger.LogError(ex, "Price sync: ERP price read failed");
            return (new Dictionary<string, ProductPriceErp>(StringComparer.OrdinalIgnoreCase), false);
        }
    }

    private async Task SyncTargetAsync(PriceSyncTarget target, PriceSyncContext context, CancellationToken ct)
    {
        IReadOnlyDictionary<string, decimal> remotePrices;
        try
        {
            remotePrices = target == PriceSyncTarget.Shoptet
                ? await _eshopClient.GetPricesWithVatAsync(ct)
                : context.ErpPrices.ToDictionary(e => e.Key, e => e.Value.PriceWithVat, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            // A failed bulk read tells us nothing about individual products. Leave every
            // state untouched rather than mass-marking them Failed.
            _logger.LogError(ex, "Price sync skipped for {Target}: bulk read failed", target);
            return;
        }

        var states = (await _repository.GetSyncStatesAsync(target, ct))
            .ToDictionary(s => s.ProductCode, StringComparer.OrdinalIgnoreCase);

        // Materials and semi-products have no selling price and are never synced (A3).
        var productCodes = context.Prices.Keys
            .Union(remotePrices.Keys, StringComparer.OrdinalIgnoreCase)
            .Where(context.InScopeProductCodes.Contains)
            .ToList();

        foreach (var productCode in productCodes)
        {
            ct.ThrowIfCancellationRequested();

            states.TryGetValue(productCode, out var state);
            state ??= new ProductPriceSyncState { ProductCode = productCode, Target = target };
            state.Target = target;

            context.Prices.TryGetValue(productCode, out var hebloPrice);

            // A sync state that has already been pushed but has no matching master row
            // would otherwise fall back to a 0m Heblo price below and could compute a
            // Push(0) — a real zero price hitting production. Refuse instead.
            if (hebloPrice is null && state.LastPushedPriceWithVat is not null)
            {
                context.Result.Failed++;
                await FailAsync(state, $"No master price row for {productCode}; refusing to push.", ct);
                continue;
            }

            remotePrices.TryGetValue(productCode, out var remoteValue);
            var remote = remotePrices.ContainsKey(productCode) ? remoteValue : (decimal?)null;

            var decision = PriceSyncDecider.Decide(
                hebloPrice?.PriceWithVat ?? 0m, state.LastPushedPriceWithVat, remote);

            await ApplyDecisionAsync(decision, target, productCode, hebloPrice, context, state, ct);
        }
    }

    private async Task ApplyDecisionAsync(
        PriceSyncDecision decision,
        PriceSyncTarget target,
        string productCode,
        ProductPrice? hebloPrice,
        PriceSyncContext context,
        ProductPriceSyncState state,
        CancellationToken ct)
    {
        switch (decision.Action)
        {
            case PriceSyncAction.None:
                context.Result.Unchanged++;
                return;

            case PriceSyncAction.MissingRemote:
                context.Result.Failed++;
                await FailAsync(state, $"Product {productCode} does not exist in {target}.", ct);
                return;

            case PriceSyncAction.Seed:
                await SeedAsync(decision, target, productCode, hebloPrice, context, state, ct);
                return;

            case PriceSyncAction.Conflict:
                context.Result.Conflicts++;
                state.Status = PriceSyncStatus.Conflict;
                state.RemoteValueAtConflict = decision.RemoteValue;
                state.ConflictDetectedAt = DateTime.UtcNow;
                state.LastError = null;
                await _repository.UpsertSyncStateAsync(state, ct);
                return;

            case PriceSyncAction.Push:
                await PushAsync(decision, target, productCode, hebloPrice, context, state, ct);
                return;
        }
    }

    private async Task SeedAsync(
        PriceSyncDecision decision,
        PriceSyncTarget target,
        string productCode,
        ProductPrice? hebloPrice,
        PriceSyncContext context,
        ProductPriceSyncState state,
        CancellationToken ct)
    {
        // Shoptet is today's retail truth, so it seeds the master value. Flexi only ever
        // adopts the seed when it already agrees; otherwise it becomes a conflict for a
        // human to reconcile.
        if (target == PriceSyncTarget.Shoptet)
        {
            await SeedFromEshopAsync(decision, productCode, context, state, ct);
            return;
        }

        await ReconcileErpSeedAsync(decision, hebloPrice, context, state, ct);
    }

    private async Task SeedFromEshopAsync(
        PriceSyncDecision decision,
        string productCode,
        PriceSyncContext context,
        ProductPriceSyncState state,
        CancellationToken ct)
    {
        if (!context.ErpAvailable)
        {
            // Seeding needs the ERP VAT rate; guessing the standard rate would persist a
            // wrong rate for a reduced-VAT product. Leave the state untouched and retry
            // the seed on the next run.
            _logger.LogWarning("Price sync: deferring seed for {ProductCode}; ERP price read failed", productCode);
            return;
        }

        context.Result.Seeded++;
        context.ErpPrices.TryGetValue(productCode, out var erp);

        var seeded = new ProductPrice
        {
            ProductCode = productCode,
            PriceWithVat = decision.RemoteValue!.Value,
            VatRate = DeriveVatRate(erp),
            ModifiedAt = DateTime.UtcNow,
            ModifiedBy = SeedModifiedBy,
        };

        await _repository.UpsertAsync(seeded, ct);

        // Shoptet is synced first, so the seeded master value must be visible to the
        // Flexi pass in this same run — otherwise Flexi would silently adopt its own
        // value instead of raising the reconciliation conflict.
        context.Prices[productCode] = seeded;

        state.LastPushedPriceWithVat = decision.RemoteValue;
        state.LastPushedAt = DateTime.UtcNow;
        state.Status = PriceSyncStatus.InSync;
        await _repository.UpsertSyncStateAsync(state, ct);
    }

    private async Task ReconcileErpSeedAsync(
        PriceSyncDecision decision,
        ProductPrice? hebloPrice,
        PriceSyncContext context,
        ProductPriceSyncState state,
        CancellationToken ct)
    {
        var seededPrice = hebloPrice?.PriceWithVat;
        if (seededPrice is null || Math.Round(seededPrice.Value, 2) == Math.Round(decision.RemoteValue!.Value, 2))
        {
            state.LastPushedPriceWithVat = decision.RemoteValue;
            state.LastPushedAt = DateTime.UtcNow;
            state.Status = PriceSyncStatus.InSync;
            await _repository.UpsertSyncStateAsync(state, ct);
            return;
        }

        context.Result.Conflicts++;
        state.Status = PriceSyncStatus.Conflict;
        state.RemoteValueAtConflict = decision.RemoteValue;
        state.ConflictDetectedAt = DateTime.UtcNow;
        await _repository.UpsertSyncStateAsync(state, ct);
    }

    private async Task PushAsync(
        PriceSyncDecision decision,
        PriceSyncTarget target,
        string productCode,
        ProductPrice? hebloPrice,
        PriceSyncContext context,
        ProductPriceSyncState state,
        CancellationToken ct)
    {
        if (decision.PriceToPush is not > 0m)
        {
            // Belt and braces alongside the missing-master-row guard above: never let a
            // non-positive value reach a live price write. Shoptet treats a literal 0 as a
            // genuine free price, not "clear", and there is no runtime zero-guard downstream.
            context.Result.Failed++;
            await FailAsync(state, $"Refusing to push a non-positive price for {productCode}.", ct);
            return;
        }

        try
        {
            if (target == PriceSyncTarget.Shoptet)
            {
                await _eshopClient.SetPriceWithVatAsync(productCode, decision.PriceToPush!.Value, ct);
            }
            else
            {
                if (!context.ErpPrices.TryGetValue(productCode, out var erp) || erp.ErpItemId <= 0)
                {
                    context.Result.Failed++;
                    await FailAsync(
                        state,
                        $"No Flexi ceník id known for {productCode}; refusing to write by code (Flexi would create a new item).",
                        ct);
                    return;
                }

                var priceWithoutVat = hebloPrice?.PriceWithoutVat
                    ?? Math.Round(decision.PriceToPush!.Value / (1 + DeriveVatRate(erp) / 100m), 2, MidpointRounding.AwayFromZero);

                await _erpWriter.SetPriceWithoutVatAsync(erp.ErpItemId, priceWithoutVat, ct);
            }

            context.Result.Pushed++;
            state.LastPushedPriceWithVat = decision.PriceToPush;
            state.LastPushedAt = DateTime.UtcNow;
            state.Status = PriceSyncStatus.InSync;
            state.RemoteValueAtConflict = null;
            state.ConflictDetectedAt = null;
            state.LastError = null;
            await _repository.UpsertSyncStateAsync(state, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to push {ProductCode} to {Target}", productCode, target);
            context.Result.Failed++;
            await FailAsync(state, ex.Message, ct);
        }
    }

    private async Task FailAsync(ProductPriceSyncState state, string error, CancellationToken ct)
    {
        state.Status = PriceSyncStatus.Failed;
        state.LastError = error;
        await _repository.UpsertSyncStateAsync(state, ct);
    }

    private static decimal DeriveVatRate(ProductPriceErp? erp) =>
        erp is null
            ? VatRateCalculator.StandardVatRate
            : VatRateCalculator.FromPrices(erp.PriceWithVat, erp.PriceWithoutVat);

    /// <summary>
    /// Per-run state threaded through the sync pipeline: the master prices being reconciled
    /// (mutated in place when Shoptet seeds a value the same run's Flexi pass must see), the
    /// ERP snapshot and whether it was actually readable this run, the in-scope product codes,
    /// and the running result counters.
    /// </summary>
    private sealed class PriceSyncContext
    {
        public required IDictionary<string, ProductPrice> Prices { get; init; }
        public required IReadOnlyDictionary<string, ProductPriceErp> ErpPrices { get; init; }
        public required IReadOnlySet<string> InScopeProductCodes { get; init; }
        public required bool ErpAvailable { get; init; }
        public required PriceSyncRunResult Result { get; init; }
    }
}
