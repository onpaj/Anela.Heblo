# Specification: Unit Test Coverage for E2ETestAuthenticationMiddleware

## Summary
`E2ETestAuthenticationMiddleware` (`backend/src/Anela.Heblo.API/Infrastructure/Authentication/E2ETestAuthenticationMiddleware.cs`) currently has 6.7% line coverage against a 60% threshold. This work adds a focused unit test suite covering every branch of `InvokeAsync` and the `ShouldBeRegistered` static overload, without changing any production code or behavior.

## Background
This middleware is a security-sensitive gate: it decides whether a request may bypass real Entra ID authentication and receive a synthetic E2E test identity. It is guarded by an environment check (Staging/Development only) and, at registration time, by `ShouldBeRegistered`, which additionally requires `UseMockAuth` to be `false`. A regression in either guard could either silently disable E2E testing or — worse — leave a mock-auth bypass reachable outside Staging/Development. The class has no existing test coverage. This is a test-only coverage-gap remediation filed by the weekly coverage-gap routine (issue #4338); no functional change is requested or in scope.

## Functional Requirements

### FR-1: Environment guard coverage
When `IWebHostEnvironment` reports an environment other than `Staging` or `Development` (e.g. `Production`), `InvokeAsync` must call `_next(context)` exactly once and return immediately, without touching `context.User`, without calling `context.AuthenticateAsync`, and without resolving `IServicePrincipalTokenValidator` or `IE2ESessionService` from `RequestServices`.

**Acceptance criteria:**
- Test with `IWebHostEnvironment.EnvironmentName = "Production"` (and `IsDevelopment()` returning `false`, `IsEnvironment("Staging")` returning `false`): `next` delegate is invoked once; response status code is unchanged from its default; no E2E-related header/cookie processing occurs.
- Test confirms `Staging` and `Development` both pass this guard and proceed to the next check (i.e. the guard is inclusive of both, not just one).

### FR-2: Already-authenticated short-circuit
When `context.User.Identity.IsAuthenticated == true` (already authenticated by an earlier scheme, e.g. real Entra ID), `InvokeAsync` must call `_next(context)` exactly once and return, without invoking cookie auth (`context.AuthenticateAsync("E2ETestCookies")`) or token-header processing.

**Acceptance criteria:**
- Test sets an authenticated `ClaimsPrincipal` on `context.User` in a Staging/Development environment: `next` is called once; `context.User` reference is unchanged; no attempt is made to read `X-E2E-Test-Token`.
- A debug-level log call is acceptable to assert but not required (log content is not part of the observable contract under test; do not over-specify logger mock behavior beyond what's testable via the existing `ILogger` mocking convention).

### FR-3: E2E session cookie authentication path
When the user is not yet authenticated and cookie authentication against the `"E2ETestCookies"` scheme succeeds (`AuthenticateResult.Succeeded == true` with a non-null `Principal`), `InvokeAsync` must set `context.User` to that principal and call `_next(context)` once, without reaching the `X-E2E-Test-Token` header path.

**Acceptance criteria:**
- Test wires up a real `"E2ETestCookies"` cookie authentication scheme (via `services.AddAuthentication().AddCookie("E2ETestCookies", ...)` in a test `ServiceCollection`, matching the app's registration in `AuthenticationExtensions.cs`) and presents a request with a valid, previously-issued cookie so that `context.AuthenticateAsync("E2ETestCookies")` succeeds.
- After `InvokeAsync`, `context.User.Identity.IsAuthenticated` is `true` and identifies the expected synthetic user; `next` was called exactly once.
- Test verifies the `X-E2E-Test-Token` header path is never reached when the cookie path succeeds (e.g. by also including a bogus/absent header and confirming no 401/500 short-circuit and no dependency on the token validator).

### FR-4: Missing/empty token header passthrough
When cookie auth does not succeed and the `X-E2E-Test-Token` header is absent, or present but empty/whitespace, `InvokeAsync` must call `_next(context)` once and must not resolve `IServicePrincipalTokenValidator` from `RequestServices` (so a request-services container with no such registration must not throw).

**Acceptance criteria:**
- Test: no `X-E2E-Test-Token` header present → `next` called once, response untouched.
- Test: header present with empty string value → `next` called once, response untouched.

### FR-5: Token validation failure path
When a non-empty `X-E2E-Test-Token` header is present and `IServicePrincipalTokenValidator.ValidateAsync(token)` returns `false`, `InvokeAsync` must set `context.Response.StatusCode = 401`, write body `"Invalid E2E test token"`, and must NOT call `_next(context)`.

**Acceptance criteria:**
- Test with a mocked `IServicePrincipalTokenValidator` returning `false`: response status is `401`; response body equals `"Invalid E2E test token"`; `next` delegate is not invoked; `IE2ESessionService` is never resolved/called.

### FR-6: Token validation success path
When `ValidateAsync` returns `true`, `InvokeAsync` must resolve `IE2ESessionService`, call `CreateSyntheticUserClaims(environmentName)`, build a `ClaimsIdentity`/`ClaimsPrincipal` with authentication type `"E2ETest"`, set it as `context.User`, append an `E2E-Auth-Override=true` response cookie, and call `_next(context)` once.

**Acceptance criteria:**
- Test with a mocked validator returning `true` and a mocked `IE2ESessionService.CreateSyntheticUserClaims(...)` returning a known claim set: after `InvokeAsync`, `context.User.Identity.IsAuthenticated` is `true`, `context.User.Identity.AuthenticationType == "E2ETest"`, and the claims match what the mock returned.
- Response contains a `Set-Cookie` header for `E2E-Auth-Override` with value `true`.
- `next` is called exactly once, and is called (i.e. the success path does not short-circuit).

### FR-7: Exception path in token validation
When `IServicePrincipalTokenValidator.ValidateAsync` (or `IE2ESessionService.CreateSyntheticUserClaims`) throws, the `catch` block must set `context.Response.StatusCode = 500`, write body `"Error validating E2E test token"`, and must NOT call `_next(context)`.

**Acceptance criteria:**
- Test with a mocked validator that throws an `Exception` from `ValidateAsync`: response status is `500`; response body equals `"Error validating E2E test token"`; `next` is not invoked.
- (Optional, only if trivially achievable without over-mocking) a variant where the validator succeeds but `IE2ESessionService.CreateSyntheticUserClaims` throws, confirming the same catch block also guards the session-service call. Include only if it does not require disproportionate setup versus its coverage value.

### FR-8: `ShouldBeRegistered` guard matrix
The static `ShouldBeRegistered(IConfiguration, IHostEnvironment)` overload (and by extension its two convenience overloads) must return `true` only when the environment is Staging or Development AND `UseMockAuth` is not `true`.

**Acceptance criteria:**
- `Development` environment, no `UseMockAuth` config (defaults to `false`) → `true`.
- `Staging` environment, no `UseMockAuth` config → `true`.
- `Production` environment, no `UseMockAuth` config → `false`.
- `Staging` environment, `UseMockAuth = true` → `false`.
- `Development` environment, `UseMockAuth = true` → `false`.
- Some other named environment (e.g. `"IntegrationTest"`), any `UseMockAuth` value → `false`.
- Use `Theory`/`InlineData` (or `MemberData`) to express this matrix concisely, mirroring the `[Theory]` convention already used in `McpBadRequestMiddlewareTests.HasValidMcpAcceptHeader_ReturnsExpected`.

## Non-Functional Requirements

### NFR-1: Test isolation and determinism
Tests must not depend on real network calls, real Azure AD tokens, wall-clock timing, or shared mutable state across tests (the `IMemoryCache`-backed caching inside `ServicePrincipalTokenValidator` is irrelevant here since that class is mocked at the interface level, not exercised directly).

### NFR-2: No production code changes
This is a test-only change. The middleware's behavior, signatures, and logging must remain byte-for-byte unchanged. If a test reveals what looks like a genuine bug (e.g. an edge case the code handles surprisingly), do not fix it — note it in Open Questions / as a follow-up rather than silently altering behavior while "just adding tests."

### NFR-3: Convention consistency
New tests must follow the existing xUnit + Moq + FluentAssertions conventions already used in `backend/test/Anela.Heblo.Tests/` (see `McpBadRequestMiddlewareTests.cs`, `Infrastructure/Authentication/ServicePrincipalTokenValidatorTests.cs`, `Infrastructure/Authentication/E2ESessionServiceTests.cs`), including `DefaultHttpContext`-based fixtures rather than a full `TestServer`/`WebApplicationFactory`.

### NFR-4: Coverage target
Line coverage for `E2ETestAuthenticationMiddleware.cs` must rise from 6.7% to at least the 60% filter threshold used by the coverage-gap routine (ideally materially higher, since all branches listed in FR-1 through FR-8 are realistically coverable with plain unit tests).

## Data Model
Not applicable — this is a test-only change with no data model impact. Relevant existing types (for test setup reference only, not to be modified):
- `IWebHostEnvironment` (mocked via `Mock<IWebHostEnvironment>`, or a lightweight fake, to control `IsDevelopment()` / `IsEnvironment("Staging")` / `EnvironmentName`).
- `IServicePrincipalTokenValidator.ValidateAsync(string token) : Task<bool>` (mocked).
- `IE2ESessionService.CreateSyntheticUserClaims(string environmentName) : Claim[]` (mocked).
- `"E2ETestCookies"` cookie authentication scheme, registered in production via `AuthenticationExtensions.cs` (`services.AddAuthentication().AddCookie("E2ETestCookies", ...)`); tests needing a real `AuthenticateAsync("E2ETestCookies")` success must register an equivalent minimal scheme in a local `ServiceCollection` and build a real `HttpContext.RequestServices` from it (see Architecture Amendments expected from the architect on the exact mechanics, since `DefaultHttpContext` alone won't resolve authentication handlers without `RequestServices` wired to a built `IServiceProvider` that includes `IAuthenticationSchemeProvider` etc.).

## API / Interface Design
Not applicable — no new or changed public interfaces. The test suite is additive: a new test class `E2ETestAuthenticationMiddlewareTests` under `backend/test/Anela.Heblo.Tests/Infrastructure/Authentication/`.

## Dependencies
- xUnit, Moq, FluentAssertions (already referenced by `Anela.Heblo.Tests.csproj`).
- `Microsoft.AspNetCore.Authentication.Cookies` (already a transitive dependency via the API project) for the FR-3 cookie-scheme test, or an equivalent lightweight `IAuthenticationService`/`IAuthenticationHandlerProvider` mock if the architect determines standing up a real cookie scheme is disproportionate — see Open Questions.

## Out of Scope
- Any change to `E2ETestAuthenticationMiddleware.cs` production logic, logging, or the `AuthenticationExtensions.cs` registration code.
- Integration/E2E-level tests exercising the middleware through the full ASP.NET Core pipeline (`WebApplicationFactory`) — this brief specifically calls for unit tests against `InvokeAsync` and `ShouldBeRegistered`.
- Testing `ServicePrincipalTokenValidator` or `E2ESessionService` implementation internals — those already have their own dedicated test files and are mocked here at the interface boundary.
- Raising coverage on any other file.

## Open Questions
None. Where the brief left mechanics unspecified (e.g. how to make `context.AuthenticateAsync("E2ETestCookies")` succeed in a unit test without a full `WebApplicationFactory`), the assumption is: register a minimal real cookie-authentication scheme in a `ServiceCollection`, build an `IServiceProvider`, and assign it to `DefaultHttpContext.RequestServices` — this is a standard, well-established pattern for unit-testing ASP.NET Core authentication middleware and keeps the tests as true unit tests (no `TestServer`, no real HTTP listener). The architecture review should confirm or refine this mechanic before implementation.

## Status: COMPLETE
