using Anela.Heblo.API.Extensions;
using FluentAssertions;
using Hangfire;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Infrastructure;

/// <summary>
/// Regression test: AddHangfireServices must register the JobStorage singleton that
/// HangfireBackgroundWorker and HangfireFailedJobCounter depend on, since it is the one
/// module where every other Hangfire adapter is already registered. See
/// Anela.Heblo.Tests.Features.Dashboard.DashboardModuleTests for the companion assertion
/// that DashboardModule no longer owns this registration.
/// </summary>
public class HangfireServicesTests
{
    [Fact]
    public void AddHangfireServices_RegistersJobStorage()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Hangfire:UseInMemoryStorage"] = "true",
                ["Hangfire:WorkerCount"] = "1",
                ["Hangfire:SchemaName"] = "hangfire",
                ["Hangfire:ConnectionLimit"] = "0",
            })
            .Build();
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns("Test");

        // Act
        services.AddHangfireServices(configuration, environment.Object);

        // Assert
        services
            .Any(d => d.ServiceType == typeof(JobStorage))
            .Should().BeTrue(
                "AddHangfireServices must register JobStorage next to the Hangfire adapters " +
                "(HangfireBackgroundWorker, HangfireFailedJobCounter) that consume it");
    }
}
