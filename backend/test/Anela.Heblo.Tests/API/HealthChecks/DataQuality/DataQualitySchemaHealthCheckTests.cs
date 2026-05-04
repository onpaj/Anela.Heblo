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
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using Xunit;

namespace Anela.Heblo.Tests.API.HealthChecks.DataQuality;

public class DataQualitySchemaHealthCheckTests
{
    private static ILogger<DataQualitySchemaHealthCheck> CreateLogger() =>
        new Mock<ILogger<DataQualitySchemaHealthCheck>>().Object;

    [Fact]
    public async Task CheckHealthAsync_WhenTableReachable_ReturnsHealthy()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"healthy-{Guid.NewGuid()}")
            .Options;
        await using var context = new ApplicationDbContext(options);
        var healthCheck = new DataQualitySchemaHealthCheck(context, CreateLogger());

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

        var healthCheck = new DataQualitySchemaHealthCheck(context, CreateLogger());

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

        var healthCheck = new DataQualitySchemaHealthCheck(context, CreateLogger());

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("DataQuality probe failed");
        result.Exception.Should().BeSameAs(connectionException);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenCancelled_ReturnsDegradedWithNoException()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"cancelled-{Guid.NewGuid()}")
            .Options;
        await using var context = new ApplicationDbContext(options);
        context.DqtRuns = BuildThrowingDbSet<DqtRun>(new OperationCanceledException());

        var loggerMock = new Mock<ILogger<DataQualitySchemaHealthCheck>>();
        var healthCheck = new DataQualitySchemaHealthCheck(context, loggerMock.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Be("DataQuality probe was cancelled");
        result.Exception.Should().BeNull();
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("DataQuality probe cancelled")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenCancellationTokenFires_ReturnsDegradedWithoutException()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"cancelled-token-{Guid.NewGuid()}")
            .Options;
        await using var context = new ApplicationDbContext(options);
        context.DqtRuns = BuildThrowingDbSet<DqtRun>(new OperationCanceledException("probe cancelled"));

        var loggerMock = new Mock<ILogger<DataQualitySchemaHealthCheck>>();
        var healthCheck = new DataQualitySchemaHealthCheck(context, loggerMock.Object);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), cts.Token);

        // Assert
        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Be("DataQuality probe was cancelled");
        result.Exception.Should().BeNull();
    }

    [Fact]
    public async Task CheckHealthAsync_WhenCancelled_LogsInformationWithProbeNameAndElapsed()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"cancelled-log-{Guid.NewGuid()}")
            .Options;
        await using var context = new ApplicationDbContext(options);
        context.DqtRuns = BuildThrowingDbSet<DqtRun>(new TaskCanceledException());

        var loggerMock = new Mock<ILogger<DataQualitySchemaHealthCheck>>();
        var healthCheck = new DataQualitySchemaHealthCheck(context, loggerMock.Object);

        // Act
        await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert: exactly one Information log with ProbeName + ElapsedMs and NO exception payload.
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) =>
                    v.ToString()!.Contains("data-quality-schema")
                    && v.ToString()!.Contains("ElapsedMs")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        loggerMock.VerifyNoOtherCalls();
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
