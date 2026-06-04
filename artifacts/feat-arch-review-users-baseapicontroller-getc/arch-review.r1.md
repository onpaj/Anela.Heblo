```markdown
# Architecture Review: Consolidate User Identity Resolution via `ICurrentUserService`

## Skip Design: true

Backend refactor only. No new screens, no UI components, no visual decisions. The frontend touch is purely the regenerated OpenAPI client losing two fields — confirmed against `frontend/src/api/hooks/useDashboard.ts`, `useCarrierCooling.ts`, `useGiftSetting.ts`, none of which send `userId` / `modifiedBy` today.

## Architectural Fit Assessment

This refactor **removes a deviation** from an already-established pattern; it does not introduce a new one. `development_guidelines.md` lists "Business logic in Controller class" as a forbidden practice, and 60+ existing handlers already inject `ICurrentUserService` (Purchase, Logistics, Marketing, Journal, Manufacture, Catalog, Article, InvoiceClassification, …). The three controllers in scope are the outliers. The proposal mirrors the canonical Marketing handler pattern (`CreateMarketingActionHandler.cs:40-45`) — verified — for the Unauthorized-on-missing-id case, and keeps the existing `"anonymous"` defense-in-depth pattern intact for the three Dashboard read/write handlers that already have it.

Integration points are minimal and well-contained:
- `BaseApiController` (shrinks by 5 lines)
- 7 handler constructors gain one DI parameter
- 7 request DTOs lose one property each
- 1 test file deleted, 1 (already existing) `CurrentUserServiceTests` extended with the priority-chain scenarios that the spec calls out as missing
- Regenerated TS API client; no handwritten frontend change required

No new module boundary is crossed. No interface contract changes for `ICurrentUserService`. No DI lifetime changes.

## Proposed Architecture

### Component Overview

```
Before (3 outlier controllers):
  Client → Controller.Method()
            └─ GetCurrentUserId()  ←  claim chain #1 (duplicated)
            └─ request.UserId = …  ←  identity stamped on contract
            └─ Mediator.Send(request)
                 └─ Handler.Handle(request)
                      └─ reads request.UserId

After (canonical pattern, already used by ~60 handlers):
  Client → Controller.Method()
            └─ Mediator.Send(request)
                 └─ Handler.Handle(request)
                      └─ ICurrentUserService.GetCurrentUser()  ←  single owner
                      └─ branch on null-id per handler policy:
                           • Dashboard (5)   : fallback "anonymous"
                           • CarrierCooling  : return Unauthorized
                           • GiftSettings    : return Unauthorized
