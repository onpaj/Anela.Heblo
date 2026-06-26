using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;
using Anela.Heblo.Application.Features.UserManagement.Validators;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.UserManagement;

public class GetGroupMembersValidationPipelineTests
{
    private static IMediator BuildMediator(Mock<IGraphService>? graphServiceMock = null)
    {
        var services = new ServiceCollection();

        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<GetGroupMembersRequest>());

        services.AddScoped<IValidator<GetGroupMembersRequest>, GetGroupMembersRequestValidator>();
        services.AddScoped<
            IPipelineBehavior<GetGroupMembersRequest, GetGroupMembersResponse>,
            ValidationBehavior<GetGroupMembersRequest, GetGroupMembersResponse>>();

        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));

        services.AddScoped<IGraphService>(_ => (graphServiceMock ?? new Mock<IGraphService>()).Object);

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Send_WithInvalidGroupId_ThrowsValidationExceptionBeforeHandler(string? groupId)
    {
        // Arrange
        var graphService = new Mock<IGraphService>(MockBehavior.Strict);
        var mediator = BuildMediator(graphService);
        var request = new GetGroupMembersRequest { GroupId = groupId! };

        // Act
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => mediator.Send(request, CancellationToken.None));

        // Assert
        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(GetGroupMembersRequest.GroupId)
                                        && e.ErrorMessage == "GroupId is required.");
        graphService.VerifyNoOtherCalls(); // handler/service never reached
    }

    [Fact]
    public async Task Send_WithValidGroupId_ReachesHandler()
    {
        // Arrange
        var graphService = new Mock<IGraphService>();
        graphService
            .Setup(g => g.GetGroupMembersAsync("valid-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserDto>());
        var mediator = BuildMediator(graphService);

        // Act
        var response = await mediator.Send(
            new GetGroupMembersRequest { GroupId = "valid-id" },
            CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        graphService.Verify(
            g => g.GetGroupMembersAsync("valid-id", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
