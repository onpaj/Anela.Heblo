using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Anela.Heblo.Application.Features.Photobank.UseCases.AddRoot;
using Anela.Heblo.Application.Features.Photobank.UseCases.AddRule;
using Anela.Heblo.Application.Features.Photobank.UseCases.RetagPhotos;
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

    [Fact]
    public async Task AddRule_MapsBodyToRequest_AndReturnsCreated()
    {
        // Arrange
        var body = new AddRuleBody
        {
            PathPattern = "/sites/marketing/2026/**",
            TagName = "marketing-2026",
            SortOrder = 5,
        };

        AddRuleRequest? captured = null;
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<AddRuleRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<AddRuleResponse>, CancellationToken>((req, _) => captured = (AddRuleRequest)req)
            .ReturnsAsync(new AddRuleResponse { Id = 7, Success = true });

        // Act
        var result = await _controller.AddRule(body, CancellationToken.None);

        // Assert
        captured.Should().NotBeNull();
        captured!.PathPattern.Should().Be(body.PathPattern);
        captured.TagName.Should().Be(body.TagName);
        captured.SortOrder.Should().Be(body.SortOrder);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.ActionName.Should().Be(nameof(PhotobankController.GetRules));
        var payload = created.Value.Should().BeOfType<AddRuleResponse>().Subject;
        payload.Id.Should().Be(7);
    }

    [Fact]
    public async Task RetagPhotos_MapsBodyToRequest_AndReturnsAccepted()
    {
        // Arrange
        var body = new RetagPhotosBody
        {
            PhotoIds = new[] { 11, 22, 33 },
            ClearExistingAiTags = true,
        };

        RetagPhotosRequest? captured = null;
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<RetagPhotosRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<RetagPhotosResponse>, CancellationToken>((req, _) => captured = (RetagPhotosRequest)req)
            .ReturnsAsync(new RetagPhotosResponse { JobId = "job-abc", Success = true });

        // Act
        var result = await _controller.RetagPhotos(body, CancellationToken.None);

        // Assert
        captured.Should().NotBeNull();
        captured!.PhotoIds.Should().BeEquivalentTo(body.PhotoIds);
        captured.ClearExistingAiTags.Should().Be(body.ClearExistingAiTags);

        var accepted = result.Result.Should().BeOfType<AcceptedResult>().Subject;
        var payload = accepted.Value.Should().BeOfType<RetagPhotosResponse>().Subject;
        payload.JobId.Should().Be("job-abc");
    }
}
