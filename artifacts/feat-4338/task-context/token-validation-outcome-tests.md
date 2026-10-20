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

