using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
using Anela.Heblo.Application.Features.FeatureFlags;
using Anela.Heblo.Application.Features.ShoptetOrders.DoubleShotSwap;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.ShoptetOrders.Infrastructure;

/// <summary>
/// Runs one double-shot swap sweep before every picking-list build, so a print never shows a V line.
/// Only while the double-shot swap flag is on: with it off the recurring job alone produces the
/// dry-run log, so a print does not pay for a sweep that changes nothing.
/// The sweep is time-boxed so a slow Shoptet never stalls printing; a swap cut off midway is
/// finished by the next run via the self-healing tag.
/// </summary>
internal sealed class ShoptetOrdersDoubleShotPickingPreparerAdapter : IPickingListOrderPreparer
{
    internal static readonly TimeSpan DefaultSweepTimeout = TimeSpan.FromSeconds(60);

    private readonly IDoubleShotOrderSwapService _swapService;
    private readonly IFeatureFlagChecker _featureFlags;
    private readonly ILogger<ShoptetOrdersDoubleShotPickingPreparerAdapter> _logger;
    private readonly TimeSpan _sweepTimeout;

    public ShoptetOrdersDoubleShotPickingPreparerAdapter(
        IDoubleShotOrderSwapService swapService,
        IFeatureFlagChecker featureFlags,
        ILogger<ShoptetOrdersDoubleShotPickingPreparerAdapter> logger)
        : this(swapService, featureFlags, logger, DefaultSweepTimeout)
    {
    }

    internal ShoptetOrdersDoubleShotPickingPreparerAdapter(
        IDoubleShotOrderSwapService swapService,
        IFeatureFlagChecker featureFlags,
        ILogger<ShoptetOrdersDoubleShotPickingPreparerAdapter> logger,
        TimeSpan sweepTimeout)
    {
        _swapService = swapService;
        _featureFlags = featureFlags;
        _logger = logger;
        _sweepTimeout = sweepTimeout;
    }

    public async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        if (!await _featureFlags.IsEnabledAsync(FeatureFlagKeys.DoubleShotOrderSwap, cancellationToken))
            return;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_sweepTimeout);
        try
        {
            await _swapService.SweepAsync(applyChanges: true, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "DoubleShotSwap: pre-print sweep did not finish within {Timeout}; printing continues and the recurring job finishes the swaps.",
                _sweepTimeout);
        }
    }
}
