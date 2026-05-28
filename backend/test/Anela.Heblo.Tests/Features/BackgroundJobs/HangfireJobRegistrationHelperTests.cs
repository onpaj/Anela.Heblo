using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Tests.Features.BackgroundJobs.Infrastructure;
using Hangfire;
using Hangfire.Storage;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

[Collection("Hangfire")]
public class HangfireJobRegistrationHelperTests : IDisposable
{
    public HangfireJobRegistrationHelperTests(HangfireTestFixture fixture)
    {
        // Hangfire is initialized by the shared collection fixture.
    }

    [Fact]
    public void RegisterOrUpdate_WithValidInputs_RegistersJobInHangfireStorage()
    {
        // Arrange
        const string jobName = "helper-test-job";
        const string cron = "0 5 * * *";
        const string tz = "Europe/Prague";

        // Act
        HangfireJobRegistrationHelper.RegisterOrUpdate(
            typeof(HelperTestRecurringJob),
            jobName,
            cron,
            tz);

        // Assert
        using var connection = JobStorage.Current.GetConnection();
        var job = connection.GetRecurringJobs().SingleOrDefault(j => j.Id == jobName);
        Assert.NotNull(job);
        Assert.Equal(cron, job.Cron);
        Assert.Equal(tz, job.TimeZoneId);
        // The helper must register the async overload (returns Task)
        Assert.Equal(typeof(Task), job.Job.Method.ReturnType);
    }

    [Fact]
    public void RegisterOrUpdate_CalledTwice_UpdatesCronOnSecondCall()
    {
        // Arrange
        const string jobName = "helper-test-update-job";
        const string firstCron = "0 1 * * *";
        const string secondCron = "0 2 * * *";

        // Act — register, then re-register with new CRON
        HangfireJobRegistrationHelper.RegisterOrUpdate(
            typeof(HelperTestRecurringJob), jobName, firstCron, "Europe/Prague");
        HangfireJobRegistrationHelper.RegisterOrUpdate(
            typeof(HelperTestRecurringJob), jobName, secondCron, "Europe/Prague");

        // Assert
        using var connection = JobStorage.Current.GetConnection();
        var job = Assert.Single(connection.GetRecurringJobs(), j => j.Id == jobName);
        Assert.Equal(secondCron, job.Cron);
    }

    [Fact]
    public void RegisterOrUpdate_WithNullJobType_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            HangfireJobRegistrationHelper.RegisterOrUpdate(
                null!, "name", "0 0 * * *", "Europe/Prague"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RegisterOrUpdate_WithMissingJobName_ThrowsArgumentException(string? jobName)
    {
        Assert.Throws<ArgumentException>(() =>
            HangfireJobRegistrationHelper.RegisterOrUpdate(
                typeof(HelperTestRecurringJob), jobName!, "0 0 * * *", "Europe/Prague"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RegisterOrUpdate_WithMissingCron_ThrowsArgumentException(string? cron)
    {
        Assert.Throws<ArgumentException>(() =>
            HangfireJobRegistrationHelper.RegisterOrUpdate(
                typeof(HelperTestRecurringJob), "name", cron!, "Europe/Prague"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RegisterOrUpdate_WithMissingTimeZoneId_ThrowsArgumentException(string? tz)
    {
        Assert.Throws<ArgumentException>(() =>
            HangfireJobRegistrationHelper.RegisterOrUpdate(
                typeof(HelperTestRecurringJob), "name", "0 0 * * *", tz!));
    }

    [Fact]
    public void RegisterOrUpdate_WithTypeNotImplementingIRecurringJob_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            HangfireJobRegistrationHelper.RegisterOrUpdate(
                typeof(NotARecurringJob), "name", "0 0 * * *", "Europe/Prague"));
        Assert.Equal("jobType", ex.ParamName);
    }

    [Fact]
    public void RegisterOrUpdate_WithInvalidTimeZoneId_ThrowsUnwrappedTimeZoneNotFoundException()
    {
        // The helper must unwrap TargetInvocationException so callers see the
        // real cause (TimeZoneNotFoundException), not the reflection wrapper.
        Assert.Throws<TimeZoneNotFoundException>(() =>
            HangfireJobRegistrationHelper.RegisterOrUpdate(
                typeof(HelperTestRecurringJob),
                "helper-test-tz-job",
                "0 0 * * *",
                "Not/A/Real/TimeZone"));
    }

    public void Dispose()
    {
        using var connection = JobStorage.Current.GetConnection();
        foreach (var job in connection.GetRecurringJobs())
        {
            RecurringJob.RemoveIfExists(job.Id);
        }
    }

    private class HelperTestRecurringJob : IRecurringJob
    {
        public RecurringJobMetadata Metadata { get; } = new()
        {
            JobName = "helper-test-job",
            DisplayName = "Helper Test Job",
            Description = "Used by HangfireJobRegistrationHelperTests",
            CronExpression = "0 0 * * *",
            TimeZoneId = "Europe/Prague",
        };

        public Task ExecuteAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private class NotARecurringJob
    {
        // Used to assert the helper rejects types that do not implement IRecurringJob.
    }
}
