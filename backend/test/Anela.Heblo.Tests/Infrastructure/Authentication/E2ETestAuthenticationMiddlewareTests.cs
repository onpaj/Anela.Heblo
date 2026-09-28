using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using Anela.Heblo.API.Infrastructure.Authentication;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Infrastructure.Authentication;

public class E2ETestAuthenticationMiddlewareTests
{
    // ── shared helpers ───────────────────────────────────────────────────

    private static Mock<IWebHostEnvironment> CreateEnvironment(string environmentName)
    {
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(environmentName);
        return env;
    }

    private static E2ETestAuthenticationMiddleware CreateMiddleware(RequestDelegate next, IWebHostEnvironment environment)
        => new(next, NullLogger<E2ETestAuthenticationMiddleware>.Instance, environment);

    // Mocks IAuthenticationService so that context.AuthenticateAsync("E2ETestCookies")
    // fails deterministically (no cookie present), letting InvokeAsync fall through to
    // the X-E2E-Test-Token header path without standing up a real cookie handler.
    private static Mock<IAuthenticationService> CreateFailingCookieAuthService()
    {
        var authService = new Mock<IAuthenticationService>();
        authService
            .Setup(s => s.AuthenticateAsync(It.IsAny<HttpContext>(), "E2ETestCookies"))
            .ReturnsAsync(AuthenticateResult.NoResult());
        return authService;
    }

    // Builds a real, minimal DI container holding only the mocks a given test passes in.
    // A dependency the middleware should NOT reach for a given branch is simply never
    // registered here, so GetRequiredService<T>() throws (and fails the test) if the
    // middleware wrongly tries to resolve it.
    private static IServiceProvider BuildServices(
        Mock<IAuthenticationService>? authService = null,
        Mock<IServicePrincipalTokenValidator>? tokenValidator = null,
        Mock<IE2ESessionService>? sessionService = null)
    {
        var services = new ServiceCollection();
        if (authService != null) services.AddSingleton(authService.Object);
        if (tokenValidator != null) services.AddSingleton(tokenValidator.Object);
        if (sessionService != null) services.AddSingleton(sessionService.Object);
        return services.BuildServiceProvider();
    }

    private static HttpContext CreateContext(IServiceProvider services, string? e2eTestToken = null)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = services,
        };
        context.User = new ClaimsPrincipal(new ClaimsIdentity()); // unauthenticated by default
        context.Response.Body = new MemoryStream();

        if (e2eTestToken != null)
            context.Request.Headers["X-E2E-Test-Token"] = e2eTestToken;

        return context;
    }

    private static async Task<string> ReadResponseBodyAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    // ── environment guard (FR-1) ────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_ProductionEnvironment_PassesThroughWithoutAuthLogic()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(
            ctx => { nextCalled = true; return Task.CompletedTask; },
            CreateEnvironment("Production").Object);

        // No services registered at all: if the middleware wrongly tried to resolve
        // IAuthenticationService / IServicePrincipalTokenValidator / IE2ESessionService,
        // GetRequiredService would throw and fail this test.
        var context = CreateContext(BuildServices());

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.User.Identity!.IsAuthenticated.Should().BeFalse();
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Development")]
    public async Task InvokeAsync_StagingOrDevelopment_ProceedsPastEnvironmentGuard(string environmentName)
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(
            ctx => { nextCalled = true; return Task.CompletedTask; },
            CreateEnvironment(environmentName).Object);

        // Cookie auth fails, no token header -> falls through to next() via the
        // "no token header" path, proving it got past the environment guard.
        var context = CreateContext(BuildServices(authService: CreateFailingCookieAuthService()));

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    // ── already-authenticated short-circuit (FR-2) ──────────────────────────

    [Fact]
    public async Task InvokeAsync_AlreadyAuthenticated_PassesThroughWithoutCookieOrTokenLogic()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(
            ctx => { nextCalled = true; return Task.CompletedTask; },
            CreateEnvironment("Staging").Object);

        // No IAuthenticationService registered: if the middleware wrongly attempted
        // cookie authentication here, GetRequiredService<IAuthenticationService> would throw.
        var context = CreateContext(BuildServices());
        var existingIdentity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "Real User") }, "Bearer");
        context.User = new ClaimsPrincipal(existingIdentity);
        context.Request.Headers["X-E2E-Test-Token"] = "some-token"; // must be ignored

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.User.Identity!.AuthenticationType.Should().Be("Bearer");
        context.User.Identity.Name.Should().Be("Real User");
    }
}
