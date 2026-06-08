namespace Anela.Heblo.Domain.Features.Authorization;

/// <summary>Per-feature metadata: which levels exist (read is implicit).</summary>
public sealed record FeatureDefinition(
    Feature Key,
    string Label,
    bool HasWrite = false,
    bool HasAdmin = false);
