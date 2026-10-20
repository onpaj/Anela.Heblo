using Anela.Heblo.Application.Features.MarketingAds.Proposals;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals;

public class AdWebAdminGuardTests
{
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly Mock<IApprovalChannelResolver> _resolver = new();

    public AdWebAdminGuardTests()
    {
        _currentUser.Setup(c => c.GetCurrentUser()).Returns(new CurrentUser("chief", "chief", "chief@anela.cz", true));
        _currentUser.Setup(c => c.IsInRole(AccessRoles.MarketingAdApprovalsAdmin)).Returns(true);
        _resolver.Setup(r => r.Resolve()).Returns(ApprovalChannelResolution.Allow(ApprovalChannel.Web));
    }

    private AdWebAdminCheck Check() => AdWebAdminGuard.Check(_currentUser.Object, _resolver.Object);

    [Fact]
    public void AdminOnTheWebChannel_IsAllowed_WithThePrincipal()
    {
        var check = Check();

        check.IsAllowed.Should().BeTrue();
        check.Principal.Should().Be("chief");
        check.ErrorCode.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void MissingPrincipal_IsUnauthorized(string? id)
    {
        _currentUser.Setup(c => c.GetCurrentUser()).Returns(new CurrentUser(id, null, null, false));

        Check().ErrorCode.Should().Be(ErrorCodes.Unauthorized);
    }

    [Fact]
    public void DeniedChannel_IsDenied_WithTheResolversReason()
    {
        _resolver.Setup(r => r.Resolve()).Returns(ApprovalChannelResolution.Deny("not yet"));

        var check = Check();

        check.ErrorCode.Should().Be(ErrorCodes.MarketingAdsApprovalChannelDenied);
        check.Params.Should().Contain("Reason", "not yet");
    }

    [Theory]
    [InlineData(ApprovalChannel.Mcp)]
    [InlineData(ApprovalChannel.System)]
    public void NonWebChannel_IsDenied_AsWebOnly(ApprovalChannel channel)
    {
        _resolver.Setup(r => r.Resolve()).Returns(ApprovalChannelResolution.Allow(channel));

        var check = Check();

        check.ErrorCode.Should().Be(ErrorCodes.MarketingAdsApprovalChannelDenied);
        check.Params.Should().Contain("Reason", AdWebAdminGuard.WebOnlyReason);
    }

    [Fact]
    public void WebCallerWithoutTheAdminRole_HasInsufficientPermissions()
    {
        _currentUser.Setup(c => c.IsInRole(AccessRoles.MarketingAdApprovalsAdmin)).Returns(false);

        Check().ErrorCode.Should().Be(ErrorCodes.InsufficientPermissions);
    }
}
