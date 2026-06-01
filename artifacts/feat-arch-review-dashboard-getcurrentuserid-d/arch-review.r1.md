# Architecture Review: Consolidate GetCurrentUserId() into BaseApiController

## Skip Design: true

This is a backend-only code-organization refactor. No UI components, screens, or visual decisions are touched.

## Architectural Fit Assessment

The proposal aligns cleanly with existing conventions. `BaseApiController` already serves as the established home for cross-controller infrastructure (`HandleResponse<T>`, `Logger`), and all three affected controllers (`DashboardController`, `CarrierCoolingController`, `GiftSettingsController`) already inherit from it. ~30 controllers in `backend/src/Anela.Heblo.API/Controllers/` use `BaseApiController` today, so adding a `protected` helper there is the established extension point — no new abstraction is being introduced.

Two integration points need explicit attention:

1. **Exception-mapping middleware does not currently exist.** A grep across `Anela.Heblo.API` finds no `UseExceptionHandler`, no `IExceptionHandler` registration, and no `ExceptionMiddleware`. `ConfigureApplicationPipeline` in `backend/src/Anela.Heblo.API/Extensions/ApplicationBuilderExtensions.cs` jumps from CORS → routing → auth → endpoints with no global exception stage. FR-3 therefore requires *adding* (not extending) a global exception handler. This is a small but real cross-cutting addition the spec calls out but does not fully scope.

2. **The brief misstates the current exception type in two of three controllers.** Actual code:
   - `DashboardController.cs:103` — `throw new Exception("User not found")` *(matches brief)*
   - `CarrierCoolingController.cs:45` — `throw new InvalidOperationException("Authenticated user has no identity claim.")`
   - `GiftSettingsController.cs:45` — `throw new InvalidOperationException("Authenticated user has no identity claim.")`

   Result is unchanged (consolidate to `UnauthorizedAccessException` on the base class), but the spec's NFR-3 catch-block sweep must also look for `catch (InvalidOperationException)`, not just `catch (Exception)`.

## Proposed Architecture

### Component Overview