```

### Key Design Decisions

#### Decision 1: Do NOT add a convenience method to `ICurrentUserService`

**Options considered:**
- A. Add `string GetRequiredUserId()` (throws or returns Unauthorized sentinel).
- B. Add `string GetUserIdOrDefault(string fallback)`.
- C. No new method; each handler inlines its policy.

**Chosen:** C.

**Rationale:** Three distinct null-handling policies coexist after this refactor:
1. Dashboard handlers → fallback to literal `"anonymous"` (defense-in-depth; `[Authorize]` already guards the endpoint).
2. CarrierCooling / GiftSettings → typed `ErrorCodes.Unauthorized` failure response (audit-trail integrity — `ModifiedBy` must never be a placeholder).
3. `CurrentUserExtensions.GetIdentifier()` → fallback to `"system"` (used elsewhere for non-user-attributable writes).

A single helper cannot serve all three without forcing two of them to bypass it, which negates the helper's value. The spec calls this out and explicitly puts a new helper out of scope. Endorse.

#### Decision 2: How handlers resolve `userId` for the `IUserDashboardSettingsMutator`

The mutator (`UserDashboardSettingsMutator.cs:33`) currently accepts `string? userId` and normalizes empty → `"anonymous"` itself. Two options:

- A. Keep the mutator signature as-is. `EnableTileHandler` / `DisableTileHandler` resolve `var userId = _currentUserService.GetCurrentUser().Id;` and pass it to `MutateAsync(userId, …)`. The mutator continues to own normalization.
- B. Inject `ICurrentUserService` into the mutator and drop the `userId` parameter.

**Chosen:** A.

**Rationale:** The mutator is an internal infrastructure helper, not a use-case boundary. Pushing identity into it would (1) break symmetry with `GetUserSettingsHandler` and `SaveUserSettingsHandler`, which still need to resolve identity themselves because their flow is more involved than a single mutate call, and (2) make the mutator non-trivial to call in tests that already work today by passing a known userId. Keep identity resolution at the handler boundary; the mutator's `userId` parameter is fine.

#### Decision 3: Where the Unauthorized branch lives in `SetCarrierCoolingHandler` / `SetGiftSettingHandler`

**Chosen:** Top of `Handle(…)`, before validation. Match `CreateMarketingActionHandler.cs:40-45`:

```csharp
var currentUser = _currentUserService.GetCurrentUser();
if (string.IsNullOrEmpty(currentUser.Id))
{
    return new SetCarrierCoolingResponse
    {
        Success = false,
        ErrorCode = ErrorCodes.Unauthorized,
    };
}
// existing validation continues with currentUser.Id available
```

**Rationale:** Unauthorized is the cheapest check; running it before payload validation avoids leaking validation feedback to an unidentified caller and matches the established pattern. The spec's spec is silent on order — pin it here for consistency.

Do **not** check `currentUser.IsAuthenticated` in addition. The endpoints carry `[Authorize]`; reaching the handler with `IsAuthenticated == false` is impossible. Marketing checks it; we don't have to. Just check `Id` since `Id` is the value we need.

#### Decision 4: Domain-layer null-id signalling

Considered: introducing a typed `UserIdentity` value object / Result-style wrapper across the domain. **Rejected.** YAGNI. The codebase has lived with the current `CurrentUser.Id` nullable string for 60+ handlers and is not changing here. This refactor is a deduplication, not a domain redesign.

## Implementation Guidance

### Directory / Module Structure

No new directories, no new files (beyond extending an existing test file). All edits are in-place:

| File | Change |
|------|--------|
| `backend/src/Anela.Heblo.API/Controllers/BaseApiController.cs` | Delete `GetCurrentUserId()` (lines 75-79). Remove `using System.Security.Claims;` (line 3) — `Claims` is used nowhere else in the file. |
| `backend/src/Anela.Heblo.API/Controllers/DashboardController.cs` | Remove all 5 `GetCurrentUserId()` calls and all `UserId = userId` assignments. Body shapes shrink accordingly. |
| `backend/src/Anela.Heblo.API/Controllers/CarrierCoolingController.cs` | Remove line 34 (`request.ModifiedBy = GetCurrentUserId();`). |
| `backend/src/Anela.Heblo.API/Controllers/GiftSettingsController.cs` | Remove line 33 (`command.ModifiedBy = GetCurrentUserId();`). |
| 5 Dashboard `…Request.cs` files | Remove `UserId` property. |
| `SetCarrierCoolingRequest.cs` | Remove `ModifiedBy` property. |
| `SetGiftSettingCommand.cs` | Remove `ModifiedBy` property. |
| 7 handler files | Add `ICurrentUserService` ctor injection; inline resolution per policy. |
| `backend/test/Anela.Heblo.Tests/Controllers/BaseApiControllerTests.cs` | Delete entire file. |
| `backend/test/Anela.Heblo.Tests/Features/Users/CurrentUserServiceTests.cs` | Already exists. Add the 3 missing priority-chain scenarios (NameIdentifier > sub > oid, sub > oid, null when no claim) — 5 of FR-6's 6 scenarios are already covered (verified). |
| Existing handler tests (Dashboard, CarrierCooling, GiftSetting) | Inject mocked `ICurrentUserService`. |

### Interfaces and Contracts

`ICurrentUserService` — **unchanged**. (Domain layer, do not touch.)

`CurrentUserExtensions` — **unchanged**. (Out of scope per spec.)

7 new constructor signatures:

```csharp
// Pattern shared by all 7 handlers
public XxxHandler(/* existing deps */, ICurrentUserService currentUserService)
{
    /* … */
    _currentUserService = currentUserService;
}
```

Constructor parameter order: append `ICurrentUserService` as the **last** parameter in every handler. This minimizes diff churn in existing tests that construct handlers positionally.

Request DTO contracts — see spec FR-2/3/4 for exact field removals. Note for the implementer: these are MediatR request classes and live in `Anela.Heblo.Application`. They are NOT the OpenAPI-exposed `[FromBody]` types in the documented sense — but they ARE the body shape because the controllers use them as `[FromBody]`. NSwag will regenerate the TS client accordingly. The DTO-classes-never-records rule from `CLAUDE.md` still applies; keep them as `class` with `{ get; set; }`.

### Data Flow

For `POST /api/carrier-cooling`:

```
1. ASP.NET binds JSON body → SetCarrierCoolingRequest (no ModifiedBy)
2. Controller forwards to MediatR (no controller-side mutation)
3. Handler:
   a. _currentUserService.GetCurrentUser() → CurrentUser
   b. if (Id is null/empty) → return Unauthorized response (HandleResponse maps to 401)
   c. validate Carrier/DeliveryHandling combo (existing)
   d. construct CarrierCoolingSetting(..., modifiedBy: currentUser.Id, ...)
   e. repository.UpsertAsync
