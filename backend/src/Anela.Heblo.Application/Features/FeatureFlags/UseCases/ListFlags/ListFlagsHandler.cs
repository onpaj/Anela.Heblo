using Anela.Heblo.Application.Features.FeatureFlags.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.FeatureFlags.UseCases.ListFlags;

internal sealed class ListFlagsHandler : IRequestHandler<ListFlagsRequest, ListFlagsResponse>
{
    private readonly IFeatureFlagOverrideRepository _repo;
    private readonly IFeatureFlagChecker _checker;

    public ListFlagsHandler(IFeatureFlagOverrideRepository repo, IFeatureFlagChecker checker)
    {
        _repo = repo;
        _checker = checker;
    }

    public async Task<ListFlagsResponse> Handle(ListFlagsRequest request, CancellationToken ct)
    {
        var overrideEntities = await _repo.GetAllAsync(ct);
        var overrideMap = overrideEntities.ToDictionary(e => e.Key, StringComparer.Ordinal);

        var tasks = FeatureFlagRegistry.All
            .Select(def => _checker.IsEnabledAsync(def.Key, def.DefaultValue, ct))
            .ToList();
        var values = await Task.WhenAll(tasks);

        var flags = FeatureFlagRegistry.All
            .Zip(values, (def, currentValue) =>
            {
                overrideMap.TryGetValue(def.Key, out var entity);
                return new FlagStatusDto
                {
                    Key = def.Key,
                    Description = def.Description,
                    CurrentValue = currentValue,
                    IsOverridden = overrideMap.ContainsKey(def.Key),
                    DefaultValue = def.DefaultValue,
                    UpdatedBy = entity?.UpdatedBy,
                    UpdatedAt = entity?.UpdatedAt,
                };
            })
            .ToList();

        return new ListFlagsResponse { Flags = flags };
    }
}
