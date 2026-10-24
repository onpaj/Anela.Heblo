using System.Collections.Generic;
using System.Linq;
using Anela.Heblo.Application;
using Anela.Heblo.Application.Features.OrgChart;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Features.OrgChart;

/// <summary>
/// Guards that ApplicationModule.AddApplicationServices wires up the OrgChart module,
/// so its startup options validation is not silently dropped.
/// </summary>
public sealed class OrgChartModuleWiringTests
{
    [Fact]
    public void AddApplicationServices_registers_OrgChart_options_validation()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var services = new ServiceCollection();

        // Act
        services.AddApplicationServices(configuration, new DevelopmentEnvironment());

        // Assert
        services.Should().Contain(d => d.ServiceType == typeof(IValidateOptions<OrgChartOptions>));
    }

    private sealed class DevelopmentEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Anela.Heblo.Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
