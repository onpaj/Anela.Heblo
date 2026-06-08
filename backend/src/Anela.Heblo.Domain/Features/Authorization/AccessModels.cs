namespace Anela.Heblo.Domain.Features.Authorization;

/// <summary>A single concrete app role (one feature × one level).</summary>
public sealed record AccessRoleDefinition(string Value, string Feature, AccessLevel Level);

/// <summary>An Entra security group representing an employee work-role.</summary>
public sealed record AccessGroup(string Name, IReadOnlyList<string> Roles);
