using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Anela.Heblo.API.Infrastructure.Hangfire;
using Anela.Heblo.Tests.Features.BackgroundJobs.Infrastructure;
using Anela.Heblo.Xcc;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices;

[Collection("Hangfire")]
public class HangfireBackgroundWorkerTests
{
    private readonly HangfireBackgroundWorker _worker;

    public HangfireBackgroundWorkerTests(HangfireTestFixture fixture)
    {
        // HangfireTestFixture (shared via the "Hangfire" collection) configures
        // JobStorage.Current to an in-memory Hangfire.MemoryStorage instance once
        // for the whole test run — see HangfireTestFixture.cs.
        _worker = new HangfireBackgroundWorker(Options.Create(new HangfireOptions()));
    }

    [Fact]
    public void Constructor_StoresHangfireOptions()
    {
        // Arrange
        var options = Options.Create(new HangfireOptions { MaxPendingJobsPageSize = 200 });

        // Act
        var worker = new HangfireBackgroundWorker(options);

        // Assert — the worker must hold the options so its monitoring calls use the cap.
        var stored = typeof(HangfireBackgroundWorker)
            .GetField("_options", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(worker) as HangfireOptions;

        stored.Should().NotBeNull();
        stored!.MaxPendingJobsPageSize.Should().Be(200);
    }

    [Fact]
    public void Constructor_AcceptsCustomPageSize()
    {
        // Arrange
        var options = Options.Create(new HangfireOptions { MaxPendingJobsPageSize = 50 });

        // Act
        var worker = new HangfireBackgroundWorker(options);

        // Assert
        var stored = typeof(HangfireBackgroundWorker)
            .GetField("_options", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(worker) as HangfireOptions;

        stored!.MaxPendingJobsPageSize.Should().Be(50);
    }

    #region GetJobById / GetJobStartedAt state coverage (targeted GetStateData lookup)

    [Fact]
    public void GetJobById_ProcessingStateWithValidStartedAt_ReturnsMatchingDateTime()
    {
        // Arrange: a job whose current Hangfire state is "Processing" and whose
        // state Data dictionary carries a valid, parseable "StartedAt" entry.
        var jobId = CreateEnqueuedJob();
        var expectedStartedAt = new DateTime(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc);
        SeedJobState(jobId, ProcessingState.StateName, new Dictionary<string, string>
        {
            ["StartedAt"] = JobHelper.SerializeDateTime(expectedStartedAt)
        });

        // Act
        var result = _worker.GetJobById(jobId);

        // Assert
        result.Should().NotBeNull();
        result!.State.Should().Be("Processing");
        result.StartedAt.Should().Be(expectedStartedAt);
    }

    [Fact]
    public void GetJobById_NonProcessingState_ReturnsNullStartedAt()
    {
        // Arrange: job is created directly into the "Enqueued" state and never
        // transitioned to "Processing".
        var jobId = CreateEnqueuedJob();

        // Act
        var result = _worker.GetJobById(jobId);

        // Assert
        result.Should().NotBeNull();
        result!.State.Should().Be("Enqueued");
        result.StartedAt.Should().BeNull();
    }

    [Fact]
    public void GetJobById_ProcessingStateWithMissingStartedAtKey_ReturnsNullStartedAt()
    {
        // Arrange: state name is "Processing" but the state Data dictionary has
        // no "StartedAt" entry at all.
        var jobId = CreateEnqueuedJob();
        SeedJobState(jobId, ProcessingState.StateName, new Dictionary<string, string>());

        // Act
        var result = _worker.GetJobById(jobId);

        // Assert
        result.Should().NotBeNull();
        result!.State.Should().Be("Processing");
        result.StartedAt.Should().BeNull();
    }

    [Fact]
    public void GetJobById_NonexistentJobId_ReturnsNull()
    {
        // Act
        var result = _worker.GetJobById("nonexistent-job-id-does-not-exist");

        // Assert
        result.Should().BeNull();
    }

    private static string CreateEnqueuedJob()
    {
        var client = new BackgroundJobClient(JobStorage.Current);
        Expression<Action> methodCall = () => Console.WriteLine("test job");
        var job = Job.FromExpression(methodCall);
        return client.Create(job, new EnqueuedState());
    }

    /// <summary>
    /// Seeds the given job directly into the given state/data via a write transaction,
    /// bypassing Hangfire's normal state-transition pipeline. This is necessary because
    /// Hangfire.States.ProcessingState's constructor is internal and cannot be
    /// instantiated from test code; FakeState below stands in for it, carrying only
    /// the (Name, Data) pair that HangfireBackgroundWorker.GetJobStartedAt reads.
    /// </summary>
    private static void SeedJobState(string jobId, string stateName, Dictionary<string, string> data)
    {
        using var connection = JobStorage.Current.GetConnection();
        using var transaction = connection.CreateWriteTransaction();
        transaction.SetJobState(jobId, new FakeState(stateName, data));
        transaction.Commit();
    }

    private sealed class FakeState : IState
    {
        private readonly Dictionary<string, string> _data;

        public FakeState(string name, Dictionary<string, string> data)
        {
            Name = name;
            _data = data;
        }

        public string Name { get; }
        public string? Reason => null;
        public bool IsFinal => false;
        public bool IgnoreJobLoadException => false;
        public Dictionary<string, string> SerializeData() => _data;
    }

    #endregion
}
