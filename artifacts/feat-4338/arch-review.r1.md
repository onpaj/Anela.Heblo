# Architecture Review: Unit Test Coverage for E2ETestAuthenticationMiddleware

## Skip Design: true
{Test-only coverage remediation for a backend middleware class. No UI, no new component, no visual design decisions. Design phase should be a no-op pass-through.}

## Architectural Fit Assessment
This is a pure test-authoring task against a single, already-stable middleware class (`E2ETestAuthenticationMiddleware`). It requires no changes to production code, module boundaries, or DI registration. The existing test project (`Anela.Heblo.Tests`) already contains sibling middleware tests (`McpBadRequestMiddlewareTests`, `McpDiagnosticsMiddlewareTests`, `PostAnswerEnrichmentMiddlewareTests`) and sibling authentication-service tests (`ServicePrincipalTokenValidatorTests`, `E2ESessionServiceTests`) in the exact directories the new test class belongs next to. The integration point is trivial: one new `.cs` file added to an existing test project, no `.csproj` changes needed since `FluentAssertions`, `Moq`, and `Microsoft.AspNetCore.*` test helpers are already referenced (confirmed by reading `McpBadRequestMiddlewareTests.cs`, which already builds `DefaultHttpContext` and mocks `ILogger<T>` the same way this middleware's tests will need to).

## Proposed Architecture

### Component Overview
```
Anela.Heblo.Tests (existing xUnit project)
└── Infrastructure/
    └── Authentication/
        ├── ServicePrincipalTokenValidatorTests.cs   (existing)
        ├── E2ESessionServiceTests.cs                 (existing)
        └── E2ETestAuthenticationMiddlewareTests.cs   (NEW)
                │
                ├── constructs E2ETestAuthenticationMiddleware directly (new (next, logger, environment))
                ├── mocks IWebHostEnvironment
                ├── mocks IServicePrincipalTokenValidator, IE2ESessionService via a built ServiceProvider
                └── for the cookie-auth-success case only: registers a REAL
                    "E2ETestCookies" cookie scheme in a local ServiceCollection
                    so context.AuthenticateAsync("E2ETestCookies") genuinely succeeds
```
No production component is touched. The "architecture" here is entirely about the test fixture's shape.

### Key Design Decisions

#### Decision 1: How to satisfy `context.AuthenticateAsync("E2ETestCookies")` in a unit test
**Options considered:**
1. Mock `IAuthenticationService` / `IAuthenticationHandlerProvider` directly and stub `AuthenticateAsync` to return a canned `AuthenticateResult`.
2. Stand up a real, minimal `"E2ETestCookies"` cookie authentication scheme via `ServiceCollection.AddAuthentication().AddCookie("E2ETestCookies", ...)`, build an `IServiceProvider`, assign it to `DefaultHttpContext.RequestServices`, and drive a genuine sign-in/round-trip (or directly inject a valid auth cookie into the request) so `AuthenticateAsync` succeeds for real.
3. Use `WebApplicationFactory<Program>` / `TestServer` to run the full pipeline.

**Chosen approach:** Option 1 (mock `IAuthenticationService`) for the success/failure branching in `InvokeAsync`, NOT option 2 or 3.

**Rationale:** `context.AuthenticateAsync(scheme)` is an extension method that resolves `IAuthenticationService` from `context.RequestServices` and calls `IAuthenticationService.AuthenticateAsync(context, scheme)`. Mocking `IAuthenticationService` directly (via `Mock<IAuthenticationService>`, registered as a singleton in a small `ServiceCollection` built into `context.RequestServices`) is far simpler than wiring a real cookie handler, avoids any dependency on `Microsoft.AspNetCore.Authentication.Cookies` internals (cookie encryption/decryption, clock skew, `DataProtection` key rings), and still exercises exactly the code path under test: the middleware's own branching on `AuthenticateResult.Succeeded` / `.Principal`. Standing up a *real* cookie scheme (option 2, the analyst's initial assumption) adds significant fixture complexity (data protection providers, a genuine sign-in round trip to produce a valid cookie header) for zero additional coverage of the middleware's own logic — the middleware does not care *how* `AuthenticateAsync` succeeded, only that it did. `TestServer` (option 3) is explicitly out of scope per the brief ("unit tests against `InvokeAsync`"), is far slower, and is unnecessary here. **Amendment to spec FR-3 / Data Model**: replace "register a real cookie scheme" with "mock `IAuthenticationService.AuthenticateAsync` to return `AuthenticateResult.Success(new AuthenticationTicket(principal, \"E2ETestCookies\"))`, registered into `context.RequestServices` via a minimal `ServiceCollection`."

