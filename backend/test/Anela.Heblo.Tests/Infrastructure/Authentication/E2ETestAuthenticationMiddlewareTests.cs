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

    // ── cookie auth path (FR-3) ──────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_CookieAuthSucceeds_SetsUserAndCallsNext()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "e2e-test-user-id"),
            new Claim(ClaimTypes.Name, "E2E Test User"),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "E2ETestCookies"));

        var authService = new Mock<IAuthenticationService>();
        authService
            .Setup(s => s.AuthenticateAsync(It.IsAny<HttpContext>(), "E2ETestCookies"))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal, "E2ETestCookies")));

        var nextCalled = false;
        var middleware = CreateMiddleware(
            ctx => { nextCalled = true; return Task.CompletedTask; },
            CreateEnvironment("Staging").Object);

        // No IServicePrincipalTokenValidator/IE2ESessionService registered: if the
        // middleware wrongly fell through to the token-header path after a successful
        // cookie auth, resolving either would throw and fail this test.
        var context = CreateContext(BuildServices(authService: authService));
        context.Request.Headers["X-E2E-Test-Token"] = "should-be-ignored";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.User.Identity!.IsAuthenticated.Should().BeTrue();
        context.User.Identity.Name.Should().Be("E2E Test User");
        context.User.Identity.AuthenticationType.Should().Be("E2ETestCookies");
    }

    // ── missing/empty token header (FR-4) ─────────────────────────────────

    [Fact]
    public async Task InvokeAsync_NoTokenHeader_PassesThroughWithoutResolvingValidator()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(
            ctx => { nextCalled = true; return Task.CompletedTask; },
            CreateEnvironment("Staging").Object);

        // No IServicePrincipalTokenValidator/IE2ESessionService registered: enforces
        // that a missing header never reaches token-validation logic.
        var context = CreateContext(BuildServices(authService: CreateFailingCookieAuthService()));

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_EmptyTokenHeader_PassesThroughWithoutResolvingValidator()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(
            ctx => { nextCalled = true; return Task.CompletedTask; },
            CreateEnvironment("Staging").Object);

        var context = CreateContext(
            BuildServices(authService: CreateFailingCookieAuthService()),
            e2eTestToken: string.Empty);

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    // ── token validation failure (FR-5) ───────────────────────────────────

    [Fact]
    public async Task InvokeAsync_InvalidToken_Returns401AndDoesNotCallNext()
    {
        var tokenValidator = new Mock<IServicePrincipalTokenValidator>();
        tokenValidator.Setup(v => v.ValidateAsync("bad-token")).ReturnsAsync(false);

        var nextCalled = false;
        var middleware = CreateMiddleware(
            ctx => { nextCalled = true; return Task.CompletedTask; },
            CreateEnvironment("Staging").Object);

        // No IE2ESessionService registered: if the middleware wrongly proceeded to
        // create a synthetic user after an invalid token, resolving it would throw.
        var context = CreateContext(
            BuildServices(authService: CreateFailingCookieAuthService(), tokenValidator: tokenValidator),
            e2eTestToken: "bad-token");

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        (await ReadResponseBodyAsync(context)).Should().Be("Invalid E2E test token");
    }

    // ── token validation success (FR-6) ───────────────────────────────────

    [Fact]
    public async Task InvokeAsync_ValidToken_SetsSyntheticUserAndOverrideCookieAndCallsNext()
    {
        var tokenValidator = new Mock<IServicePrincipalTokenValidator>();
        tokenValidator.Setup(v => v.ValidateAsync("good-token")).ReturnsAsync(true);

        var syntheticClaims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "e2e-test-user-id"),
            new Claim(ClaimTypes.Name, "E2E Test User"),
        };
        var sessionService = new Mock<IE2ESessionService>();
        sessionService.Setup(s => s.CreateSyntheticUserClaims("Staging")).Returns(syntheticClaims);

        var nextCalled = false;
        var middleware = CreateMiddleware(
            ctx => { nextCalled = true; return Task.CompletedTask; },
            CreateEnvironment("Staging").Object);

        var context = CreateContext(
            BuildServices(
                authService: CreateFailingCookieAuthService(),
                tokenValidator: tokenValidator,
                sessionService: sessionService),
            e2eTestToken: "good-token");

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.User.Identity!.IsAuthenticated.Should().BeTrue();
        context.User.Identity.AuthenticationType.Should().Be("E2ETest");
        context.User.Identity.Name.Should().Be("E2E Test User");

        context.Response.Headers["Set-Cookie"].ToString().Should().Contain("E2E-Auth-Override=true");
    }

    // ── exception path (FR-7) ─────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_TokenValidatorThrows_Returns500AndDoesNotCallNext()
    {
        var tokenValidator = new Mock<IServicePrincipalTokenValidator>();
        tokenValidator.Setup(v => v.ValidateAsync("boom-token")).ThrowsAsync(new InvalidOperationException("boom"));

        var nextCalled = false;
        var middleware = CreateMiddleware(
            ctx => { nextCalled = true; return Task.CompletedTask; },
            CreateEnvironment("Staging").Object);

        var context = CreateContext(
            BuildServices(authService: CreateFailingCookieAuthService(), tokenValidator: tokenValidator),
            e2eTestToken: "boom-token");

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        (await ReadResponseBodyAsync(context)).Should().Be("Error validating E2E test token");
    }
}
