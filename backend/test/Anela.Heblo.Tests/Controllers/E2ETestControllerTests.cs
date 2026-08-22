using Anela.Heblo.API.Controllers;
using Anela.Heblo.API.Infrastructure.Authentication;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Controllers;

public class E2ETestControllerTests
{
    private static E2ETestController CreateController(string environmentName)
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.Setup(e => e.EnvironmentName).Returns(environmentName);

        var configuration = new Mock<IConfiguration>();
        var tokenValidator = new Mock<IServicePrincipalTokenValidator>();
        var sessionService = new Mock<IE2ESessionService>();

        return new E2ETestController(
            NullLogger<E2ETestController>.Instance,
            environment.Object,
            configuration.Object,
            tokenValidator.Object,
            sessionService.Object);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Test")]
    [InlineData("QA")]
    public void GetEnvironmentInfo_OutsideStagingOrDevelopment_ShouldReturnNotFound(string environmentName)
    {
        var controller = CreateController(environmentName);

        var result = controller.GetEnvironmentInfo();

        var notFound = result.Result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var propertyNames = notFound.Value!.GetType().GetProperties().Select(p => p.Name).ToArray();
        propertyNames.Should().Contain(new[] { "error", "currentEnvironment" });

        var currentEnvironment = (string)notFound.Value!.GetType().GetProperty("currentEnvironment")!.GetValue(notFound.Value)!;
        currentEnvironment.Should().Be(environmentName);
    }

    [Fact]
    public void GetEnvironmentInfo_OutsideStagingOrDevelopment_ShouldNotLeakEnvironmentVariables()
    {
        var controller = CreateController("Production");

        var result = controller.GetEnvironmentInfo();

        var notFound = result.Result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var propertyNames = notFound.Value!.GetType().GetProperties().Select(p => p.Name).ToArray();
        propertyNames.Should().NotContain("environmentVariables");
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Development")]
    public void GetEnvironmentInfo_InStagingOrDevelopment_ShouldReturnOkWithEnvironmentDetails(string environmentName)
    {
        var controller = CreateController(environmentName);

        var result = controller.GetEnvironmentInfo();

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var value = ok.Value!;
        var environmentProperty = (string)value.GetType().GetProperty("environment")!.GetValue(value)!;
        environmentProperty.Should().Be(environmentName);
    }
}
