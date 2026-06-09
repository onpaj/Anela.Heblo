namespace Anela.Heblo.Domain.Features.Authorization;

/// <summary>
/// Deserialization shape for <c>access-matrix.json</c>. The JSON is the single
/// hand-edited source of truth for the feature catalog, menu paths, and the
/// bootstrap group list consumed by the on-demand seeder. The runtime
/// <see cref="AccessMatrix"/> static class is generated from this manifest by
/// <c>Anela.Heblo.AccessMatrixGen</c>.
/// </summary>
public sealed record AccessMatrixManifest(
    string BaseRole,
    IReadOnlyList<FeatureEntry> Features,
    IReadOnlyList<MenuPathEntry> MenuPaths,
    IReadOnlyList<SeedGroupEntry> SeedGroups);

public sealed record FeatureEntry(
    string Key,
    string Label,
    bool HasWrite = false,
    bool HasAdmin = false);

public sealed record MenuPathEntry(
    string Path,
    IReadOnlyList<MenuPathRequirementEntry> Requires);

public sealed record MenuPathRequirementEntry(
    string Feature,
    string Level);

public sealed record SeedGroupEntry(
    string Name,
    IReadOnlyList<string> Roles);
