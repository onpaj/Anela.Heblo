namespace Anela.Heblo.Domain.Features.Authorization;

/// <summary>
/// One menu entry. Key is the frontend route (e.g. "/finance/overview") or a virtual
/// identifier for external onClick items (e.g. "#hangfire"). Requires lists permissions
/// the user must hold (AND semantics) for the item to be visible.
/// </summary>
public sealed record MenuPath(
    string Key,
    IReadOnlyList<FeaturePermission> Requires);