```
                ┌────────────────────────────────────────┐
                │ HTTP Request (authenticated, [Authorize]) │
                └───────────────────────────────────────┬┘
                                                        │
                ┌───────────────────────────────────────▼─┐
                │ AuthN/AuthZ middleware (existing)        │
                │ — populates ControllerBase.User          │
                └───────────────────────────────────────┬─┘
                                                        │
                ┌───────────────────────────────────────▼─┐
                │ Derived controller action               │
                │   var userId = GetCurrentUserId();      │
                └────────┬────────────────────────────────┘
                         │ (calls inherited protected method)
                ┌────────▼────────────────────────────────┐
                │ BaseApiController.GetCurrentUserId()    │  ◄── single source of truth
                │ NameIdentifier → sub → oid → throw      │
                └────────┬────────────────────────────────┘
                         │ (on missing claim)
                ┌────────▼────────────────────────────────┐
                │ UnauthorizedAccessException             │
                └────────┬────────────────────────────────┘
                         │ (bubbles up)
                ┌────────▼────────────────────────────────┐
                │ IExceptionHandler (NEW)                 │
                │ UnauthorizedAccessException → 401       │
                │ + ProblemDetails body                   │
                └─────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Method visibility — `protected` (not `protected internal` or `public`)
**Options considered:** `private` (defeats reuse), `protected`, `protected internal`, `public`.
**Chosen approach:** `protected`.
**Rationale:** Matches the visibility of the existing `HandleResponse<T>` and `Logger` members on `BaseApiController`. `internal` exposure is unnecessary — only derived controllers need it, and the ASP.NET model-binding pipeline never invokes helper methods directly. The method is non-virtual; no existing override pattern exists on `BaseApiController` and overriding identity resolution per-controller would be a misuse.

#### Decision 2: Exception type — `UnauthorizedAccessException`
**Options considered:** `Exception` (status quo, surfaces as 500), `InvalidOperationException` (current Carrier/Gift behavior, also 500), `UnauthorizedAccessException` (proposed), a custom `MissingClaimException`.
**Chosen approach:** `UnauthorizedAccessException` (BCL type).
**Rationale:** Semantically accurate — the principal is authenticated but lacks the claim required to identify them, which is an authorization-context failure. BCL type avoids adding a new exception class for a single call site. Aligns with the project's preference for idiomatic .NET over bespoke abstractions (CLAUDE.md §"Surgical changes"). A custom exception offers no benefit and adds another type developers must learn.

#### Decision 3: 401 mapping — implement `IExceptionHandler`, not `UseExceptionHandler` with lambda
**Options considered:**
- Per-controller `try/catch` (rejected — defeats consolidation).
- `app.UseExceptionHandler(...)` with inline lambda.
- `IExceptionHandler` (ASP.NET Core 8+ DI-friendly handler chain).

**Chosen approach:** Register an `IExceptionHandler` (e.g. `UnauthorizedAccessExceptionHandler`) via `services.AddExceptionHandler<T>()` and `app.UseExceptionHandler()` in the pipeline.
**Rationale:** Project targets .NET 8 (per CLAUDE.md). `IExceptionHandler` is the .NET 8 idiom — strongly typed, testable, composable, and lets future handlers (e.g. for `ValidationException`) be added without touching pipeline code. It coexists with the existing `BaseResponse`/`ErrorCodes` pattern (which handles *expected* business failures via `HandleResponse<T>`) by handling only *unexpected* exceptions — the two patterns do not overlap.

#### Decision 4: Response body — `ProblemDetails` with no message leakage
**Options considered:** Empty 401 body, custom JSON envelope, `ProblemDetails`.
**Chosen approach:** RFC 7807 `ProblemDetails` with `status: 401`, `title: "Unauthorized"`, no `detail` (i.e. exception message NOT returned to the client).
**Rationale:** `ProblemDetails` is already used elsewhere in the codebase (e.g. `LeafletController.cs:35-61`). The exception message `"Authenticated user has no identifiable claim."` is safe for logs but should not be echoed to clients — keeping `detail` empty avoids any policy ambiguity (NFR-2). The exception itself MUST be logged with full context server-side.

#### Decision 5: Test placement — single test class on the base controller
**Options considered:** Keep the four `GetCurrentUserId_*` tests on `DashboardControllerTests`, duplicate per controller, or migrate to a dedicated `BaseApiControllerTests` fixture.
**Chosen approach:** Create `backend/test/Anela.Heblo.Tests/Controllers/BaseApiControllerTests.cs` with a minimal `TestBaseApiController : BaseApiController` harness exposing `GetCurrentUserId` for assertion. Delete the four tests currently at `DashboardControllerTests.cs:230, 250, 278, 306` since the behavior they exercise has moved to the base class.
**Rationale:** Tests should sit on the type that owns the behavior. Per-controller duplication contradicts the whole point of consolidation. A small test-only derived class avoids exposing the method publicly on production code.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.API/
├── Controllers/
│   └── BaseApiController.cs                          (MODIFY — add GetCurrentUserId)
│   └── DashboardController.cs                        (MODIFY — remove private method; remove unused `using System.Security.Claims;`)
│   └── CarrierCoolingController.cs                   (MODIFY — same)
│   └── GiftSettingsController.cs                     (MODIFY — same)
├── Infrastructure/
│   └── ExceptionHandling/                            (NEW directory)
│       └── UnauthorizedAccessExceptionHandler.cs     (NEW)
└── Extensions/
    ├── ApplicationBuilderExtensions.cs               (MODIFY — UseExceptionHandler before UseRouting)
    └── ServiceCollectionExtensions.cs                (MODIFY — AddExceptionHandler<...>(); AddProblemDetails();)

backend/test/Anela.Heblo.Tests/
├── Controllers/
│   ├── BaseApiControllerTests.cs                     (NEW — claim chain + UnauthorizedAccessException tests)
│   └── DashboardControllerTests.cs                   (MODIFY — delete the four GetCurrentUserId_* tests at lines 230, 250, 278, 306)
└── Infrastructure/
    └── ExceptionHandling/
        └── UnauthorizedAccessExceptionHandlerTests.cs (NEW — verifies 401 + ProblemDetails + no message leak)
```

