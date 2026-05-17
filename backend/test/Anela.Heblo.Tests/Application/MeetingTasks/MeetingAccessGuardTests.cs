using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Application.MeetingTasks;

public class MeetingAccessGuardTests
{
    private readonly Mock<ICurrentUserService> _userServiceMock;
    private readonly MeetingAccessGuard _guard;

    public MeetingAccessGuardTests()
    {
        _userServiceMock = new Mock<ICurrentUserService>();
        _guard = new MeetingAccessGuard(_userServiceMock.Object);
    }

    [Fact]
    public void IsManager_ReturnsTrue_WhenUserHasMeetingManagerRole()
    {
        _userServiceMock.Setup(x => x.IsInRole(AuthorizationConstants.Roles.MeetingManager)).Returns(true);
        _guard.IsManager().Should().BeTrue();
    }

    [Fact]
    public void IsManager_ReturnsFalse_WhenUserLacksRole()
    {
        _userServiceMock.Setup(x => x.IsInRole(AuthorizationConstants.Roles.MeetingManager)).Returns(false);
        _guard.IsManager().Should().BeFalse();
    }

    [Fact]
    public void CanAccess_ReturnsTrue_ForManagerRegardlessOfAccessLevel()
    {
        _userServiceMock.Setup(x => x.IsInRole(AuthorizationConstants.Roles.MeetingManager)).Returns(true);
        _userServiceMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser(null, "Manager", "manager@test.com", true));
        var privateTranscript = MakeTranscript(MeetingAccessLevel.Private);
        _guard.CanAccess(privateTranscript).Should().BeTrue();
    }

    [Fact]
    public void CanAccess_ReturnsTrue_ForPublicTranscript_WhenNonManager()
    {
        SetupNonManager("user@test.com");
        var transcript = MakeTranscript(MeetingAccessLevel.Public);
        _guard.CanAccess(transcript).Should().BeTrue();
    }

    [Fact]
    public void CanAccess_ReturnsFalse_ForPrivateTranscript_WhenNonManager()
    {
        SetupNonManager("user@test.com");
        var transcript = MakeTranscript(MeetingAccessLevel.Private);
        _guard.CanAccess(transcript).Should().BeFalse();
    }

    [Fact]
    public void CanAccess_ReturnsTrue_ForRestrictedTranscript_WhenEmailMatches()
    {
        SetupNonManager("user@test.com");
        var transcript = MakeTranscript(MeetingAccessLevel.Restricted, grantedEmails: ["user@test.com"]);
        _guard.CanAccess(transcript).Should().BeTrue();
    }

    [Fact]
    public void CanAccess_ReturnsTrue_ForRestrictedTranscript_CaseInsensitiveMatch()
    {
        SetupNonManager("USER@TEST.COM");
        var transcript = MakeTranscript(MeetingAccessLevel.Restricted, grantedEmails: ["user@test.com"]);
        _guard.CanAccess(transcript).Should().BeTrue();
    }

    [Fact]
    public void CanAccess_ReturnsFalse_ForRestrictedTranscript_WhenEmailDoesNotMatch()
    {
        SetupNonManager("other@test.com");
        var transcript = MakeTranscript(MeetingAccessLevel.Restricted, grantedEmails: ["user@test.com"]);
        _guard.CanAccess(transcript).Should().BeFalse();
    }

    [Fact]
    public void CanAccess_ReturnsFalse_WhenEmailIsNull()
    {
        _userServiceMock.Setup(x => x.IsInRole(AuthorizationConstants.Roles.MeetingManager)).Returns(false);
        _userServiceMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser(null, "Anonymous", null, false));
        var transcript = MakeTranscript(MeetingAccessLevel.Public);
        _guard.CanAccess(transcript).Should().BeFalse();
    }

    [Fact]
    public void CanAccess_ReturnsFalse_WhenEmailIsWhitespace()
    {
        _userServiceMock.Setup(x => x.IsInRole(AuthorizationConstants.Roles.MeetingManager)).Returns(false);
        _userServiceMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser(null, "User", "   ", false));
        var transcript = MakeTranscript(MeetingAccessLevel.Public);
        _guard.CanAccess(transcript).Should().BeFalse();
    }

    private void SetupNonManager(string email)
    {
        _userServiceMock.Setup(x => x.IsInRole(AuthorizationConstants.Roles.MeetingManager)).Returns(false);
        _userServiceMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser(null, "User", email, true));
    }

    private static MeetingTranscript MakeTranscript(MeetingAccessLevel level, string[]? grantedEmails = null)
    {
        var grants = (grantedEmails ?? []).Select(e => new MeetingAccessGrant { UserEmail = e }).ToList();
        return new MeetingTranscript
        {
            Id = Guid.NewGuid(),
            PlaudRecordingId = "test",
            PlaudCreatedAt = DateTime.UtcNow,
            Subject = "Test",
            Summary = "Test",
            RawTranscript = "Test",
            Status = MeetingTranscriptStatus.PendingReview,
            ReceivedAt = DateTime.UtcNow,
            AccessLevel = level,
            AccessGrants = grants
        };
    }
}
