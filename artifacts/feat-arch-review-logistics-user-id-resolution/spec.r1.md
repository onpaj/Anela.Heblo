# Specification: Move User ID Resolution Out of LogisticsController

## Summary
Refactor `LogisticsController` to eliminate duplicated user-ID resolution logic (GUID parsing + system-user fallback) that currently lives in two action methods. The resolution moves into the corresponding MediatR handlers, the controller actions become one-line `_mediator.Send` calls, and `ICurrentUserService` is no longer injected into the controller.

## Background
The architecture guideline `docs/architecture/development_guidelines.md` forbids business logic in controllers. `LogisticsController` violates this rule in two action methods (`CreateGiftPackageManufacture` and `DisassembleGiftPackage`), both of which:

1. Read the current user via `ICurrentUserService.GetCurrentUser()`.
2. Attempt to parse the user's `Id` string into a `Guid`.
3. Fall back to a hard-coded "system/mock user" GUID (`00000000-0000-0000-0000-000000000001`) when parsing fails.
4. Stamp the result onto `request.UserId` before sending the request via MediatR.

The duplication means a future change to the fallback GUID, the fallback policy, or the user-id source must touch two controller methods. The logic is invisible to handler unit tests, so handler behavior cannot be verified end-to-end without spinning up the controller. No other action in the controller depends on `ICurrentUserService`, so the dependency exists solely to support this duplicated block.

The fix consolidates the rule into the handlers (or a shared helper they depend on), removes the controller's dependency on `ICurrentUserService`, and keeps observable behavior identical.

## Functional Requirements

### FR-1: Controller actions become thin pass-throughs
`LogisticsController.CreateGiftPackageManufacture` and `LogisticsController.DisassembleGiftPackage` must contain no logic beyond dispatching the request to MediatR and returning the response via the existing `HandleResponse` helper. The shape matches every other action in the controller:

```csharp
public async Task<ActionResult<TResponse>> Action(
    [FromBody] TRequest request,
    CancellationToken cancellationToken)
{
    return HandleResponse(await _mediator.Send(request, cancellationToken));
}
```

**Acceptance criteria:**
- Both action methods consist of a single `return HandleResponse(await _mediator.Send(...))` statement.
- Neither action method references `ICurrentUserService`, `Guid.TryParse`, or any hard-coded GUID literal.
- `LogisticsController` no longer declares `ICurrentUserService` as a constructor dependency.
- All other action methods in the controller are untouched.

### FR-2: Handlers resolve the acting user's ID
`CreateGiftPackageManufactureHandler` and `DisassembleGiftPackageHandler` are responsible for obtaining the acting user's GUID at the start of their `Handle` method, before any business work. The resolution must:

- Read the current user via `ICurrentUserService.GetCurrentUser()`.
- Parse `currentUser.Id` as a `Guid`.
- Fall back to the system-user GUID `00000000-0000-0000-0000-000000000001` when parsing fails.
- Use the resolved GUID for downstream persistence and domain calls that previously read `request.UserId`.

The behavior must be byte-for-byte identical to the current controller behavior for both the happy path (parseable GUID) and the fallback path (null/unparseable ID).

**Acceptance criteria:**
- Both handlers depend on `ICurrentUserService` via constructor injection.
- Both handlers produce the same persisted `UserId` value as before the refactor for every combination of authenticated/anonymous/mock user.
- Handler unit tests cover (a) parseable GUID happy path, (b) unparseable/empty/null user-id fallback to system GUID, and (c) the resolved GUID flows into the same downstream calls that previously consumed `request.UserId`.

### FR-3: Shared resolution helper
Because the resolution logic is identical in both handlers and may be reused by future logistics handlers, extract it into a shared helper rather than copy-pasting it into each handler. The helper:

