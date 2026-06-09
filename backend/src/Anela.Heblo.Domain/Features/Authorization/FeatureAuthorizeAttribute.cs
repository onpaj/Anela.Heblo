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
}
