using System.Reflection;
using Anela.Heblo.API.Controllers;
using FluentAssertions;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Controllers;

public class DiagnosticsControllerTests
{
    private static DiagnosticsController CreateController(string environmentName, out RecordingTelemetryChannel channel)
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.Setup(e => e.EnvironmentName).Returns(environmentName);

        channel = new RecordingTelemetryChannel();
        var telemetryConfiguration = new TelemetryConfiguration { TelemetryChannel = channel };
        var telemetryClient = new TelemetryClient(telemetryConfiguration)
        {
            InstrumentationKey = "11111111-1111-1111-1111-111111111111"
        };

        return new DiagnosticsController(NullLogger<DiagnosticsController>.Instance, telemetryClient, environment.Object);
    }

    [Fact]
    public void Controller_ShouldRequireAuthorization()
    {
        typeof(DiagnosticsController).GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
    }

    [Theory]
    [InlineData(nameof(DiagnosticsController.TestLogging))]
    [InlineData(nameof(DiagnosticsController.TestException))]
    [InlineData(nameof(DiagnosticsController.Health))]
    [InlineData(nameof(DiagnosticsController.GetApplicationInsightsConfig))]
    public void Actions_ShouldNotAllowAnonymous(string actionName)
    {
        var method = typeof(DiagnosticsController).GetMethod(actionName);
        method!.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
    }

    [Fact]
    public void TestLogging_InProduction_ShouldReturnNotFoundAndEmitNoTelemetry()
    {
        var controller = CreateController("Production", out var channel);

        var result = controller.TestLogging();

        result.Should().BeOfType<NotFoundResult>();
        channel.SentItems.Should().BeEmpty();
    }

    [Fact]
    public void TestException_InProduction_ShouldReturnNotFoundAndEmitNoTelemetry()
    {
        var controller = CreateController("Production", out var channel);

        var result = controller.TestException();

        result.Should().BeOfType<NotFoundResult>();
        channel.SentItems.Should().BeEmpty();
    }

    [Fact]
    public void Health_InProduction_ShouldReturnNotFound()
    {
        var controller = CreateController("Production", out _);

        var result = controller.Health();

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public void GetApplicationInsightsConfig_InProduction_ShouldReturnNotFound()
    {
        var controller = CreateController("Production", out _);

        var result = controller.GetApplicationInsightsConfig();

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public void TestLogging_InDevelopment_ShouldReturnOkAndEmitTelemetry()
    {
        var controller = CreateController("Development", out var channel);

        var result = controller.TestLogging();

        result.Should().BeOfType<OkObjectResult>();
        channel.SentItems.Should().NotBeEmpty();
    }

    [Fact]
    public void TestException_InDevelopment_ShouldReturn500AndEmitTelemetry()
    {
        var controller = CreateController("Development", out var channel);

        var result = controller.TestException();

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(500);
        channel.SentItems.Should().NotBeEmpty();
    }

    [Fact]
    public void Health_InDevelopment_ShouldReturnOk()
    {
        var controller = CreateController("Development", out _);

        var result = controller.Health();

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public void GetApplicationInsightsConfig_InDevelopment_ShouldNotExposeKeyMaterial()
    {
        var controller = CreateController("Development", out _);

        var result = controller.GetApplicationInsightsConfig();

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var propertyNames = okResult.Value!.GetType().GetProperties().Select(p => p.Name).ToArray();

        propertyNames.Should().NotContain("InstrumentationKey");
        propertyNames.Should().NotContain("ConnectionStringSource");
        propertyNames.Should().Contain(new[] { "HasConnectionString", "HasInstrumentationKey", "Environment", "CloudRole", "CloudRoleInstance" });
    }

    [Fact]
    public void GetApplicationInsightsConfig_WhenConfigured_ShouldReportHasInstrumentationKeyTrue()
    {
        var controller = CreateController("Development", out _);

        var result = controller.GetApplicationInsightsConfig();

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var hasInstrumentationKey = (bool)okResult.Value!.GetType().GetProperty("HasInstrumentationKey")!.GetValue(okResult.Value)!;
        hasInstrumentationKey.Should().BeTrue();
    }

    [Fact]
    public void GetApplicationInsightsConfig_WhenNotConfigured_ShouldReportHasConnectionStringFalse()
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.Setup(e => e.EnvironmentName).Returns("Development");

        var channel = new RecordingTelemetryChannel();
        var telemetryConfiguration = new TelemetryConfiguration { TelemetryChannel = channel };
        var telemetryClient = new TelemetryClient(telemetryConfiguration);
        var controller = new DiagnosticsController(NullLogger<DiagnosticsController>.Instance, telemetryClient, environment.Object);

        Environment.SetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING", null);
        Environment.SetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY", null);

        var result = controller.GetApplicationInsightsConfig();

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var hasConnectionString = (bool)okResult.Value!.GetType().GetProperty("HasConnectionString")!.GetValue(okResult.Value)!;
        hasConnectionString.Should().BeFalse();
    }

    private class RecordingTelemetryChannel : ITelemetryChannel
    {
        public List<ITelemetry> SentItems { get; } = new();

        public bool? DeveloperMode { get; set; }
        public string EndpointAddress { get; set; } = string.Empty;

        public void Send(ITelemetry item) => SentItems.Add(item);
        public void Flush() { }
        public void Dispose() { }
    }
}
