using MediatR;

namespace Anela.Heblo.Application.Features.FeatureFlags.UseCases.EvaluateFlagsForClient;

internal class EvaluateFlagsForClientHandler : IRequestHandler<EvaluateFlagsForClientRequest, EvaluateFlagsForClientResponse>
{
    private readonly IFeatureFlagChecker _checker;

    public EvaluateFlagsForClientHandler(IFeatureFlagChecker checker) => _checker = checker;

    public async Task<EvaluateFlagsForClientResponse> Handle(
        EvaluateFlagsForClientRequest request, CancellationToken ct)
    {
        var result = new Dictionary<string, bool>();
        foreach (var def in FeatureFlagRegistry.All)
            result[def.Key] = await _checker.IsEnabledAsync(def.Key, def.DefaultValue, ct);
        return new EvaluateFlagsForClientResponse { Flags = result };
    }
}
