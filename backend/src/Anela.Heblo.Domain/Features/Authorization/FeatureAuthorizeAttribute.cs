using Microsoft.AspNetCore.Authorization;

namespace Anela.Heblo.Domain.Features.Authorization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class FeatureAuthorizeAttribute : AuthorizeAttribute
{
    public Feature Feature { get; }
    public AccessLevel Level { get; }

    public FeatureAuthorizeAttribute(Feature feature, AccessLevel level = AccessLevel.Read)
    {
        Feature = feature;
        Level = level;
        Roles = AccessRoles.For(feature, level);
    }

    /// <summary>
    /// Authorizes the endpoint when the caller holds ANY of the given features at Read
    /// level (OR semantics). Use when a single capability can be reached through several
    /// distinct permissions — e.g. reading a job's status is allowed for trigger, disable,
    /// or administration holders.
    /// </summary>
    public FeatureAuthorizeAttribute(params Feature[] features)
    {
        if (features is null || features.Length == 0)
        {
            throw new ArgumentException("At least one feature must be supplied.", nameof(features));
        }

        Feature = features[0];
        Level = AccessLevel.Read;
        Roles = string.Join(",", features.Select(f => AccessRoles.For(f, AccessLevel.Read)));
    }
}
