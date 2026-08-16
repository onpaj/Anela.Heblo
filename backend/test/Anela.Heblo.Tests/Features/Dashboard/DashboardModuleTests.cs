using Anela.Heblo.Application.Features.Dashboard;
using FluentAssertions;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard;

/// <summary>
/// Regression test: DashboardModule must not register Hangfire's JobStorage singleton.
///
/// Bug: DashboardModule.AddDashboardModule() registered the backend's only JobStorage
/// binding, even though nothing under Dashboard's own owned code consumes it. The real
/// consumers (HangfireBackgroundWorker, HangfireFailedJobCounter) live in
/// API/Infrastructure/Hangfire and are registered by AddHangfireServices. If
/// AddDashboardModule() were ever skipped or removed, those adapters would fail DI
/// resolution at startup with no discoverable root cause.
///
/// Fix: JobStorage is now registered inside AddHangfireServices, next to its consumers.
/// </summary>
public class DashboardModuleTests
{
    [Fact]
    public void AddDashboardModule_DoesNotRegisterJobStorage()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddDashboardModule();

        // Assert
        services
            .Any(d => d.ServiceType == typeof(JobStorage))
            .Should().BeFalse(
                "JobStorage is consumed by HangfireBackgroundWorker and HangfireFailedJobCounter " +
                "in the API project and must be registered in AddHangfireServices, not in the " +
                "unrelated DashboardModule");
    }
}
