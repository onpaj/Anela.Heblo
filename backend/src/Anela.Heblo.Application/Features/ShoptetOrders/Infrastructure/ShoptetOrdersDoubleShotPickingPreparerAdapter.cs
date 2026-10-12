using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
using Anela.Heblo.Application.Features.FeatureFlags;
using Anela.Heblo.Application.Features.ShoptetOrders.DoubleShotSwap;

namespace Anela.Heblo.Application.Features.ShoptetOrders.Infrastructure;

/// <summary>
/// Runs one double-shot swap sweep before every picking-list build, so a print never shows a V line.
/// Only while the double-shot swap flag is on: with it off the recurring job alone produces the
/// dry-run log, so a print does not pay for a sweep that changes nothing.
/// </summary>
internal sealed class ShoptetOrdersDoubleShotPickingPreparerAdapter : IPickingListOrderPreparer
{
    private readonly IDoubleShotOrderSwapService _swapService;
    private readonly IFeatureFlagChecker _featureFlags;

    public ShoptetOrdersDoubleShotPickingPreparerAdapter(IDoubleShotOrderSwapService swapService, IFeatureFlagChecker featureFlags)
    {
        _swapService = swapService;
        _featureFlags = featureFlags;
    }

    public async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        if (!await _featureFlags.IsEnabledAsync(FeatureFlagKeys.DoubleShotOrderSwap, cancellationToken))
            return;

        await _swapService.SweepAsync(applyChanges: true, cancellationToken);
    }
}
