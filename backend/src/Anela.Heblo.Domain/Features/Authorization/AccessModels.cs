namespace Anela.Heblo.Domain.Features.Authorization;

/// <summary>A protectable area of the app. Produces one role per supported level.</summary>
public sealed record AccessFeature(
    string Key,              // e.g. "purchase_orders"
    string Label,            // human label, e.g. "Nákupní objednávky"
    string Section,          // sidebar section, e.g. "Nákup"
    string? Path,            // primary frontend route, e.g. "/purchase/orders" (null = no nav entry)
    bool HasWrite = false,
    bool HasAdmin = false,
    IReadOnlyList<string>? AdditionalPaths = null); // extra menu paths gated by feature.read

/// <summary>A single concrete app role (one feature × one level).</summary>
public sealed record AccessRoleDefinition(string Value, string Feature, AccessLevel Level);

/// <summary>An Entra security group representing an employee work-role.</summary>
public sealed record AccessGroup(string Name, IReadOnlyList<string> Roles);
