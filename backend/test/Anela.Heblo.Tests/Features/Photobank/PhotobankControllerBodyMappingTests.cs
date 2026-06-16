using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Anela.Heblo.Application.Features.Photobank.UseCases.AddRoot;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public sealed class PhotobankControllerBodyMappingTests
{
    private readonly Mock<IMediator> _mediatorMock = new();
    private readonly PhotobankController _controller;

    public PhotobankControllerBodyMappingTests()
    {
        _controller = new PhotobankController(_mediatorMock.Object);
        SetupHttpContext();
    }

    private void SetupHttpContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, "test-user") })),
            RequestServices = services.BuildServiceProvider()
        };

        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    [Fact]
    public async Task AddRoot_MapsBodyToRequest_AndReturnsCreated()
    {
        // Arrange
        var body = new AddRootBody
        {
            SharePointPath = "/sites/marketing/photos",
            DisplayName = "Marketing photos",
            DriveId = "drive-abc-123",
        };

        AddRootRequest? captured = null;
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<AddRootRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<AddRootResponse>, CancellationToken>((req, _) => captured = (AddRootRequest)req)
            .ReturnsAsync(new AddRootResponse { Id = 42, Success = true });

        // Act
        var result = await _controller.AddRoot(body, CancellationToken.None);

        // Assert
        captured.Should().NotBeNull();
        captured!.SharePointPath.Should().Be(body.SharePointPath);
        captured.DisplayName.Should().Be(body.DisplayName);
        captured.DriveId.Should().Be(body.DriveId);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.ActionName.Should().Be(nameof(PhotobankController.GetRoots));
        var payload = created.Value.Should().BeOfType<AddRootResponse>().Subject;
        payload.Id.Should().Be(42);
    }
}
