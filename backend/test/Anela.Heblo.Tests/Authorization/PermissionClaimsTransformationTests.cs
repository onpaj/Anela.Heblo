using System.Security.Claims;
using Anela.Heblo.API.Infrastructure.Authentication;
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class PermissionClaimsTransformationTests
{
    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "Test"));

    [Fact]
    public async Task Transform_SuperUserWithObjectId_GrantsAllPermissions_AndRecordsLoginViaResolver()
    {
        // Regression: super_users must still hit the resolver so LastLoginAt is recorded.
        // The token role still drives the granted permissions (wildcard), but the resolver
        // call is what materializes / updates the AppUser row on login.
        const string oid = "oid-super";
        var resolver = new Mock<IPermissionResolver>(MockBehavior.Strict);
        resolver
            .Setup(r => r.ResolveAsync(oid, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectivePermissions(false, new[] { "products.catalog.read" }, Array.Empty<string>()));
        var sut = new PermissionClaimsTransformation(resolver.Object);
        var principal = Principal(
            new Claim("oid", oid),
            new Claim(ClaimTypes.Role, AccessRoles.SuperUser));

        var result = await sut.TransformAsync(principal);

        foreach (var perm in AccessMatrix.AllRoleValues())
            result.IsInRole(perm).Should().BeTrue($"super_user must grant {perm}");
        result.IsInRole(AccessRoles.Base).Should().BeTrue();
        resolver.Verify(
            r => r.ResolveAsync(oid, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        resolver.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Transform_SuperUserWithoutObjectId_StillGrantsAllPermissions()
    {
        // Defensive: if a super_user token somehow lacks both oid and NameIdentifier
        // (shouldn't happen in production Entra or mock auth), break-glass grant still applies.
        var resolver = new Mock<IPermissionResolver>(MockBehavior.Strict);
        var sut = new PermissionClaimsTransformation(resolver.Object);
        var principal = Principal(new Claim(ClaimTypes.Role, AccessRoles.SuperUser));

        var result = await sut.TransformAsync(principal);

        foreach (var perm in AccessMatrix.AllRoleValues())
            result.IsInRole(perm).Should().BeTrue($"super_user must grant {perm}");
        result.IsInRole(AccessRoles.Base).Should().BeTrue();
        resolver.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Transform_RegularUser_AddsResolvedPermissionsAsRoles()
    {
        var resolver = new Mock<IPermissionResolver>();
        resolver
            .Setup(r => r.ResolveAsync("oid-1", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectivePermissions(false, new[] { "heblo_user", "products.catalog.read" }, new[] { "G" }));
        var sut = new PermissionClaimsTransformation(resolver.Object);
        var principal = Principal(new Claim("oid", "oid-1"));

        var result = await sut.TransformAsync(principal);

        result.IsInRole("products.catalog.read").Should().BeTrue();
        result.IsInRole("heblo_user").Should().BeTrue();
        result.IsInRole("products.journal.read").Should().BeFalse();
    }

    [Fact]
    public async Task Transform_Idempotent_DoesNotDuplicateClaims()
    {
        var resolver = new Mock<IPermissionResolver>();
        resolver
            .Setup(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectivePermissions(false, new[] { "products.catalog.read" }, Array.Empty<string>()));
        var sut = new PermissionClaimsTransformation(resolver.Object);
        var principal = Principal(new Claim("oid", "oid-1"));

        var once = await sut.TransformAsync(principal);
        var twice = await sut.TransformAsync(once);

        twice.Claims.Count(c => c.Type == ClaimTypes.Role && c.Value == "products.catalog.read").Should().Be(1);
    }

    [Fact]
    public async Task Transform_Unauthenticated_ReturnsUnchanged()
    {
        var resolver = new Mock<IPermissionResolver>(MockBehavior.Strict);
        var sut = new PermissionClaimsTransformation(resolver.Object);
        var anon = new ClaimsPrincipal(new ClaimsIdentity()); // not authenticated

        var result = await sut.TransformAsync(anon);

        result.Claims.Should().BeEmpty();
    }

    [Fact]
    public async Task Transform_EntraToken_UsesOidClaim_NotNameIdentifier()
    {
        // Real Entra JWT: contains both "oid" (tenant-wide object ID, used by Graph)
        // and a NameIdentifier mapped from "sub" (per-user-per-app pairwise pseudonymous ID).
        // The resolver MUST be called with the oid, not the sub — otherwise the pre-created
        // AppUser row (keyed by oid from EntraMemberSearch) is missed and a duplicate is JIT-created.
        const string realOid = "11111111-2222-3333-4444-555555555555";
        const string subValue = "sub-value-different-from-oid";

        var resolver = new Mock<IPermissionResolver>(MockBehavior.Strict);
        resolver
            .Setup(r => r.ResolveAsync(realOid, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectivePermissions(false, new[] { "products.catalog.read" }, new[] { "G" }));

        var sut = new PermissionClaimsTransformation(resolver.Object);
        var principal = Principal(
            new Claim("oid", realOid),
            new Claim(ClaimTypes.NameIdentifier, subValue));

        var result = await sut.TransformAsync(principal);

        result.IsInRole("products.catalog.read").Should().BeTrue();
        resolver.Verify(
            r => r.ResolveAsync(realOid, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        resolver.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Transform_EntraToken_UsesObjectIdentifierUri_WhenMapInboundClaimsApplied()
    {
        // When MapInboundClaims is enabled in JwtBearerOptions, the JWT "oid" claim is rewritten
        // to the URI "http://schemas.microsoft.com/identity/claims/objectidentifier".
        // GetObjectId() reads either form; pin that behavior here.
        const string realOid = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

        var resolver = new Mock<IPermissionResolver>(MockBehavior.Strict);
        resolver
            .Setup(r => r.ResolveAsync(realOid, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectivePermissions(false, new[] { "products.catalog.read" }, new[] { "G" }));

        var sut = new PermissionClaimsTransformation(resolver.Object);
        var principal = Principal(
            new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", realOid),
            new Claim(ClaimTypes.NameIdentifier, "sub-mapped-to-name-identifier"));

        var result = await sut.TransformAsync(principal);

        result.IsInRole("products.catalog.read").Should().BeTrue();
        resolver.Verify(
            r => r.ResolveAsync(realOid, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        resolver.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Transform_PassesPreferredUsernameAndRawName_NotClaimTypesNameWhichAliasesUpn()
    {
        // Regression: Microsoft.Identity.Web's default NameClaimType for Web APIs is
        // "preferred_username", which makes FindFirst(ClaimTypes.Name) silently return
        // the UPN/email instead of the display name. Naively reading ClaimTypes.Name first
        // caused the resolver to create AppUser rows with DisplayName=UPN and Email=null
        // (then falling back to entraObjectId, producing rows like Email='3983f7e5-…').
        const string oid = "11111111-2222-3333-4444-555555555555";
        const string upn = "ondra@anela.cz";
        const string displayName = "Ondrej Pajgrt";

        var resolver = new Mock<IPermissionResolver>(MockBehavior.Strict);
        resolver
            .Setup(r => r.ResolveAsync(oid, upn, displayName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectivePermissions(false, new[] { "products.catalog.read" }, Array.Empty<string>()));

        var sut = new PermissionClaimsTransformation(resolver.Object);
        // Mimic Microsoft.Identity.Web Web API: NameClaimType = "preferred_username", so
        // ClaimTypes.Name reads the UPN. The real display name is in the raw "name" claim.
        var identity = new ClaimsIdentity(
            claims: new[]
            {
                new Claim("oid", oid),
                new Claim("preferred_username", upn),
                new Claim("name", displayName),
            },
            authenticationType: "Test",
            nameType: "preferred_username",
            roleType: ClaimTypes.Role);
        var principal = new ClaimsPrincipal(identity);

        await sut.TransformAsync(principal);

        // Strict mock: ResolveAsync MUST be called with the real UPN as email and the
        // real display name as name — proves we read "preferred_username" / raw "name"
        // and ignored the UPN-aliased ClaimTypes.Name.
        resolver.Verify(
            r => r.ResolveAsync(oid, upn, displayName, It.IsAny<CancellationToken>()),
            Times.Once);
        resolver.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Transform_NoOidClaim_FallsBackToNameIdentifier()
    {
        // Mock auth scheme (used in dev/test/E2E) doesn't emit "oid" — only NameIdentifier.
        // The fallback must still hand a non-null identifier to the resolver.
        const string mockIdentifier = "mock-only-identifier";

        var resolver = new Mock<IPermissionResolver>(MockBehavior.Strict);
        resolver
            .Setup(r => r.ResolveAsync(mockIdentifier, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectivePermissions(false, new[] { "products.catalog.read" }, Array.Empty<string>()));

        var sut = new PermissionClaimsTransformation(resolver.Object);
        var principal = Principal(new Claim(ClaimTypes.NameIdentifier, mockIdentifier));

        var result = await sut.TransformAsync(principal);

        result.IsInRole("products.catalog.read").Should().BeTrue();
        resolver.Verify(
            r => r.ResolveAsync(mockIdentifier, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        resolver.VerifyNoOtherCalls();
    }
}
