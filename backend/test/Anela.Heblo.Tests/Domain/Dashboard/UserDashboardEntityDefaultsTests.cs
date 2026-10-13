using Anela.Heblo.Domain.Features.Dashboard;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Domain.Dashboard;

public class UserDashboardEntityDefaultsTests
{
    [Fact]
    public void UserDashboardTile_Constructed_DefaultsLastModifiedToMinValue()
    {
        var tile = new UserDashboardTile();

        tile.LastModified.Should().Be(DateTime.MinValue);
    }

    [Fact]
    public void UserDashboardSettings_Constructed_DefaultsLastModifiedToMinValue()
    {
        var settings = new UserDashboardSettings();

        settings.LastModified.Should().Be(DateTime.MinValue);
    }
}
