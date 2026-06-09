namespace Anela.Heblo.Domain.Features.Authorization.Entities;

/// <summary>Where an AppUser originates. Entra users have an EntraObjectId and can log in;
/// Local users are login-less packing operators created by an administrator.</summary>
public enum AppUserSource
{
    Entra,
    Local,
}
