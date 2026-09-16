using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anela.Heblo.Tests.Controllers;

public class BaseApiControllerTests
{
    private sealed class ProbeResponse : BaseResponse
    {
        public ProbeResponse() { }

        public ProbeResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
            : base(errorCode, parameters) { }
    }

    private sealed class ProbeController : BaseApiController
    {
        public ActionResult<ProbeResponse> Probe(ProbeResponse response) => HandleResponse(response);
    }

    private readonly ProbeController _controller;

    public BaseApiControllerTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        _controller = new ProbeController
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    [Theory]
    [InlineData(ErrorCodes.InsufficientPermissions)]
    [InlineData(ErrorCodes.Forbidden)]
    public void HandleResponse_ForbiddenErrorCode_Returns403WithTheEnvelopeAsBody(ErrorCodes errorCode)
    {
        // Arrange - a handler-level permission refusal carries a user-facing message the
        // frontend reads from Params["ErrorMessage"]; a bodiless Forbid() would drop it.
        var response = new ProbeResponse(errorCode, new Dictionary<string, string>
        {
            ["ErrorMessage"] = "Nemáte oprávnění."
        });

        // Act
        var result = _controller.Probe(response);

        // Assert
        var objectResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        objectResult.Value.Should().BeSameAs(response);
    }

    [Fact]
    public void HandleResponse_Success_ReturnsOkWithTheResponse()
    {
        // Arrange
        var response = new ProbeResponse();

        // Act
        var result = _controller.Probe(response);

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeSameAs(response);
    }
}