- Lives in the Logistics module (suggested: `backend/src/Anela.Heblo.Application/Features/Logistics/UserIdResolver.cs` or analogous path consistent with the module's filesystem conventions).
- Exposes a single method, e.g. `Guid Resolve()` or `Guid ResolveCurrentUserId()`, that performs the read + parse + fallback.
- Depends on `ICurrentUserService` via constructor injection.
- Is registered in the Logistics module's DI registration alongside its other application services.
- Defines the fallback GUID as a single named `private static readonly` constant (e.g. `SystemUserId`) inside the helper, not as a magic literal.

**Acceptance criteria:**
- The fallback GUID literal `00000000-0000-0000-0000-000000000001` appears exactly once in the codebase after the refactor.
- Both handlers depend on the helper (directly or via `ICurrentUserService` if the helper is judged unnecessary — see Open Questions) instead of inlining the parsing block.
- The helper has direct unit tests covering parse success and fallback paths.

### FR-4: Request DTO `UserId` property
The current request DTOs (`CreateGiftPackageManufactureRequest`, `DisassembleGiftPackageRequest`) expose a `UserId` property that the controller currently writes. After the refactor, the controller no longer writes this property, and the handler resolves the user from `ICurrentUserService` directly.

The `UserId` property must be **removed** from both request DTOs unless it is part of the OpenAPI client surface that external callers populate (it is not — it is a server-side concern populated from the auth context).

**Acceptance criteria:**
- `CreateGiftPackageManufactureRequest.UserId` and `DisassembleGiftPackageRequest.UserId` are removed.
- The generated TypeScript OpenAPI client no longer exposes a `userId` field on these requests.
- Frontend call sites do not send `userId` (verify nothing in `frontend/src` references it before deletion; if anything does, that call site must be updated to omit the field).
- The OpenAPI client regenerates cleanly on `dotnet build`.

### FR-5: Behavior preservation
The HTTP contract, response shapes, status codes, and persisted side effects of `POST /api/logistics/gift-packages/manufacture` and `POST /api/logistics/gift-packages/disassemble` must be identical before and after the refactor for all input combinations.

**Acceptance criteria:**
- Existing handler unit tests pass without modification beyond DI wiring changes (mocking `ICurrentUserService` instead of receiving `UserId` in the request).
- Existing integration tests for both endpoints pass without changes to test inputs or assertions on the response.
- Manual smoke test on staging: both endpoints succeed with an authenticated user and record the expected `UserId` in audit/history columns.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected. The work moved from the controller to the handler is a few CPU cycles; both layers run in the same request pipeline. No new I/O, no new dependencies on external services.

### NFR-2: Security
- The fallback to the system-user GUID is preserved exactly as-is; this refactor does not change who is permitted to invoke these endpoints or what user context is recorded.
- Existing endpoint authorization (`[Authorize]` attributes inherited from the controller or applied per-action) remains untouched.
- No new attack surface: the helper does not log or echo the raw user-id string.

### NFR-3: Testability
Handler unit tests must be able to exercise the user-id resolution branch directly by mocking `ICurrentUserService`. The mock must allow:
- Returning a valid GUID string → handler uses it.
- Returning a non-GUID string (e.g. `"mock-user"`, empty, whitespace) → handler falls back to system GUID.
- Returning `null` for the user or its `Id` → handler falls back to system GUID.

### NFR-4: Code quality
- After the refactor, `LogisticsController.cs` is shorter and uniform: every action method is a one-liner.
- The fallback GUID literal exists exactly once in the codebase.
- No code references `ICurrentUserService` in the API/Controllers layer for the logistics endpoints.

## Data Model
No schema changes. Persisted columns (`UserId` on the gift-package manufacture/disassembly audit/history rows) continue to receive the same values as today.

The only model-level change is the **removal** of the `UserId` property from the two request DTOs (`CreateGiftPackageManufactureRequest`, `DisassembleGiftPackageRequest`). All other DTO fields are untouched.

## API / Interface Design

### HTTP surface (unchanged externally)
- `POST /api/logistics/gift-packages/manufacture` — request body no longer includes `userId`. Response shape unchanged.
- `POST /api/logistics/gift-packages/disassemble` — request body no longer includes `userId`. Response shape unchanged.

### Internal interfaces
- **New (suggested):** `UserIdResolver` (concrete class) in `Anela.Heblo.Application/Features/Logistics/` with a single method `Guid Resolve()`. Constructor takes `ICurrentUserService`. Registered as scoped in the Logistics module's DI registration class.
- **Modified:** `CreateGiftPackageManufactureHandler` and `DisassembleGiftPackageHandler` constructors gain a dependency on `UserIdResolver` (or `ICurrentUserService` directly — see Open Questions).
- **Modified:** `LogisticsController` constructor loses its `ICurrentUserService` parameter.

### UI flow
No UI changes. Frontend continues to call the same endpoints with the same request payloads minus the `userId` field (which it does not appear to send today; verify).

## Dependencies
- `ICurrentUserService` (existing) — moves from controller injection to handler injection (or into the new `UserIdResolver`).
- MediatR (existing) — unchanged.
- OpenAPI client generation pipeline — regenerates TypeScript types on next build to reflect DTO field removal.
- Logistics module DI registration class — gains the `UserIdResolver` registration if FR-3's helper approach is taken.

No new NuGet packages, no new external services.

## Out of Scope
- Refactoring user-id resolution patterns in other modules/controllers (Catalog, Manufacture, Accounting, etc.) — even if they exhibit the same pattern.
- Replacing the hard-coded system-user GUID with a configuration value or a different sentinel — preserve the existing literal exactly.
- Changing the authentication scheme, the `ICurrentUserService` contract, or the upstream identity provider behavior.
- Auditing or reworking other action methods in `LogisticsController`.
- Database migrations (none required).
- Frontend changes beyond what the OpenAPI client regeneration produces automatically.

## Open Questions
None.

## Status: COMPLETE