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
        var prices = (await _repository.GetAllAsync(ct)).ToDictionary(p => p.ProductCode, StringComparer.OrdinalIgnoreCase);

        var erpPrices = await ReadErpPricesAsync(ct);
        var inScope = await ReadInScopeProductCodesAsync(ct);

        await SyncTargetAsync(PriceSyncTarget.Shoptet, prices, erpPrices, inScope, result, ct);
        await SyncTargetAsync(PriceSyncTarget.Flexi, prices, erpPrices, inScope, result, ct);

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

    private async Task<IReadOnlyDictionary<string, ProductPriceErp>> ReadErpPricesAsync(CancellationToken ct)
    {
        var erpPrices = await _erpReader.GetAllAsync(forceReload: false, ct);

        return erpPrices
            .Where(p => !string.IsNullOrWhiteSpace(p.ProductCode))
            .GroupBy(p => p.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    private async Task SyncTargetAsync(
        PriceSyncTarget target,
        IDictionary<string, ProductPrice> prices,
        IReadOnlyDictionary<string, ProductPriceErp> erpPrices,
        IReadOnlySet<string> inScopeProductCodes,
        PriceSyncRunResult result,
        CancellationToken ct)
    {
        IReadOnlyDictionary<string, decimal> remotePrices;
        try
        {
            remotePrices = target == PriceSyncTarget.Shoptet
                ? await _eshopClient.GetPricesWithVatAsync(ct)
                : erpPrices.ToDictionary(e => e.Key, e => e.Value.PriceWithVat, StringComparer.OrdinalIgnoreCase);
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
        var productCodes = prices.Keys
            .Union(remotePrices.Keys, StringComparer.OrdinalIgnoreCase)
            .Where(inScopeProductCodes.Contains)
            .ToList();

        foreach (var productCode in productCodes)
        {
            ct.ThrowIfCancellationRequested();

            states.TryGetValue(productCode, out var state);
            state ??= new ProductPriceSyncState { ProductCode = productCode, Target = target };
            state.Target = target;

            prices.TryGetValue(productCode, out var hebloPrice);
            remotePrices.TryGetValue(productCode, out var remoteValue);
            var remote = remotePrices.ContainsKey(productCode) ? remoteValue : (decimal?)null;

            var decision = PriceSyncDecider.Decide(
                hebloPrice?.PriceWithVat ?? 0m, state.LastPushedPriceWithVat, remote);

            await ApplyDecisionAsync(decision, target, productCode, hebloPrice, prices, erpPrices, state, result, ct);
        }
    }

    private async Task ApplyDecisionAsync(
        PriceSyncDecision decision,
        PriceSyncTarget target,
        string productCode,
        ProductPrice? hebloPrice,
        IDictionary<string, ProductPrice> prices,
        IReadOnlyDictionary<string, ProductPriceErp> erpPrices,
        ProductPriceSyncState state,
        PriceSyncRunResult result,
        CancellationToken ct)
    {
        switch (decision.Action)
        {
            case PriceSyncAction.None:
                result.Unchanged++;
                return;

            case PriceSyncAction.MissingRemote:
                result.Failed++;
                await FailAsync(state, $"Product {productCode} does not exist in {target}.", ct);
                return;

            case PriceSyncAction.Seed:
                await SeedAsync(decision, target, productCode, hebloPrice, prices, erpPrices, state, result, ct);
                return;

            case PriceSyncAction.Conflict:
                result.Conflicts++;
                state.Status = PriceSyncStatus.Conflict;
                state.RemoteValueAtConflict = decision.RemoteValue;
                state.ConflictDetectedAt = DateTime.UtcNow;
                state.LastError = null;
                await _repository.UpsertSyncStateAsync(state, ct);
                return;

            case PriceSyncAction.Push:
                await PushAsync(decision, target, productCode, hebloPrice, erpPrices, state, result, ct);
                return;
        }
    }

    private async Task SeedAsync(
        PriceSyncDecision decision,
        PriceSyncTarget target,
        string productCode,
        ProductPrice? hebloPrice,
        IDictionary<string, ProductPrice> prices,
        IReadOnlyDictionary<string, ProductPriceErp> erpPrices,
        ProductPriceSyncState state,
        PriceSyncRunResult result,
        CancellationToken ct)
    {
        // Shoptet is today's retail truth, so it seeds the master value. Flexi only ever
        // adopts the seed when it already agrees; otherwise it becomes a conflict for a
        // human to reconcile.
        if (target == PriceSyncTarget.Shoptet)
        {
            result.Seeded++;
            erpPrices.TryGetValue(productCode, out var erp);

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
            prices[productCode] = seeded;

            state.LastPushedPriceWithVat = decision.RemoteValue;
            state.LastPushedAt = DateTime.UtcNow;
            state.Status = PriceSyncStatus.InSync;
            await _repository.UpsertSyncStateAsync(state, ct);
            return;
        }

        var seededPrice = hebloPrice?.PriceWithVat;
        if (seededPrice is null || Math.Round(seededPrice.Value, 2) == Math.Round(decision.RemoteValue!.Value, 2))
        {
            state.LastPushedPriceWithVat = decision.RemoteValue;
            state.LastPushedAt = DateTime.UtcNow;
            state.Status = PriceSyncStatus.InSync;
            await _repository.UpsertSyncStateAsync(state, ct);
            return;
        }

        result.Conflicts++;
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
        IReadOnlyDictionary<string, ProductPriceErp> erpPrices,
        ProductPriceSyncState state,
        PriceSyncRunResult result,
        CancellationToken ct)
    {
        try
        {
            if (target == PriceSyncTarget.Shoptet)
            {
                await _eshopClient.SetPriceWithVatAsync(productCode, decision.PriceToPush!.Value, ct);
            }
            else
            {
                if (!erpPrices.TryGetValue(productCode, out var erp) || erp.ErpItemId <= 0)
                {
                    result.Failed++;
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

            result.Pushed++;
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
            result.Failed++;
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
}
