namespace Anela.Heblo.Domain.Features.Authorization;

/// <summary>A specific (feature, level) pair. Used inside MenuPath.Requires.</summary>
public sealed record FeaturePermission(Feature Feature, AccessLevel Level);
