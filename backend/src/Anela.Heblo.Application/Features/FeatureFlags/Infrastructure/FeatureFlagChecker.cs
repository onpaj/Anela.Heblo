using OpenFeature;

namespace Anela.Heblo.Application.Features.FeatureFlags.Infrastructure;

internal sealed class FeatureFlagChecker : IFeatureFlagChecker
{
    private readonly IFeatureClient _client;

    public FeatureFlagChecker(IFeatureClient client) => _client = client;

    public Task<bool> IsEnabledAsync(string key, CancellationToken ct = default)
    {
        FeatureFlagRegistry.ByKey.TryGetValue(key, out var def);
        return _client.GetBooleanValueAsync(key, def?.DefaultValue ?? false, cancellationToken: ct);
    }

    public Task<bool> IsEnabledAsync(string key, bool defaultValue, CancellationToken ct = default)
        => _client.GetBooleanValueAsync(key, defaultValue, cancellationToken: ct);
}
