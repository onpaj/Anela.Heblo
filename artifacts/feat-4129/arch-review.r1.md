# Architecture Review: FeatureFlagsController identity resolution fix (ADR-005 compliance)

## Skip Design: true

Backend-only change: no new or changed UI components, screens, or visual design decisions. The client-facing request/response contract (`UpsertFlagOverrideBodyDto` / `UpsertFlagOverrideResponse`) is unchanged, so the existing feature-flags admin frontend needs no changes and no OpenAPI/TypeScript client regeneration.

## Architectural Fit Assessment

This aligns exactly with an existing, already-converged pattern rather than introducing anything new. ADR-005 (`docs/architecture/development_guidelines.md`, "User Identity Resolution") is Accepted and states its own consequence explicitly: *"arch-review should treat any controller-side identity resolution ... as a violation of an accepted decision, not a new finding."* `FeatureFlagsController.Put()` is exactly such a violation — it independently reads `User.Identity?.Name`, applies its own ad hoc fallback (`"unknown"`), and stamps the result onto a request DTO field (`UpdatedBy`), instead of letting the handler resolve identity via `ICurrentUserService`.

The codebase already has multiple converged examples of the correct pattern in the same architectural layer this change touches — e.g. `UpdatePackingMaterialQuantityHandler`, `ScanPackingOrderHandler`, `SaveMindMapDocumentHandler`, `CreateJournalTagHandler` — all of which inject `ICurrentUserService` into the handler and call `GetCurrentUser()` there. `FeatureFlagsController`'s other three actions (`Get`, `GetAdmin`, `Delete`) are already plain dispatchers with zero business logic; `Put()` is the sole outlier in its own controller. This fix brings `Put()` in line with both its sibling actions and the rest of the codebase — there is only one viable approach, not a set of tradeoffs to weigh.

**Integration points:**
- `FeatureFlagsController.Put()` — controller action, loses its identity-resolution branch.
- `UpsertFlagOverrideRequest` — MediatR request DTO, loses `UpdatedBy` property.
- `UpsertFlagOverrideHandler` — MediatR handler, gains `ICurrentUserService` constructor dependency and resolves `updatedBy` internally.
- `ICurrentUserService` / `CurrentUser` / `CurrentUserExtensions.GetDisplayName()` (`Anela.Heblo.Domain.Features.Users`) — consumed, not modified. Already DI-registered application-wide.

No other component touches `UpsertFlagOverrideRequest.UpdatedBy` (confirmed by search — the property is only read in `UpsertFlagOverrideHandler` and only ever set in `FeatureFlagsController.Put()`), so the blast radius of the DTO change is exactly these two files plus the controller.

## Proposed Architecture

### Component Overview

```
Before:
  HTTP PUT /api/feature-flags/admin/{key}
        │
        ▼
  FeatureFlagsController.Put()
        │  reads User.Identity?.Name directly  ← ADR-005 violation
        │  logs warning + applies "unknown" fallback
        │  builds UpsertFlagOverrideRequest { Key, IsEnabled, UpdatedBy }
        ▼
  MediatR ─▶ UpsertFlagOverrideHandler.Handle()
                  reads request.UpdatedBy (trusts controller)
                  → IFeatureFlagOverrideRepository.UpsertAsync(key, isEnabled, updatedBy)

After:
  HTTP PUT /api/feature-flags/admin/{key}
        │
        ▼
  FeatureFlagsController.Put()          (straight-through dispatcher, like Get/GetAdmin/Delete)
        │  builds UpsertFlagOverrideRequest { Key, IsEnabled }
        ▼
  MediatR ─▶ UpsertFlagOverrideHandler.Handle()
                  _currentUserService.GetCurrentUser().GetDisplayName()  ← resolved here
                  → IFeatureFlagOverrideRepository.UpsertAsync(key, isEnabled, updatedBy)
```

### Key Design Decisions

#### Decision 1: Where to resolve identity

**Options considered:**
- (a) Leave resolution in the controller but switch it from `User.Identity?.Name` to calling `ICurrentUserService` there instead.
- (b) Move resolution into `UpsertFlagOverrideHandler`, per ADR-005.

**Chosen approach:** (b) — inject `ICurrentUserService` into `UpsertFlagOverrideHandler` and resolve inside `Handle()`.

**Rationale:** Option (a) is explicitly forbidden by ADR-005 regardless of which identity API is used ("Controllers never resolve identity ... no `ICurrentUserService` injection" in the controller). Only (b) is a compliant fix, and it is also literally the pattern this issue's linked finding recommends and that every other handler in the codebase already follows. There is no tradeoff to make here.

#### Decision 2: Fallback value for unresolvable identity

**Options considered:**
- (a) Keep the literal `"unknown"` string as a bespoke fallback inside the handler.
- (b) Use `CurrentUserExtensions.GetDisplayName()`'s existing fallback (`"System"` for unauthenticated `CurrentUser`).

**Chosen approach:** (b).

