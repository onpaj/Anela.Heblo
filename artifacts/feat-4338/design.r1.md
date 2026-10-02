# Design: Unit Test Coverage for E2ETestAuthenticationMiddleware

## Component Design

### `E2ETestAuthenticationMiddlewareTests` (new)
Location: `backend/test/Anela.Heblo.Tests/Infrastructure/Authentication/E2ETestAuthenticationMiddlewareTests.cs`

Responsibility: exercise every branch of `E2ETestAuthenticationMiddleware.InvokeAsync` and every row of the `ShouldBeRegistered(IConfiguration, IHostEnvironment)` guard matrix, as a pure unit test with no production code changes, following the architecture review's Decision 1–3.

Structure (mirrors `McpBadRequestMiddlewareTests.cs`):

```
public class E2ETestAuthenticationMiddlewareTests
{
    // shared helpers
    private static Mock<IWebHostEnvironment> CreateEnvironment(string environmentName)
    private static E2ETestAuthenticationMiddleware CreateMiddleware(
        RequestDelegate next, IWebHostEnvironment environment, ILogger<...>? logger = null)
    private static HttpContext CreateContext(
        IServiceProvider services, ClaimsPrincipal? user = null, string? e2eTestToken = null)
        // sets context.Response.Body = new MemoryStream() so 401/500 bodies are readable
    private static IServiceProvider BuildServices(
        Mock<IAuthenticationService>? authService = null,
        Mock<IServicePrincipalTokenValidator>? tokenValidator = null,
        Mock<IE2ESessionService>? sessionService = null)
        // registers only what's passed in — omission is intentional per FR-4's
        // "must not be resolved" requirement (unregistered service throws if reached)

    // ── environment guard (FR-1) ────────────────────────────
    [Theory] InvokeAsync_NonStagingNonDevelopment_PassesThroughWithoutAuthLogic
    [Theory] InvokeAsync_StagingOrDevelopment_ProceedsPastGuard   // Staging, Development

    // ── already-authenticated short-circuit (FR-2) ─────────
    [Fact] InvokeAsync_AlreadyAuthenticated_PassesThroughWithoutCookieOrTokenLogic

    // ── cookie auth path (FR-3) ─────────────────────────────
    [Fact] InvokeAsync_CookieAuthSucceeds_SetsUserAndCallsNext

    // ── missing/empty token header (FR-4) ───────────────────
    [Fact] InvokeAsync_NoTokenHeader_PassesThroughWithoutResolvingValidator
    [Fact] InvokeAsync_EmptyTokenHeader_PassesThroughWithoutResolvingValidator

    // ── token validation failure (FR-5) ─────────────────────
    [Fact] InvokeAsync_InvalidToken_Returns401AndDoesNotCallNext

    // ── token validation success (FR-6) ─────────────────────
    [Fact] InvokeAsync_ValidToken_SetsSyntheticUserAndOverrideCookieAndCallsNext

    // ── exception path (FR-7) ───────────────────────────────
    [Fact] InvokeAsync_TokenValidatorThrows_Returns500AndDoesNotCallNext

    // ── ShouldBeRegistered matrix (FR-8) ────────────────────
    [Theory] ShouldBeRegistered_ReturnsExpected(string env, bool? useMockAuth, bool expected)
}
```

No other component changes. The middleware itself, `IServicePrincipalTokenValidator`, `IE2ESessionService`, and `AuthenticationExtensions` are consumed as-is and are not modified.

## Data Schemas
Not applicable — no data schema, API contract, or event payload is introduced or changed by this test-only work. The only "shape" involved is the test fixture's DI container (see Component Design's `BuildServices` helper), which is internal to the test file and not a public contract.