### Interfaces and Contracts

**`BaseApiController.cs` — addition:**

```csharp
using System.Security.Claims;

// existing class body…

protected string GetCurrentUserId()
    => User.FindFirst(ClaimTypes.NameIdentifier)?.Value
       ?? User.FindFirst("sub")?.Value
       ?? User.FindFirst("oid")?.Value
       ?? throw new UnauthorizedAccessException("Authenticated user has no identifiable claim.");
```

**`UnauthorizedAccessExceptionHandler.cs` — contract:**

```csharp
public sealed class UnauthorizedAccessExceptionHandler : IExceptionHandler
{
    private readonly ILogger<UnauthorizedAccessExceptionHandler> _logger;
    public UnauthorizedAccessExceptionHandler(ILogger<UnauthorizedAccessExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not UnauthorizedAccessException uax) return false;

        _logger.LogWarning(uax, "Unauthorized access: {Message}", uax.Message);

        httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Unauthorized",
            Type = "https://tools.ietf.org/html/rfc7235#section-3.1"
        }, cancellationToken);
        return true;
    }
}
```

**Pipeline wiring (`ApplicationBuilderExtensions.cs`):**

`app.UseExceptionHandler()` must be registered **before** `app.UseRouting()` (line 89) so it can catch exceptions from any later middleware including the endpoint executor. The `ConfigureApplicationPipeline` ordering becomes:

```
ForwardedHeaders → CORS → HttpsRedirection → HttpLogging → RequestLogging
→ UseExceptionHandler()         ◄── INSERT HERE
→ E2ETestAuth (conditional)
→ UseRouting → UseAuthentication → UseAuthorization → …
```

**Service registration (`ServiceCollectionExtensions.cs`):**

```csharp
services.AddExceptionHandler<UnauthorizedAccessExceptionHandler>();
services.AddProblemDetails();
```

### Data Flow

**Happy path (claim present):**
1. `[Authorize]` middleware admits the request; `ControllerBase.User` is populated.
2. Controller action calls inherited `GetCurrentUserId()`.
3. First non-null of `NameIdentifier` / `sub` / `oid` is returned.
4. Action proceeds; behavior is byte-identical to the previous per-controller method.

**Missing-claim path:**
1. `[Authorize]` admits the request (token was valid) but the token has none of the three claims.
2. `GetCurrentUserId()` throws `UnauthorizedAccessException`.
3. Exception bubbles past the controller and through the endpoint middleware.
4. `UseExceptionHandler` invokes `UnauthorizedAccessExceptionHandler.TryHandleAsync`.
5. Handler logs the exception (full context, server-side) and writes a `401 ProblemDetails` response with **no message in the body**.

