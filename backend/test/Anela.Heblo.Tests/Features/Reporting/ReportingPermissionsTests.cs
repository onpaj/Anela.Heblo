using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Reporting;

public class ReportingPermissionsTests
{
    [Theory]
    [InlineData(Feature.Reporting_WebAnalytics, "reporting.web_analytics.read")]
    [InlineData(Feature.Reporting_Eshop, "reporting.eshop.read")]
    [InlineData(Feature.Reporting_Accounting, "reporting.accounting.read")]
    public void Each_source_has_a_read_role(Feature feature, string expectedRole)
    {
        AccessRoles.For(feature, AccessLevel.Read).Should().Be(expectedRole);
    }
}
