using Anela.Heblo.Application.Features.FeatureFlags.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.FeatureFlags.UseCases.ListFlags;

internal class ListFlagsHandler : IRequestHandler<ListFlagsRequest, ListFlagsResponse>
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
        var overrides = await _repo.GetAllAsDictionaryAsync(ct);
        var overrideEntities = await _repo.GetAllAsync(ct);
        var flags = new List<FlagStatusDto>();

        foreach (var def in FeatureFlagRegistry.All)
        {
            var currentValue = await _checker.IsEnabledAsync(def.Key, def.DefaultValue, ct);
            var entity = overrideEntities.FirstOrDefault(e => e.Key == def.Key);

            flags.Add(new FlagStatusDto
            {
                Key = def.Key,
                Description = def.Description,
                CurrentValue = currentValue,
                IsOverridden = overrides.ContainsKey(def.Key),
                DefaultValue = def.DefaultValue,
                UpdatedBy = entity?.UpdatedBy,
                UpdatedAt = entity?.UpdatedAt,
            });
        }

        return new ListFlagsResponse { Flags = flags };
    }
}