**Rationale:** `GetDisplayName()` is the canonical, already-tested extension used by every other handler for exactly this purpose. Reintroducing a bespoke `"unknown"` fallback string next to it would recreate a second, divergent convention — the same category of drift ADR-005 was written to eliminate. The change from `"unknown"` to `"System"` is a cosmetic literal-string change to an edge case (endpoint requires `[FeatureAuthorize(..., AccessLevel.Write)]`, so an authenticated, authorized caller is expected on every real request; this path only triggers if `CurrentUser.IsAuthenticated` is somehow false despite passing authorization, which the existing code already treated as an unexpected/logged condition). This is flagged as an intentional change in the spec so downstream review doesn't mistake it for an accidental behavior change.

#### Decision 3: Whether to touch anything beyond the three files

**Options considered:**
- (a) Scope this fix narrowly to `FeatureFlagsController.Put()`, `UpsertFlagOverrideRequest`, `UpsertFlagOverrideHandler`.
- (b) Take the opportunity to sweep the rest of the codebase for other ADR-005 violations.

**Chosen approach:** (a).

**Rationale:** The linked issue (#4129) scopes the finding to this one endpoint. `arch-review` files one issue per finding by design; a broader sweep is a separate concern (and separate issue) if other violations exist. Surgical, traceable changes are also the repository's stated engineering norm.

## Implementation Guidance

### Directory / Module Structure

No new files or directories. Edits only, in place:
- `backend/src/Anela.Heblo.API/Controllers/FeatureFlagsController.cs` — simplify `Put()`.
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/UpsertFlagOverride/UpsertFlagOverrideRequest.cs` — remove `UpdatedBy` property.
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/UpsertFlagOverride/UpsertFlagOverrideHandler.cs` — inject `ICurrentUserService`, resolve `updatedBy` in `Handle()`.

### Interfaces and Contracts

- `UpsertFlagOverrideRequest` becomes:
  ```csharp
  public class UpsertFlagOverrideRequest : IRequest<UpsertFlagOverrideResponse>
  {
      public string Key { get; init; } = "";
      public bool IsEnabled { get; init; }
  }
  ```
- `UpsertFlagOverrideHandler` constructor gains `ICurrentUserService currentUserService` (alongside existing `IFeatureFlagOverrideRepository repo`, `IMemoryCache cache`); `using Anela.Heblo.Domain.Features.Users;` added for `ICurrentUserService` / `CurrentUserExtensions`.
- `Handle()` body: replace `request.UpdatedBy` with `_currentUserService.GetCurrentUser().GetDisplayName()` as the third argument to `_repo.UpsertAsync(...)`. No signature change to `IFeatureFlagOverrideRepository.UpsertAsync`.
- `FeatureFlagsController.Put()`: remove the `name`/`updatedBy`/warning-log lines; build `new UpsertFlagOverrideRequest { Key = key, IsEnabled = body.IsEnabled }` directly, matching the style of `GetAdmin`/`Delete` in the same file. `Logger` usage in this action goes away entirely — confirm `BaseApiController.Logger` is still used elsewhere in the file/project (it is not otherwise used in this controller; removing the one `Logger.LogWarning` call here is safe and does not require removing the inherited `Logger` member itself).

### Data Flow

Unchanged end-to-end shape: `HTTP PUT` → `FeatureFlagsController.Put()` → `IMediator.Send(UpsertFlagOverrideRequest)` → `UpsertFlagOverrideHandler.Handle()` → `IFeatureFlagOverrideRepository.UpsertAsync()` → cache invalidation (`_cache.Remove(HebloFeatureProvider.CacheKey)`) → `UpsertFlagOverrideResponse`. The only change is *where inside this pipeline* the `updatedBy` string value originates (handler instead of controller) and *how* it's derived (`ICurrentUserService` claim-priority chain instead of `User.Identity?.Name`).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Fallback string changes from `"unknown"` to `"System"` for the (authorized-but-unauthenticated-`CurrentUser`) edge case, surprising anyone grepping historical override audit data for the literal `"unknown"`. | Low | Called out explicitly in spec (FR-4) and here as an intentional, documented behavior change, not a defect. |
| Missed reference to `request.UpdatedBy` elsewhere causing a build break after removing the property. | Low | Confirmed via repo-wide search: `UpsertFlagOverrideRequest`/`UpsertFlagOverrideHandler` are referenced only in the three files listed above; no test files reference either type today. `dotnet build` in validation will catch any missed reference regardless. |
| No existing unit test for `UpsertFlagOverrideHandler` today, so a regression in the identity-resolution swap could go unnoticed. | Low | Planner should include adding/extending a handler unit test that asserts `_repo.UpsertAsync` is called with the value from `ICurrentUserService.GetCurrentUser().GetDisplayName()` (mocked), following the existing test style used for other `ICurrentUserService`-consuming handlers in the test project. |

## Specification Amendments

None — the spec (`spec.r1.md`) is already implementation-ready and consistent with this review; no changes required.

## Prerequisites

None. No migrations, config, or infrastructure changes needed — `ICurrentUserService` is already registered in DI and used throughout the Application layer.
