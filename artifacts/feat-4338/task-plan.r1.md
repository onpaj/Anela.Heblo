# E2ETestAuthenticationMiddleware Unit Test Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Raise `E2ETestAuthenticationMiddleware.cs` line coverage from 6.7% to comfortably above the 60% threshold by adding a focused xUnit unit test suite that exercises every branch of `InvokeAsync` and every row of the `ShouldBeRegistered` guard matrix, with zero production code changes.

**Architecture:** One new test file, `E2ETestAuthenticationMiddlewareTests.cs`, added next to the existing `ServicePrincipalTokenValidatorTests.cs` / `E2ESessionServiceTests.cs` in `backend/test/Anela.Heblo.Tests/Infrastructure/Authentication/`. Mocks `IWebHostEnvironment`, `IAuthenticationService` (to make `context.AuthenticateAsync("E2ETestCookies")` succeed/fail deterministically without a real cookie handler), `IServicePrincipalTokenValidator`, and `IE2ESessionService`, wired into `HttpContext.RequestServices` via a real, minimal `ServiceCollection`/`BuildServiceProvider()`. Only services a given test actually needs are registered — an unregistered dependency being reached throws via `GetRequiredService`, which doubles as a hard assertion that a branch under test does not fall through into logic it shouldn't reach.

**Tech Stack:** .NET 8, xUnit, Moq, FluentAssertions, `Microsoft.AspNetCore.Http`/`Authentication`/`Hosting` test doubles (`DefaultHttpContext`, `IAuthenticationService`, `ConfigurationBuilder.AddInMemoryCollection`).

---

## Spec coverage map

| Spec requirement | Task |
|---|---|
| FR-1 Environment guard | task: scaffold-file-and-environment-guard-tests |
| FR-2 Already-authenticated short-circuit | task: scaffold-file-and-environment-guard-tests |
| FR-3 Cookie auth success path | task: cookie-and-missing-token-tests |
| FR-4 Missing/empty token header passthrough | task: cookie-and-missing-token-tests |
| FR-5 Token validation failure (401) | task: token-validation-outcome-tests |
| FR-6 Token validation success (synthetic user + cookie) | task: token-validation-outcome-tests |
| FR-7 Exception path (500) | task: token-validation-outcome-tests |
| FR-8 `ShouldBeRegistered` matrix | task: should-be-registered-and-final-verification |
| NFR-1 Test isolation/determinism | all tasks (no real network/time/shared state used) |
| NFR-2 No production code changes | all tasks (only the new test file is touched) |
| NFR-3 Convention consistency | all tasks (mirrors `McpBadRequestMiddlewareTests.cs` structure) |
| NFR-4 Coverage target | task: should-be-registered-and-final-verification (final build+test run confirms) |

All four tasks append to the **same** file, `backend/test/Anela.Heblo.Tests/Infrastructure/Authentication/E2ETestAuthenticationMiddlewareTests.cs`, in order. Do not run these tasks out of order or in parallel against the same branch — each depends on the file state the previous task left behind.

---

### task: scaffold-file-and-environment-guard-tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Infrastructure/Authentication/E2ETestAuthenticationMiddlewareTests.cs`

This task creates the test file with all shared helper methods (used by every later task) plus the environment-guard tests (FR-1) and the already-authenticated short-circuit test (FR-2).

- [ ] **Step 1: Create the test file with usings, class declaration, and shared helpers**

```csharp
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
}
```

- [ ] **Step 2: Add the environment guard tests (FR-1) and already-authenticated test (FR-2) inside the class, after the helpers**

Insert this immediately before the class's closing `}`:

```csharp

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
```

- [ ] **Step 3: Run the new tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~E2ETestAuthenticationMiddlewareTests"`
Expected: 4 tests discovered (`InvokeAsync_ProductionEnvironment_PassesThroughWithoutAuthLogic`, 2 cases of `InvokeAsync_StagingOrDevelopment_ProceedsPastEnvironmentGuard`, `InvokeAsync_AlreadyAuthenticated_PassesThroughWithoutCookieOrTokenLogic`), all PASS.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Infrastructure/Authentication/E2ETestAuthenticationMiddlewareTests.cs
git commit -m "test(feat-4338): add E2ETestAuthenticationMiddleware environment guard tests"
```

---

### task: cookie-and-missing-token-tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Infrastructure/Authentication/E2ETestAuthenticationMiddlewareTests.cs` (append inside the class, after the already-authenticated test from the previous task, before the final closing `}`)

This task adds the cookie-authentication-success test (FR-3) and the missing/empty token header tests (FR-4).

- [ ] **Step 1: Write the failing^H^H^H^H^H^H new tests**

Insert this immediately before the class's closing `}` (after `InvokeAsync_AlreadyAuthenticated_PassesThroughWithoutCookieOrTokenLogic`):

```csharp

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
        // no X-E2E-Test-Token header set

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
```

