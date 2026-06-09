namespace Anela.Heblo.Domain.Features.Authorization;

/// <summary>A single concrete app role (one feature × one level).</summary>
public sealed record AccessRoleDefinition(string Value, string Feature, AccessLevel Level);