4. Controller's HandleResponse(response) → 200 / 401 / 4xx
```

For Dashboard endpoints — identical except step 3a/3b is replaced with `var userId = currentUser.Id ?? "anonymous";` (preserves today's behavior bit-for-bit).

`EnableTileHandler` / `DisableTileHandler` then pass the resolved `userId` to `_mutator.MutateAsync(userId, …)` — the mutator's existing normalization (`string.IsNullOrEmpty(userId) ? "anonymous" : userId` at `UserDashboardSettingsMutator.cs:33`) becomes a redundant safety net but should be **kept** (don't widen the change surface).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Existing Dashboard handler tests rely on positional ctor args | Medium | Append `ICurrentUserService` as the last ctor param (Decision in §Interfaces). Update tests in lockstep — they're in the same PR. |
| Generated TS client churn surfaces unexpected references | Low | Spec's audit already verified hooks don't send the fields. FR-5 mandates a post-regen grep. Run `npm run build` to surface any remaining types. |
| `IsAuthenticated` invariant assumed but not asserted in Marketing-style guard | Low | Only check `Id` (not `IsAuthenticated`) — `[Authorize]` enforces auth at the pipeline; `Id` is what we actually need. |
| `CurrentUserServiceTests` partial existence not noticed → spec writer adds duplicates | Low | The file at `backend/test/Anela.Heblo.Tests/Features/Users/CurrentUserServiceTests.cs` already covers 5 of FR-6's 6 scenarios using `IHttpContextAccessor` + `DefaultHttpContext`. Only **add** the 3 priority-chain tests (NameIdentifier-over-sub-over-oid, sub-over-oid, all-absent → null). Do not recreate the file. |
| `BaseApiControllerTests.cs` continues to compile against a deleted member | Low | The file becomes uncompilable as a unit after FR-1; the spec calls for deletion of the entire file (FR-6). Verify by running `dotnet build` after removal. |
| `SetGiftSettingHandler` is `sealed` and tests may construct via reflection | Low | Search `Mock<SetGiftSettingHandler>` / `new SetGiftSettingHandler(` in tests before changing the ctor. Standard ctor injection works with `sealed`. |
| Two handlers (`EnableTileHandler`, `DisableTileHandler`) currently pass `request.UserId` to the mutator and that property is removed | Low | Inline the resolution in the handler (`var userId = _currentUserService.GetCurrentUser().Id;`) and pass `userId` to `_mutator.MutateAsync(userId, …)`. Mutator signature unchanged. |
| Forgetting that `CurrentUserService` is registered Singleton, not Scoped | Low | The spec's NFR-1 says "scoped" — that's wrong (see Amendments). Behavior is unaffected because `IHttpContextAccessor` is AsyncLocal-backed, which is exactly why `UsersModule.cs:13` keeps the singleton lifetime with an explanatory comment. Don't change the lifetime. |

## Specification Amendments

1. **NFR-1 wording correction.** `ICurrentUserService` is registered as a **Singleton** (`backend/src/Anela.Heblo.API/Features/Users/UsersModule.cs:14`), not Scoped. The comment in that file explains why this is safe (`IHttpContextAccessor` is AsyncLocal-backed). The non-functional point — "no measurable performance change" — stands; only the lifetime descriptor is wrong. Implementers should NOT change the lifetime as part of this work.

2. **FR-6 is partially already satisfied.** The file `backend/test/Anela.Heblo.Tests/Features/Users/CurrentUserServiceTests.cs` already exists (8 tests, including scenarios 1, 2, and 3 of the spec's list). Scenarios 4, 5, and 6 (priority chains and all-absent → null) are not yet covered there. Add **3 new tests**, not 6, and do not recreate the file.

3. **Controller-side `request.UserId` for `EnableTileRequest` / `DisableTileRequest`.** These two request types are sent via routes that take `tileId` from the path — they have no body in the JSON contract. The `UserId` property therefore had no client-supplied value to begin with. Removing it is purely an internal change for these two; no contract regeneration impact beyond the field disappearing from the TS request type. Spec doesn't say otherwise but it's worth flagging so the implementer doesn't expect a frontend code change for these two.

4. **Handler constructor parameter ordering.** Append `ICurrentUserService` as the **last** ctor parameter in all 7 handlers. Reduces test churn (positional ctor args in existing unit tests stay valid; only the new last arg needs to be supplied). Spec is silent on ordering.

5. **`SetCarrierCoolingHandler` / `SetGiftSettingHandler` early-return position.** Place the Unauthorized check at the **top** of `Handle(…)`, before payload validation. Matches `CreateMarketingActionHandler.cs:40-45` and avoids leaking validation feedback to an unidentified caller. Spec didn't pin this.

6. **`EnableTileHandler` / `DisableTileHandler` interaction with the mutator.** Resolve `userId` in the handler from `ICurrentUserService` and pass it to `_mutator.MutateAsync(userId, …)`. Keep the mutator's existing `userId` parameter and its empty→`"anonymous"` normalization. Do **not** inject `ICurrentUserService` into the mutator — that widens the change surface and breaks symmetry with the other 3 Dashboard handlers. Spec offers both options; pin this one.

## Prerequisites

None. All of the following already exist and are verified:

- `ICurrentUserService` (Domain layer, unchanged interface)
- `CurrentUserService` (registered in DI via `UsersModule.AddUsersModule`)
- `ErrorCodes.Unauthorized` (value 0013, `HttpStatusCode.Unauthorized` attribute)
- `BaseApiController.HandleResponse<T>` maps `ErrorCodes.Unauthorized` to HTTP 401 via `Unauthorized(response)` (verified at `BaseApiController.cs:50`)
- `CurrentUserServiceTests` file (extend, don't create)
- The Marketing handler pattern that we're aligning with (verified at `CreateMarketingActionHandler.cs:40-45`)

No migrations. No config changes. No new infrastructure. Implementation can start immediately.
```