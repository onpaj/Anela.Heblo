using Anela.Heblo.Application.Features.FeatureFlags;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Shared.Printing;

/// <summary>
/// Decorates <see cref="ILabelPrintingService"/> so the physical print is gated behind the
/// <see cref="FeatureFlagKeys.LabelPrintingEnabled"/> flag. When the flag is off (e.g. Staging,
/// where no printer exists) the ZPL is not sent to the printer, but the surrounding operation
/// still completes — label generation/persistence is unaffected. Fails open to the inner
/// service if the flag cannot be evaluated (checker falls back to the registry default = true).
/// </summary>
public class FeatureGatedLabelPrintingService : ILabelPrintingService
{
    private readonly ILabelPrintingService _inner;
    private readonly IFeatureFlagChecker _featureFlags;
    private readonly ILogger<FeatureGatedLabelPrintingService> _logger;

    public FeatureGatedLabelPrintingService(
        ILabelPrintingService inner,
        IFeatureFlagChecker featureFlags,
        ILogger<FeatureGatedLabelPrintingService> logger)
    {
        _inner = inner;
        _featureFlags = featureFlags;
        _logger = logger;
    }

    public async Task PrintZplAsync(string zpl, CancellationToken cancellationToken = default)
    {
        if (!await _featureFlags.IsEnabledAsync(FeatureFlagKeys.LabelPrintingEnabled, cancellationToken))
        {
            _logger.LogInformation(
                "Label printing disabled by feature flag '{Flag}'; skipping physical print of {Length}-char ZPL batch.",
                FeatureFlagKeys.LabelPrintingEnabled, zpl?.Length ?? 0);
            return;
        }

        await _inner.PrintZplAsync(zpl, cancellationToken);
    }
}
