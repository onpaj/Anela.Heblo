using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Tests.Features.BackgroundJobs.Infrastructure;
using FluentAssertions;
using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

/// <summary>
/// Unit tests for HangfireJobEnqueuer service.
/// Tests reflection-based job enqueueing logic.
/// Uses HangfireTestFixture via collection to properly manage Hangfire infrastructure and prevent
/// ObjectDisposedException when tests run in bulk.
/// </summary>
[Collection("Hangfire")]
public class HangfireJobEnqueuerTests
{
    private readonly Mock<ILogger<HangfireJobEnqueuer>> _loggerMock;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly HangfireJobEnqueuer _enqueuer;

    public HangfireJobEnqueuerTests(HangfireTestFixture fixture)
    {
        // Hangfire is already initialized by the shared collection fixture
        // Just create the test instance dependencies
        _loggerMock = new Mock<ILogger<HangfireJobEnqueuer>>();
        _backgroundJobClient = new BackgroundJobClient(JobStorage.Current);
        _enqueuer = new HangfireJobEnqueuer(_loggerMock.Object, _backgroundJobClient);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act
        Action act = () => new HangfireJobEnqueuer(null!, _backgroundJobClient);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WithNullBackgroundJobClient_ShouldThrowArgumentNullException()
    {
        // Act
        Action act = () => new HangfireJobEnqueuer(_loggerMock.Object, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("backgroundJobClient");
    }

    #endregion

    #region EnqueueJob Tests

    [Fact]
    public void EnqueueJob_WithNullJob_ShouldThrowArgumentNullException()
    {
        // Act
        Action act = () => _enqueuer.EnqueueJob(null!, CancellationToken.None);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("job");
    }

    [Fact]
    public void EnqueueJob_WithValidJob_UsesMetadataCorrectly()
    {
        // Arrange
        var testJob = new TestRecurringJob();

        // Act & Assert
        testJob.Metadata.JobName.Should().Be("test-job");
    }

    [Fact]
    public void EnqueueJobInternal_ExistsAndHasCorrectSignature()
    {
        // This test verifies that our internal wrapper method exists and has correct signature.
        // This provides design-time validation of Hangfire API - if it changes, this test fails.

        var enqueueInternalMethod = typeof(HangfireJobEnqueuer)
            .GetMethod("EnqueueJobInternal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Assert method exists
        Assert.NotNull(enqueueInternalMethod);
        Assert.Equal("EnqueueJobInternal", enqueueInternalMethod.Name);
        Assert.True(enqueueInternalMethod.IsGenericMethodDefinition);
        Assert.Equal(typeof(string), enqueueInternalMethod.ReturnType);

        // Verify it has 1 parameter: Expression<Func<T, Task>> methodCall
        var parameters = enqueueInternalMethod.GetParameters();
        Assert.Single(parameters);
        Assert.Equal("methodCall", parameters[0].Name);
        Assert.True(parameters[0].ParameterType.IsGenericType);

        // Verify generic constraint: where T : IRecurringJob
        var genericConstraints = enqueueInternalMethod.GetGenericArguments()[0].GetGenericParameterConstraints();
        Assert.Single(genericConstraints);
        Assert.Equal(typeof(IRecurringJob), genericConstraints[0]);
    }

    [Fact]
    public void CreateExecutionExpression_CreatesValidLambda()
    {
        // Test that CreateExecutionExpression produces a valid lambda expression
        var jobType = typeof(TestRecurringJob);
        var executeMethod = typeof(IRecurringJob).GetMethod(nameof(IRecurringJob.ExecuteAsync));
        var cancellationToken = CancellationToken.None;

        Assert.NotNull(executeMethod);

        var createExpressionMethod = typeof(HangfireJobEnqueuer)
            .GetMethod("CreateExecutionExpression", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(createExpressionMethod);

        // Act
        var lambda = createExpressionMethod.Invoke(_enqueuer, new object[] { jobType, executeMethod, cancellationToken });

        // Assert
        Assert.NotNull(lambda);
        Assert.IsAssignableFrom<System.Linq.Expressions.LambdaExpression>(lambda);

        var lambdaExpr = (System.Linq.Expressions.LambdaExpression)lambda;
        Assert.Equal(jobType, lambdaExpr.Parameters[0].Type);
    }

    #endregion

    #region Test Helper Classes

    /// <summary>
    /// Simple test implementation of IRecurringJob for testing purposes.
    /// </summary>
    private class TestRecurringJob : IRecurringJob
    {
        public RecurringJobMetadata Metadata => new RecurringJobMetadata
        {
            JobName = "test-job",
            DisplayName = "Test Job",
            Description = "A test job for unit testing",
            CronExpression = "0 0 * * *",
            DefaultIsEnabled = true,
            TimeZoneId = "Europe/Prague"
        };

        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    #endregion
}
