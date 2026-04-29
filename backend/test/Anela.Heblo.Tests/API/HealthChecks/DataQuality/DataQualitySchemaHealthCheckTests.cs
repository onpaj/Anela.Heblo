using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.API.HealthChecks.DataQuality;
using Anela.Heblo.Domain.Features.DataQuality;
using Anela.Heblo.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Npgsql;
using Xunit;

namespace Anela.Heblo.Tests.API.HealthChecks.DataQuality;

public class DataQualitySchemaHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_WhenTableReachable_ReturnsHealthy()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"healthy-{Guid.NewGuid()}")
            .Options;
        await using var context = new ApplicationDbContext(options);
        var healthCheck = new DataQualitySchemaHealthCheck(context);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("DataQuality schema is reachable");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenTableMissing_ReturnsUnhealthyWith42P01Data()
    {
        // Arrange
        var tableNotFoundException = new PostgresException(
            messageText: "relation does not exist",
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: "42P01");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"table-missing-{Guid.NewGuid()}")
            .Options;
        await using var context = new ApplicationDbContext(options);
        context.DqtRuns = BuildThrowingDbSet<DqtRun>(tableNotFoundException);

        var healthCheck = new DataQualitySchemaHealthCheck(context);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("DataQuality table not found");
        result.Exception.Should().BeSameAs(tableNotFoundException);
        result.Data["entity"].Should().Be("DqtRun");
        result.Data["expectedTable"].Should().Be("DqtRuns");
        result.Data["schema"].Should().Be("public");
        result.Data["sqlState"].Should().Be("42P01");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenOtherException_ReturnsUnhealthyWithRawException()
    {
        // Arrange
        var connectionException = new InvalidOperationException("connection broken");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"probe-failed-{Guid.NewGuid()}")
            .Options;
        await using var context = new ApplicationDbContext(options);
        context.DqtRuns = BuildThrowingDbSet<DqtRun>(connectionException);

        var healthCheck = new DataQualitySchemaHealthCheck(context);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("DataQuality probe failed");
        result.Exception.Should().BeSameAs(connectionException);
    }

    private static DbSet<T> BuildThrowingDbSet<T>(Exception toThrow) where T : class
    {
        var mockProvider = new Mock<IAsyncQueryProvider>();
        mockProvider
            .Setup(p => p.ExecuteAsync<Task<bool>>(It.IsAny<Expression>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromException<bool>(toThrow));

        var emptyQueryable = Enumerable.Empty<T>().AsQueryable();

        var mockDbSet = new Mock<DbSet<T>>();
        mockDbSet.As<IQueryable<T>>().Setup(q => q.Provider).Returns(mockProvider.Object);
        mockDbSet.As<IQueryable<T>>().Setup(q => q.Expression).Returns(emptyQueryable.Expression);
        mockDbSet.As<IQueryable<T>>().Setup(q => q.ElementType).Returns(typeof(T));
        mockDbSet.As<IQueryable<T>>().Setup(q => q.GetEnumerator()).Returns(emptyQueryable.GetEnumerator());

        return mockDbSet.Object;
    }
}
