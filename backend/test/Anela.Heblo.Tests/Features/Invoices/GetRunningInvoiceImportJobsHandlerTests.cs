using Anela.Heblo.Xcc;
using Anela.Heblo.Application.Features.Invoices.UseCases.GetRunningInvoiceImportJobs;
using Anela.Heblo.Xcc.Services;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices;

public class GetRunningInvoiceImportJobsHandlerTests
{
    private static GetRunningInvoiceImportJobsHandler CreateHandler(
        IBackgroundWorker worker,
        IMemoryCache cache,
        int cacheSeconds = 2)
    {
        var options = Options.Create(new HangfireOptions
        {
            RunningJobsCacheSeconds = cacheSeconds
        });
        return new GetRunningInvoiceImportJobsHandler(
            worker,
            cache,
            options,
            NullLogger<GetRunningInvoiceImportJobsHandler>.Instance);
    }

    private static IMemoryCache NewCache() =>
        new MemoryCache(new MemoryCacheOptions());

    private static BackgroundJobInfo Job(string name, string state = "Processing", string id = "j1") =>
        new() { Id = id, JobName = name, State = state, Queue = "default" };

    [Fact]
    public async Task Handle_FiltersToInvoiceImportJobsOnly()
    {
        // Arrange
        var worker = new Mock<IBackgroundWorker>();
        worker.Setup(w => w.GetRunningJobs()).Returns(new List<BackgroundJobInfo>
        {
            Job("InvoiceImportJob.Run", id: "r1"),
            Job("SomeOtherJob.Run", id: "r2"),
        });
        worker.Setup(w => w.GetPendingJobs()).Returns(new List<BackgroundJobInfo>
        {
            Job("InvoiceImportJob.Run", state: "Enqueued", id: "p1"),
            Job("UnrelatedJob.Run", state: "Enqueued", id: "p2"),
        });

        var handler = CreateHandler(worker.Object, NewCache());

        // Act
        var result = await handler.Handle(new GetRunningInvoiceImportJobsRequest(), CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.Select(j => j.Id).Should().BeEquivalentTo(new[] { "r1", "p1" });
    }

    [Fact]
    public async Task Handle_CacheHit_DoesNotCallWorkerSecondTime()
    {
        // Arrange
        var worker = new Mock<IBackgroundWorker>();
        worker.Setup(w => w.GetRunningJobs()).Returns(new List<BackgroundJobInfo>
        {
            Job("InvoiceImportJob.Run", id: "r1"),
        });
        worker.Setup(w => w.GetPendingJobs()).Returns(new List<BackgroundJobInfo>());

        var cache = NewCache();
        var handler = CreateHandler(worker.Object, cache, cacheSeconds: 60);

        // Act
        var first = await handler.Handle(new GetRunningInvoiceImportJobsRequest(), CancellationToken.None);
        var second = await handler.Handle(new GetRunningInvoiceImportJobsRequest(), CancellationToken.None);

        // Assert
        first.Should().HaveCount(1);
        second.Should().BeSameAs(first); // returned from cache, exact same reference
        worker.Verify(w => w.GetRunningJobs(), Times.Once);
        worker.Verify(w => w.GetPendingJobs(), Times.Once);
    }

    [Fact]
    public async Task Handle_CacheDisabled_CallsWorkerOnEveryInvocation()
    {
        // Arrange
        var worker = new Mock<IBackgroundWorker>();
        worker.Setup(w => w.GetRunningJobs()).Returns(new List<BackgroundJobInfo>
        {
            Job("InvoiceImportJob.Run", id: "r1"),
        });
        worker.Setup(w => w.GetPendingJobs()).Returns(new List<BackgroundJobInfo>());

        var handler = CreateHandler(worker.Object, NewCache(), cacheSeconds: 0);

        // Act
        await handler.Handle(new GetRunningInvoiceImportJobsRequest(), CancellationToken.None);
        await handler.Handle(new GetRunningInvoiceImportJobsRequest(), CancellationToken.None);
        await handler.Handle(new GetRunningInvoiceImportJobsRequest(), CancellationToken.None);

        // Assert
        worker.Verify(w => w.GetRunningJobs(), Times.Exactly(3));
        worker.Verify(w => w.GetPendingJobs(), Times.Exactly(3));
    }

    [Fact]
    public async Task Handle_CacheDisabled_DoesNotWriteToCache()
    {
        // Arrange
        var worker = new Mock<IBackgroundWorker>();
        worker.Setup(w => w.GetRunningJobs()).Returns(new List<BackgroundJobInfo>
        {
            Job("InvoiceImportJob.Run", id: "r1"),
        });
        worker.Setup(w => w.GetPendingJobs()).Returns(new List<BackgroundJobInfo>());

        var cache = NewCache();
        var handler = CreateHandler(worker.Object, cache, cacheSeconds: 0);

        // Act
        await handler.Handle(new GetRunningInvoiceImportJobsRequest(), CancellationToken.None);

        // Assert
        cache.TryGetValue(GetRunningInvoiceImportJobsHandler.CacheKey, out _).Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WorkerThrows_ReturnsEmptyListAndDoesNotCache()
    {
        // Arrange
        var worker = new Mock<IBackgroundWorker>();
        worker.Setup(w => w.GetRunningJobs()).Throws(new InvalidOperationException("hangfire down"));

        var cache = NewCache();
        var handler = CreateHandler(worker.Object, cache, cacheSeconds: 60);

        // Act
        var result = await handler.Handle(new GetRunningInvoiceImportJobsRequest(), CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
        cache.TryGetValue(GetRunningInvoiceImportJobsHandler.CacheKey, out _).Should().BeFalse();
    }
}
