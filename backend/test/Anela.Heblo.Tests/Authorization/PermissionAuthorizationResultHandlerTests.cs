using System.Text.Json;
using System.Text.Json.Serialization;
using Anela.Heblo.API.Infrastructure.Authentication;
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class PermissionAuthorizationResultHandlerTests
{
    private sealed class SpyFallbackHandler : IAuthorizationMiddlewareResultHandler
    {
        public bool WasCalled { get; private set; }

        public Task HandleAsync(
            RequestDelegate next,
            HttpContext context,
            AuthorizationPolicy policy,
            PolicyAuthorizationResult authorizeResult)
        {
            WasCalled = true;
            return Task.CompletedTask;
        }
    }

    private static PermissionAuthorizationResultHandler CreateHandler(IAuthorizationMiddlewareResultHandler fallback)
    {
        var options = new Microsoft.AspNetCore.Mvc.JsonOptions();
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        return new PermissionAuthorizationResultHandler(Options.Create(options), fallback);
    }

    private static (HttpContext Context, MemoryStream Body) CreateContext()
    {
        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;
        return (context, body);
    }

    private static PolicyAuthorizationResult ForbiddenWithRoles(params string[] allowedRoles)
    {
        var requirement = new RolesAuthorizationRequirement(allowedRoles);
        var failure = AuthorizationFailure.Failed(new IAuthorizationRequirement[] { requirement });
        return PolicyAuthorizationResult.Forbid(failure);
    }

    private static async Task<string> ReadBodyAsync(MemoryStream body)
    {
        body.Position = 0;
        using var reader = new StreamReader(body, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    [Fact]
    public async Task HandleAsync_WritesRequiredPermission_WhenForbiddenWithRoleRequirement()
    {
        // Arrange
        var fallback = new SpyFallbackHandler();
        var handler = CreateHandler(fallback);
        var (context, body) = CreateContext();
        var policy = new AuthorizationPolicyBuilder().RequireRole(AccessRoles.ProductsCatalogWrite).Build();
        var result = ForbiddenWithRoles(AccessRoles.ProductsCatalogWrite);

        // Act
        await handler.HandleAsync(_ => Task.CompletedTask, context, policy, result);

        // Assert
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        fallback.WasCalled.Should().BeFalse();

        using var doc = JsonDocument.Parse(await ReadBodyAsync(body));
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("InsufficientPermissions");
        doc.RootElement.GetProperty("params").GetProperty("requiredPermission").GetString()
            .Should().Be(AccessRoles.ProductsCatalogWrite);
    }

    [Fact]
    public async Task HandleAsync_JoinsMultiplePermissions_WithOrSemantics()
    {
        // Arrange
        var fallback = new SpyFallbackHandler();
        var handler = CreateHandler(fallback);
        var (context, body) = CreateContext();
        var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        var result = ForbiddenWithRoles(AccessRoles.JobsTriggerRead, AccessRoles.JobsDisableRead);

        // Act
        await handler.HandleAsync(_ => Task.CompletedTask, context, policy, result);

        // Assert
        using var doc = JsonDocument.Parse(await ReadBodyAsync(body));
        doc.RootElement.GetProperty("params").GetProperty("requiredPermission").GetString()
            .Should().Be($"{AccessRoles.JobsTriggerRead}, {AccessRoles.JobsDisableRead}");
    }

    [Fact]
    public async Task HandleAsync_FiltersBaseRole_AndDelegatesToFallback_WhenNoFeaturePermissionRemains()
    {
        // Arrange — failing only on the baseline role carries no meaningful "required permission"
        var fallback = new SpyFallbackHandler();
        var handler = CreateHandler(fallback);
        var (context, body) = CreateContext();
        var policy = new AuthorizationPolicyBuilder().RequireRole(AccessRoles.Base).Build();
        var result = ForbiddenWithRoles(AccessRoles.Base);

        // Act
        await handler.HandleAsync(_ => Task.CompletedTask, context, policy, result);

        // Assert
        fallback.WasCalled.Should().BeTrue();
        (await ReadBodyAsync(body)).Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_DelegatesToFallback_WhenChallenged()
    {
        // Arrange — anonymous request (401 challenge) must keep the framework default behavior
        var fallback = new SpyFallbackHandler();
        var handler = CreateHandler(fallback);
        var (context, body) = CreateContext();
        var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        var result = PolicyAuthorizationResult.Challenge();

        // Act
        await handler.HandleAsync(_ => Task.CompletedTask, context, policy, result);

        // Assert
        fallback.WasCalled.Should().BeTrue();
        (await ReadBodyAsync(body)).Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_DelegatesToFallback_WhenSucceeded()
    {
        // Arrange
        var fallback = new SpyFallbackHandler();
        var handler = CreateHandler(fallback);
        var (context, body) = CreateContext();
        var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        var result = PolicyAuthorizationResult.Success();

        // Act
        await handler.HandleAsync(_ => Task.CompletedTask, context, policy, result);

        // Assert
        fallback.WasCalled.Should().BeTrue();
        (await ReadBodyAsync(body)).Should().BeEmpty();
    }
}
