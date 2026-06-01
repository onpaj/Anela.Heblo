using MediatR;

namespace Anela.Heblo.Application.Features.FeatureFlags.UseCases.EvaluateFlagsForClient;

internal sealed class EvaluateFlagsForClientHandler : IRequestHandler<EvaluateFlagsForClientRequest, EvaluateFlagsForClientResponse>
{
    private readonly IFeatureFlagChecker _checker;

    public EvaluateFlagsForClientHandler(IFeatureFlagChecker checker) => _checker = checker;

    public async Task<EvaluateFlagsForClientResponse> Handle(
        EvaluateFlagsForClientRequest request, CancellationToken ct)
    {
        var tasks = FeatureFlagRegistry.All
            .Select(def => _checker.IsEnabledAsync(def.Key, def.DefaultValue, ct))
            .ToList();
        var values = await Task.WhenAll(tasks);

        var flags = FeatureFlagRegistry.All
            .Zip(values, (def, value) => (def.Key, value))
            .ToDictionary(x => x.Key, x => x.value);

        return new EvaluateFlagsForClientResponse { Flags = flags };
    }
}