#### Decision 2: How to build `context.RequestServices` for the token-validator/session-service paths
**Options considered:**
1. `Mock<IServiceProvider>` with `GetService`/`GetRequiredService` stubbed via `It.IsAny<Type>()` switch logic.
2. A real `ServiceCollection` with the mocked interfaces' `.Object` registered as singletons, then `.BuildServiceProvider()`.

**Chosen approach:** Option 2 — a real, minimal `ServiceCollection`/`BuildServiceProvider()`.

**Rationale:** `GetRequiredService<T>()` is an extension method with specific behavior (throws `InvalidOperationException` with a descriptive message when unregistered) that is awkward and brittle to hand-roll against a raw `Mock<IServiceProvider>`. Building a real minimal container is the pattern already implicitly needed for Decision 1's `IAuthenticationService` registration, so one `ServiceCollection` per test can host both: `IAuthenticationService` (mocked), `IServicePrincipalTokenValidator` (mocked), `IE2ESessionService` (mocked). This is also idiomatic and low-risk — it exercises real `Microsoft.Extensions.DependencyInjection` resolution semantics rather than reimplementing them.

#### Decision 3: Test class location and structure
**Options considered:** single flat test class vs. splitting by concern (environment guard, cookie path, token path, `ShouldBeRegistered`).
**Chosen approach:** Single test class `E2ETestAuthenticationMiddlewareTests.cs`, with `// ──` region-comment banners grouping tests by concern (mirroring `McpBadRequestMiddlewareTests.cs`'s existing banner style: `// ── non-MCP paths ──`, `// ── probe blocking ──`, etc.), plus a private `CreateMiddleware(...)` / `CreateContext(...)` helper pair mirroring that same file's `CreateMiddleware`/`CreateContext` helpers.
**Rationale:** Directly matches the one clear sibling precedent in this codebase (`McpBadRequestMiddlewareTests`), minimizing review friction for a solo-developer + AI-review workflow, and keeps `ShouldBeRegistered`'s `[Theory]` matrix visually separate from the `InvokeAsync` `[Fact]`s.

## Implementation Guidance

### Directory / Module Structure
- New file: `backend/test/Anela.Heblo.Tests/Infrastructure/Authentication/E2ETestAuthenticationMiddlewareTests.cs`
- No other files change. No `.csproj` edits needed (verify `Microsoft.AspNetCore.Http` and `Microsoft.Extensions.DependencyInjection` are already available transitively via the existing `Anela.Heblo.Tests.csproj` references used by `McpBadRequestMiddlewareTests.cs`, which already uses `DefaultHttpContext`).

### Interfaces and Contracts
No new interfaces. Tests consume the existing public surface:
- `E2ETestAuthenticationMiddleware(RequestDelegate next, ILogger<E2ETestAuthenticationMiddleware> logger, IWebHostEnvironment environment)`
- `Task InvokeAsync(HttpContext context)`
- `static bool ShouldBeRegistered(IConfiguration configuration, IHostEnvironment environment)` (test this overload directly — it is the one with the actual logic; the two convenience overloads (`WebApplication`, `WebApplicationBuilder`) simply forward to it and do not need separate coverage since they contain no branching of their own).

### Data Flow (test fixture wiring)
For each `InvokeAsync` test:
1. Build a `Mock<IWebHostEnvironment>` and stub `EnvironmentName`, `IsDevelopment()` is a real extension method over `EnvironmentName == Environments.Development`, so set `EnvironmentName` directly (e.g. `"Development"`, `"Staging"`, `"Production"`) rather than trying to mock the extension method itself — same for `IsEnvironment("Staging")`, which also compares `EnvironmentName` case-insensitively. **Note for developer**: `IsDevelopment()`/`IsEnvironment()` are `Microsoft.Extensions.Hosting.HostEnvironmentEnvExtensions` extension methods operating on `IHostEnvironment.EnvironmentName` — do not attempt `mock.Setup(x => x.IsDevelopment())`, it will not intercept an extension method call; set `EnvironmentName` on the mock instead.
2. Build a `ServiceCollection`, register mocked `IAuthenticationService`, `IServicePrincipalTokenValidator`, `IE2ESessionService` as needed per test (only register what that test needs — the "missing header" tests in FR-4 should register NEITHER `IServicePrincipalTokenValidator` NOR `IE2ESessionService`, so that a `GetRequiredService<IServicePrincipalTokenValidator>()` call would throw if the code path wrongly reached it — this turns "must not be resolved" from an unverifiable assumption into an enforced failure).
3. `BuildServiceProvider()` → assign to `DefaultHttpContext.RequestServices`.
4. Construct `E2ETestAuthenticationMiddleware` with a `RequestDelegate` capturing `nextCalled = true` (same pattern as `McpBadRequestMiddlewareTests.CreateMiddleware`).
5. Call `InvokeAsync(context)`, then assert on `nextCalled`, `context.User`, `context.Response.StatusCode`, and (for the 401/500 paths) the response body — read via `context.Response.Body` after rewinding a `MemoryStream` set as the response body stream (`context.Response.Body = new MemoryStream()`, then seek to 0 and read as UTF-8 text before asserting).

For `ShouldBeRegistered` tests: build a `ConfigurationBuilder().AddInMemoryCollection(...)` with `"UseMockAuth"` set/unset, and a `Mock<IHostEnvironment>` with `EnvironmentName` set per row — call the `(IConfiguration, IHostEnvironment)` overload directly.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Extension-method mocking mistake (`IsDevelopment()`/`IsEnvironment()`) silently no-ops, making the environment-guard test pass for the wrong reason (Moq loose mode returns `false` for un-stubbed bool-returning members, which happens to match some cases coincidentally) | Medium | Explicitly set `EnvironmentName` on the mock as documented in Data Flow step 1; assert both a `Production`-EnvironmentName case (`false`/skip) and a `Development` case (proceeds) to prove the guard branches both ways, not just one. |
| `context.Response.Body` defaults to a non-seekable stream in `DefaultHttpContext`, causing body-assertion tests (FR-5, FR-7) to fail or throw on read-back | Low | Explicitly set `context.Response.Body = new MemoryStream()` before invoking, matching common ASP.NET Core middleware test patterns; this file has no existing precedent to copy for body assertions (the MCP middleware tests never write a body), so call this out explicitly for the developer. |
| Registering `IAuthenticationService` as a full mock diverges from "real" production wiring, so a future change to how `AuthenticateAsync` is called (e.g. new overload) could go unnoticed by this test suite | Low | Acceptable per NFR-1 (unit-test isolation is the explicit goal); flagged here for visibility, not a blocker. |
| Coverage tool may still show gaps in unreachable defensive code (none identified) | Low | None needed — no such code observed in this file. |

## Specification Amendments
- **FR-3 / Data Model**: Replace the "register a real `E2ETestCookies` cookie scheme" mechanic with "mock `IAuthenticationService.AuthenticateAsync(HttpContext, string)` to return a successful `AuthenticateResult` via `AuthenticateResult.Success(new AuthenticationTicket(principal, \"E2ETestCookies\"))`, registered as a singleton in a `ServiceCollection` assigned to `context.RequestServices`." (See Decision 1.) This resolves the analyst's Open Question / stated assumption with a lighter-weight, equally valid mechanic.
- **FR-4**: Strengthen acceptance criteria to explicitly *not register* `IServicePrincipalTokenValidator`/`IE2ESessionService` in the test's `ServiceCollection` for the "missing/empty token" cases, so the assertion "these services are never resolved" is enforced by the DI container throwing rather than merely inferred from mock `Verify(..., Times.Never)`, which is also acceptable but weaker on its own — prefer non-registration where practical, combine with `Verify` where mocks are registered for other reasons in the same test.
- No other spec changes required; FR-1, FR-2, FR-5, FR-6, FR-7, FR-8 are architecturally sound as written.

## Prerequisites
None — no infrastructure, migration, or config changes are needed before implementation can start. The single new test file can be written and run immediately against the current `main`/feature branch state.
