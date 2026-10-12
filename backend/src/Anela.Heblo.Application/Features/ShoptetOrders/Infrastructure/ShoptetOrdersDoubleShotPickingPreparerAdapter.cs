using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
using Anela.Heblo.Application.Features.FeatureFlags;
using Anela.Heblo.Application.Features.ShoptetOrders.DoubleShotSwap;

namespace Anela.Heblo.Application.Features.ShoptetOrders.Infrastructure;

/// <summary>
/// Runs one double-shot swap sweep before every picking-list build, so a print never shows a V line.
/// Dry run unless the double-shot swap flag is on — same switch as the recurring job.
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
        var applyChanges = await _featureFlags.IsEnabledAsync(FeatureFlagKeys.DoubleShotOrderSwap, cancellationToken);
        await _swapService.SweepAsync(applyChanges, cancellationToken);
    }
}