**Practical note on FR-3:** Because all three controllers carry `[Authorize]` and the token validation pipeline already requires a valid principal, the missing-claim path is a true edge case (would require a token issued to a principal with none of the three claim types). The handler is therefore primarily a defensive contract guarantee, not a hot path.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Other controllers also have private `GetCurrentUserId()` — codebase-wide sweep missed something. | Low | Run `grep -rn "GetCurrentUserId" backend/src --include="*.cs"` as a hard gate before declaring complete. Spec already requires this. Confirmed by grep on the worktree: only the three controllers from the brief contain a definition. |
| Adding `UseExceptionHandler` globally inadvertently swallows or reshapes errors from other code paths (e.g. background jobs, MCP). | Medium | `IExceptionHandler.TryHandleAsync` returning `false` for non-matching types means the default ASP.NET error pipeline still runs for everything else. Restrict the new handler to `UnauthorizedAccessException` only. Verify no other code path in the codebase throws `UnauthorizedAccessException` expecting a 500 (grep result: only `GraphService.cs:183` catches it — does not throw). |
| Existing tests at `DashboardControllerTests.cs:230` use `Assert.ThrowsAsync<Exception>` and check `Message == "User not found"`. Removing the per-controller method breaks them. | High | Tests must be deleted as part of this change (they target moved behavior). Replacement tests live in `BaseApiControllerTests`. Listed explicitly under "Specification Amendments" below. |
| `CarrierCoolingController` and `GiftSettingsController` `using System.Security.Claims;` becomes unused after refactor — `dotnet format` will flag. | Low | Remove the `using` directive in the same edit. Same for `DashboardController.cs:1`. |
| `UnauthorizedAccessException` from controller-construction or routing throws (extremely unlikely) might also get mapped to 401 unexpectedly. | Low | Handler scope is correct as-is; if needed in the future, scope by inspecting `httpContext.GetEndpoint()` or path prefix `/api/`. Not required for the initial implementation. |
| The handler's `ProblemDetails` body conflicts with what the project's existing 401 responses (from `HandleResponse<T>`) return — clients may see two different 401 shapes. | Medium | Acceptable: existing `HandleResponse<T>` 401s return a `BaseResponse` (business error), while the new handler returns `ProblemDetails` (infrastructure error). These are distinct semantic categories. Document the difference in `UnauthorizedAccessExceptionHandler.cs` with a one-line comment. |

## Specification Amendments

1. **NFR-3 sweep must include `InvalidOperationException`.** The spec says to search for `catch (Exception)`. Two of the three controllers actually throw `InvalidOperationException`, so the sweep must also include `catch (InvalidOperationException)` to satisfy NFR-3.

2. **FR-2 should explicitly list `using System.Security.Claims;` removal.** Once the private method is removed, none of the three controllers reference `ClaimTypes` directly. The unused `using` must be removed in the same edit to avoid an analyzer warning under `dotnet format`.

3. **FR-4 acceptance criteria should specify test relocation.** Add to FR-4: "The four existing `GetCurrentUserId_*` tests on `DashboardControllerTests` (lines 230, 250, 278, 306) MUST be deleted and replaced by equivalent tests on a new `BaseApiControllerTests` fixture using a minimal test-derived controller. The Dashboard tests are not migrated — they are deleted and the equivalents live on the base-class test class."

4. **FR-3 should name the implementation mechanism.** Replace "add one (in the existing global exception handler / middleware, ...)" with: "register an `IExceptionHandler` implementation (`UnauthorizedAccessExceptionHandler`) via `services.AddExceptionHandler<T>()`, `services.AddProblemDetails()`, and call `app.UseExceptionHandler()` before `app.UseRouting()` in `ConfigureApplicationPipeline`. Response body MUST be `ProblemDetails` with no `detail` (exception message logged server-side only, never returned to the client)."

5. **NFR-2 should add a "no other thrower" check.** Add: "Confirm via grep that no other code path throws `UnauthorizedAccessException` expecting a 500 response. As of the current codebase, only `GraphService.cs:183` references the type (in a `catch`), so this is safe."

6. **Add a new acceptance criterion under "API / Interface Design":** "Response body shape for the missing-claim 401 is `ProblemDetails` (`{status, title, type}`), distinct from the `BaseResponse`-shaped 401s returned by `HandleResponse<T>`. This shape divergence is intentional — infrastructure exceptions and business errors are separate categories."

## Prerequisites

None. All required types (`UnauthorizedAccessException`, `IExceptionHandler`, `ProblemDetails`, `ClaimsPrincipal`) are in the BCL or ASP.NET Core 8 framework already referenced by `Anela.Heblo.API.csproj`. No new NuGet packages, configuration entries, Key Vault secrets, database migrations, or infrastructure changes are required.