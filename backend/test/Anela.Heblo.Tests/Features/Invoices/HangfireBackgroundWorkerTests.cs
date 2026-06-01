using System.Reflection;
using Anela.Heblo.Xcc;
using Anela.Heblo.API.Infrastructure.Hangfire;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices;

public class HangfireBackgroundWorkerTests
{
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
}