(Note: the strikethrough in this step's title is intentional flavor text acknowledging that, per NFR-2, production code already implements this behavior — these tests are written to characterize existing behavior, not to drive new implementation. They are expected to PASS on first run, not fail.)

- [ ] **Step 2: Run the new tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~E2ETestAuthenticationMiddlewareTests"`
Expected: 7 tests total now (the 4 from the previous task plus `InvokeAsync_CookieAuthSucceeds_SetsUserAndCallsNext`, `InvokeAsync_NoTokenHeader_PassesThroughWithoutResolvingValidator`, `InvokeAsync_EmptyTokenHeader_PassesThroughWithoutResolvingValidator`), all PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Infrastructure/Authentication/E2ETestAuthenticationMiddlewareTests.cs
git commit -m "test(feat-4338): add E2ETestAuthenticationMiddleware cookie and missing-token tests"
```

---

### task: token-validation-outcome-tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Infrastructure/Authentication/E2ETestAuthenticationMiddlewareTests.cs` (append inside the class, after the missing-token tests from the previous task, before the final closing `}`)

This task adds the invalid-token (FR-5), valid-token (FR-6), and exception-path (FR-7) tests.

- [ ] **Step 1: Add the new tests**

Insert this immediately before the class's closing `}` (after `InvokeAsync_EmptyTokenHeader_PassesThroughWithoutResolvingValidator`):

```csharp

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
```

- [ ] **Step 2: Run the new tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~E2ETestAuthenticationMiddlewareTests"`
Expected: 10 tests total now, all PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Infrastructure/Authentication/E2ETestAuthenticationMiddlewareTests.cs
git commit -m "test(feat-4338): add E2ETestAuthenticationMiddleware token validation outcome tests"
```

---

### task: should-be-registered-and-final-verification

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Infrastructure/Authentication/E2ETestAuthenticationMiddlewareTests.cs` (append inside the class, after the exception-path test from the previous task, before the final closing `}`)

This task adds the `ShouldBeRegistered` guard-matrix tests (FR-8), then runs the full validation suite (build, format, full test run) to confirm the coverage target from NFR-4 is met and nothing else broke.

- [ ] **Step 1: Add the `ShouldBeRegistered` theory**

Insert this immediately before the class's closing `}` (after `InvokeAsync_TokenValidatorThrows_Returns500AndDoesNotCallNext`):

```csharp

    // ── ShouldBeRegistered guard matrix (FR-8) ────────────────────────────

    [Theory]
    [InlineData("Development", null, true)]
    [InlineData("Staging", null, true)]
    [InlineData("Production", null, false)]
    [InlineData("Staging", true, false)]
    [InlineData("Development", true, false)]
    [InlineData("Staging", false, true)]
    [InlineData("IntegrationTest", null, false)]
    [InlineData("IntegrationTest", true, false)]
    public void ShouldBeRegistered_ReturnsExpected(string environmentName, bool? useMockAuth, bool expected)
    {
        var configData = new Dictionary<string, string?>();
        if (useMockAuth.HasValue)
            configData["UseMockAuth"] = useMockAuth.Value.ToString();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configData).Build();

        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns(environmentName);

        var result = E2ETestAuthenticationMiddleware.ShouldBeRegistered(configuration, environment.Object);

        result.Should().Be(expected);
    }
```

- [ ] **Step 2: Run the full new test class to verify everything passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~E2ETestAuthenticationMiddlewareTests"`
Expected: 18 tests total (10 from before + 8 `ShouldBeRegistered_ReturnsExpected` rows), all PASS.

- [ ] **Step 3: Build the whole backend solution**

Run: `cd backend && dotnet build`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (or the baseline warning count already present on the branch — no NEW warnings introduced by this file).

- [ ] **Step 4: Run `dotnet format` and confirm no diffs**

Run: `cd backend && dotnet format --verify-no-changes`
Expected: exits 0 with no changes reported. If it reports formatting issues in the new file, run `dotnet format` (without `--verify-no-changes`) to auto-fix, review the diff is confined to `E2ETestAuthenticationMiddlewareTests.cs`, then re-run `--verify-no-changes` to confirm.

- [ ] **Step 5: Run the full backend test suite to confirm nothing else regressed**

Run: `cd backend && dotnet test`
Expected: all existing tests plus the 18 new tests pass; no pre-existing test starts failing.

- [ ] **Step 6: Confirm the coverage target was met (NFR-4)**

If the repo's coverage tooling is available locally (check `docs/architecture/testing-strategy.md` for the exact command used in CI), run it scoped to the changed file and confirm `E2ETestAuthenticationMiddleware.cs` line coverage is now >= 60%. If no local coverage command is readily available, this is also verified automatically by the coverage-gap routine's next scheduled run against CI — note in the PR description that local verification of the exact percentage was not performed, only that every branch enumerated in the spec's FR-1 through FR-8 has an asserting test.

- [ ] **Step 7: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Infrastructure/Authentication/E2ETestAuthenticationMiddlewareTests.cs
git commit -m "test(feat-4338): add ShouldBeRegistered guard matrix tests, complete coverage for E2ETestAuthenticationMiddleware"
```

---

## Self-Review

**Spec coverage:** FR-1 through FR-8 and NFR-1 through NFR-4 are each mapped to a task in the table above; no spec requirement is without a task.

**Placeholder scan:** No "TBD"/"TODO"/"add appropriate handling" placeholders — every step contains complete, compilable C# test code. Step 6 of the last task is the one place execution depends on discovering the repo's actual coverage-tool invocation (not specified in the spec or found during architecture review), so it is written as a best-effort check with an explicit fallback note rather than a fabricated command.

**Type consistency:** `CreateEnvironment`, `CreateMiddleware`, `CreateFailingCookieAuthService`, `BuildServices`, `CreateContext`, and `ReadResponseBodyAsync` are defined once in task `scaffold-file-and-environment-guard-tests` and used with identical signatures in every subsequent task. `IServicePrincipalTokenValidator.ValidateAsync(string) : Task<bool>` and `IE2ESessionService.CreateSyntheticUserClaims(string) : Claim[]` match the real interfaces read from `backend/src/Anela.Heblo.API/Infrastructure/Authentication/ServicePrincipalTokenValidator.cs` and `E2ESessionService.cs`.
