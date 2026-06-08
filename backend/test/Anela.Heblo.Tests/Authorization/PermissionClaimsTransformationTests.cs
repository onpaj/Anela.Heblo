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
    public async Task Transform_SuperUserRoleInToken_AddsAllPermissions_NoResolverCall()
    {
        var resolver = new Mock<IPermissionResolver>(MockBehavior.Strict);
        var sut = new PermissionClaimsTransformation(resolver.Object);
        var principal = Principal(
            new Claim(ClaimTypes.NameIdentifier, "oid-super"),
            new Claim(ClaimTypes.Role, AccessRoles.SuperUser));

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
            .ReturnsAsync(new EffectivePermissions(false, new[] { "heblo_user", "catalog.read" }, new[] { "G" }));
        var sut = new PermissionClaimsTransformation(resolver.Object);
        var principal = Principal(new Claim(ClaimTypes.NameIdentifier, "oid-1"));

        var result = await sut.TransformAsync(principal);

        result.IsInRole("catalog.read").Should().BeTrue();
        result.IsInRole("heblo_user").Should().BeTrue();
        result.IsInRole("journal.read").Should().BeFalse();
    }

    [Fact]
    public async Task Transform_Idempotent_DoesNotDuplicateClaims()
    {
        var resolver = new Mock<IPermissionResolver>();
        resolver
            .Setup(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectivePermissions(false, new[] { "catalog.read" }, Array.Empty<string>()));
        var sut = new PermissionClaimsTransformation(resolver.Object);
        var principal = Principal(new Claim(ClaimTypes.NameIdentifier, "oid-1"));

        var once = await sut.TransformAsync(principal);
        var twice = await sut.TransformAsync(once);

        twice.Claims.Count(c => c.Type == ClaimTypes.Role && c.Value == "catalog.read").Should().Be(1);
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
            .ReturnsAsync(new EffectivePermissions(false, new[] { "catalog.read" }, new[] { "G" }));

        var sut = new PermissionClaimsTransformation(resolver.Object);
        var principal = Principal(
            new Claim("oid", realOid),
            new Claim(ClaimTypes.NameIdentifier, subValue));

        var result = await sut.TransformAsync(principal);

        result.IsInRole("catalog.read").Should().BeTrue();
        resolver.Verify(
            r => r.ResolveAsync(realOid, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        resolver.VerifyNoOtherCalls();
    }
}
