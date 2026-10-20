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

