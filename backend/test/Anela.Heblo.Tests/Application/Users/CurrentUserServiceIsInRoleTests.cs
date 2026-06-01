using System.Security.Claims;
using Anela.Heblo.API.Features.Users;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;

namespace Anela.Heblo.Tests.Application.Users;

public class CurrentUserServiceIsInRoleTests
{
    [Fact]
    public void IsInRole_ReturnsTrue_WhenUserHasMatchingRole()
    {
        // Arrange
        var claims = new[] { new Claim(ClaimTypes.Role, "meeting_manager") };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        var accessorMock = new Mock<IHttpContextAccessor>();
        accessorMock.Setup(x => x.HttpContext).Returns(httpContext);
        var sut = new CurrentUserService(accessorMock.Object);

        // Act
        var result = sut.IsInRole("meeting_manager");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsInRole_ReturnsFalse_WhenUserDoesNotHaveRole()
    {
        // Arrange
        var claims = new[] { new Claim(ClaimTypes.Role, "heblo_user") };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        var accessorMock = new Mock<IHttpContextAccessor>();
        accessorMock.Setup(x => x.HttpContext).Returns(httpContext);
        var sut = new CurrentUserService(accessorMock.Object);

        // Act
        var result = sut.IsInRole("meeting_manager");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsInRole_ReturnsFalse_WhenHttpContextIsNull()
    {
        // Arrange
        var accessorMock = new Mock<IHttpContextAccessor>();
        accessorMock.Setup(x => x.HttpContext).Returns((HttpContext?)null);
        var sut = new CurrentUserService(accessorMock.Object);

        // Act
        var result = sut.IsInRole("meeting_manager");

        // Assert
        result.Should().BeFalse();
    }
}
