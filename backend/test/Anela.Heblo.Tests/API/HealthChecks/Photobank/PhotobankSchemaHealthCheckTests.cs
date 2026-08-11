using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.API.HealthChecks.Photobank;
using Anela.Heblo.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Anela.Heblo.Tests.API.HealthChecks.Photobank;

public class PhotobankSchemaHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_WhenProviderNotRelational_ReturnsHealthyAndSkips()
    {
        // Arrange — the in-memory provider used by this test suite is non-relational, so the
        // check must short-circuit to Healthy rather than attempting an information_schema
        // query the in-memory provider cannot serve.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"photobank-schema-{Guid.NewGuid()}")
            .Options;
        await using var context = new ApplicationDbContext(options);
        var healthCheck = new PhotobankSchemaHealthCheck(context);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("Non-relational provider — schema drift check skipped");
    }
}
