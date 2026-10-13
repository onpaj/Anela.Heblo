# Architecture Review: Bulk mutation support in IUserDashboardSettingsMutator to eliminate SaveUserSettingsHandler's duplicated scaffold

## Skip Design: true

This is a backend-only internal refactor of a MediatR handler and an `internal` infrastructure
interface within the Dashboard module. No new/changed UI components, screens, layouts, request/
response DTOs, or HTTP contracts are involved — `SaveUserSettingsRequest`/`SaveUserSettingsResponse`
are unchanged, and the frontend dashboard save flow observes no behavioral difference.

## Architectural Fit Assessment

This aligns cleanly with the codebase's existing conventions and requires no new patterns:

- **Vertical slice / module boundary**: all touched types (`IUserDashboardSettingsMutator`,
  `UserDashboardSettingsMutator`, `SaveUserSettingsHandler`, `SaveUserSettingsHandlerTests`) live
  entirely inside the `Dashboard` module (`Anela.Heblo.Application/Features/Dashboard/`). No
  cross-module boundary is crossed, no new `contracts/` surface is added — `UserDashboardTileDto`
  already lives in `Dashboard/Contracts/` and is reused as-is.
- **DI**: `IUserDashboardSettingsMutator` is already registered as `AddScoped` in
  `DashboardModule.AddDashboardModule()`. Widening its interface with one more method requires
  **no DI change** — the existing `UserDashboardSettingsMutator` registration covers the new
  method automatically.
- **Handler shape**: after the refactor, `SaveUserSettingsHandler` has the exact same shape as
  the already-existing `EnableTileHandler`/`DisableTileHandler` — inject `IUserDashboardSettingsMutator`
  + `ICurrentUserService`, delegate, return response. This is a convergence onto an established
  in-repo pattern, not a new one.
- **DTOs-are-classes rule**: no new DTOs are introduced. `UserDashboardTileDto` (already a class)
  and the existing `UserDashboardSettingsMutationResult` (an existing `internal readonly record
  struct` — a domain/internal value type, not an API contract, so the "DTOs are classes" rule
  correctly does not apply to it) are reused unchanged.
- **Identity resolution (ADR-005)**: unaffected. `SaveUserSettingsHandler` continues to resolve
  `ICurrentUserService.GetCurrentUser().Id` inside the handler; the mutator continues to own
  anonymous-fallback normalization internally, exactly as `MutateAsync` already does. No
  behavior here changes.

I confirmed this by reading `docs/architecture/development_guidelines.md` (contracts/DTO rules,
module independence, DI patterns, identity-resolution ADR-005 summary) and by reading the actual
source: `SaveUserSettingsHandler.cs`, `IUserDashboardSettingsMutator.cs`,
`UserDashboardSettingsMutator.cs`, `EnableTileHandler.cs`, `DisableTileHandler.cs`,
`GetUserSettingsHandler.cs`, `IUserDashboardSettingsLock.cs`, `DashboardModule.cs`, and the
existing `SaveUserSettingsHandlerTests.cs` test file. Nothing in the proposed change conflicts
with any documented rule or existing convention.

## Proposed Architecture

### Component Overview

```
Before:
  SaveUserSettingsHandler
    ├── IUserDashboardSettingsRepository  (direct)
    ├── IUserDashboardSettingsLock        (direct)
    ├── TimeProvider                       (direct)
    ├── IMediator                          (direct, sends GetUserSettingsRequest)
    └── ICurrentUserService

  EnableTileHandler / DisableTileHandler
    └── IUserDashboardSettingsMutator ──► (repository, lock, TimeProvider, mediator)
    └── ICurrentUserService

After:
  SaveUserSettingsHandler
    ├── IUserDashboardSettingsMutator ──► MutateBulkAsync(userId, tiles, ct)
    └── ICurrentUserService

  IUserDashboardSettingsMutator
    ├── MutateAsync(userId, tileId, onTileFound, onTileMissing, ct)      [unchanged, single-tile]
    └── MutateBulkAsync(userId, tiles, ct)                                [new, batch]
         both share: provision (mediator) → lock → load (repository) → mutate → save
```

All three handlers (`Enable`, `Disable`, `SaveUserSettings`) now have an identical dependency
shape and delegate 100% of the scaffold to the single mutator implementation.

### Key Design Decisions

#### Decision 1: Add a sibling `MutateBulkAsync` method rather than reshaping `MutateAsync`
**Options considered:**
1. Generalize `MutateAsync` itself to accept a list of tiles (single method, `tileId` becomes
   `IReadOnlyList<...>`, `onTileFound`/`onTileMissing` become per-tile callbacks invoked in a
   loop).
2. Add a new, separate `MutateBulkAsync` method alongside the existing `MutateAsync`, as the
   issue's suggested fix proposes.
3. Introduce a generic "batch mutation" abstraction (e.g. a mutation-plan object passed to a
   single generic `MutateAsync<TPlan>`).

**Chosen approach:** Option 2 — add `MutateBulkAsync` as a new method on the same interface.

**Rationale:** `EnableTileHandler`/`DisableTileHandler` use `MutateAsync`'s single-tile,
delegate-based shape (`onTileFound`/`onTileMissing`) specifically because each needs custom
per-call logic on find/miss (e.g. `EnableTile`'s `onTileMissing` computes `maxOrder + 1` and
appends; `DisableTile` passes `onTileMissing: null` to explicitly skip on miss). Reshaping
`MutateAsync` to a list-based signature would force those two call sites to either wrap a
single-element list (adding ceremony with no benefit) or split into two differently-shaped
methods anyway. `SaveUserSettings`'s batch semantics are also structurally simpler than the
delegate-based single-tile case — it always upserts (match-or-append) using the DTO's own
fields, with no custom per-tile callback needed — so a dedicated, simpler signature
(`IReadOnlyList<UserDashboardTileDto>` in, unconditional match-or-append, unconditional save) is
a better fit than forcing it through the callback shape. Option 3 (generic plan abstraction) is
rejected as over-engineering for two call shapes with only one consumer each of the "batch" side.
Two clearly-named, purpose-built methods on one small `internal` interface is consistent with the
existing interface's size and style (it already exposes exactly one method).

## Implementation Guidance

### Directory / Module Structure
No new files or directories. All changes are to existing files, all within
`backend/src/Anela.Heblo.Application/Features/Dashboard/`:

- `Infrastructure/IUserDashboardSettingsMutator.cs` — add `MutateBulkAsync` method signature +
  XML docs.
- `Infrastructure/UserDashboardSettingsMutator.cs` — implement `MutateBulkAsync`.
- `UseCases/SaveUserSettings/SaveUserSettingsHandler.cs` — rewrite to delegate to the mutator;
  drop the now-unused `IUserDashboardSettingsRepository`, `IUserDashboardSettingsLock`,
  `TimeProvider`, `IMediator` fields/constructor params and their `using` directives.

Test-side, within `backend/test/Anela.Heblo.Tests/Features/Dashboard/`:
- `SaveUserSettingsHandlerTests.cs` — rewrite to mock `IUserDashboardSettingsMutator` (as
  `EnableTileHandlerTests.cs`/`DisableTileHandlerTests.cs` already do for the single-tile path),
  asserting `MutateBulkAsync` is invoked with the expected `userId` and `tiles` argument.
- A new fixture, `Infrastructure/UserDashboardSettingsMutatorTests.cs`, should be added to
  directly cover `MutateBulkAsync`'s scaffold behavior — lock-once-per-call, provision-before-
  lock ordering, anonymous-userId fallback, unconditional `UpdateAsync` on found settings
  (including empty `tiles`), no `UpdateAsync` on null settings, shared `LastModified` timestamp
  across all touched/appended tiles in one call, and match-vs-append tile semantics. This
  directly carries forward the scaffold-level test coverage that today lives, somewhat
  accidentally, inside `SaveUserSettingsHandlerTests.cs` (see NFR-2 in the spec) — without it,
  that coverage would simply be lost when the handler test is narrowed to a thin delegation
  check. (There is currently no `UserDashboardSettingsMutatorTests.cs` at all — `MutateAsync`'s
  scaffold is exercised today only indirectly via `EnableTileHandlerTests`/`DisableTileHandlerTests`.
  Adding a direct fixture for the new method is recommended but does not require retrofitting
  equivalent direct coverage for the pre-existing `MutateAsync` — that is out of scope here.)

### Interfaces and Contracts

```csharp
// IUserDashboardSettingsMutator.cs — add alongside existing MutateAsync
Task<UserDashboardSettingsMutationResult> MutateBulkAsync(
    string? userId,
    IReadOnlyList<UserDashboardTileDto> tiles,
    CancellationToken cancellationToken);
```

- Reuses the existing `UserDashboardSettingsMutationResult` record struct as-is — no new result
  type. `TileFound`/`TileAppended` become "at least one tile in the batch was found / appended"
  rather than "the one tile was found / appended"; this is a reasonable, low-risk widening of
  meaning since `SaveUserSettingsHandler` (the only caller) ignores the result entirely today and
  is specified to keep doing so (spec FR-3).
- `tiles` is `IReadOnlyList<UserDashboardTileDto>`, not `UserDashboardTileDto[]`, to decouple the
  interface from the request DTO's array-typed field and avoid callers needing to `?? []` into an
  array specifically — `SaveUserSettingsHandler` converts `request.Tiles` (nullable array) to an
  empty list when null before calling.
- Keep `MutateBulkAsync` XML-documented with the same provisioning-order invariant already on
  the interface's class-level `<remarks>`, per spec FR-1 — do not let the invariant live only on
  `MutateAsync`'s doc comment where a reader of `MutateBulkAsync` alone would miss it.

### Data Flow

1. `SaveUserSettingsHandler.Handle` resolves `userId` from `ICurrentUserService` (unchanged) and
   calls `_mutator.MutateBulkAsync(userId, request.Tiles ?? [], cancellationToken)`.
2. `UserDashboardSettingsMutator.MutateBulkAsync`:
   - Normalizes `userId` → `resolvedUserId` (empty/null → `"anonymous"`).
   - `await _mediator.Send(new GetUserSettingsRequest(), ct)` (provisioning, outside lock).
   - `await using var lockHandle = await _lock.AcquireAsync(resolvedUserId, ct)`.
   - `var settings = await _repository.GetByUserIdAsync(resolvedUserId)`.
   - If `settings is null` → return early, no write (mirrors current handler's null-branch and
     `MutateAsync`'s own null-branch).
   - Else: single `now = _timeProvider.GetUtcNow().DateTime`; for each tile DTO, match-by-`TileId`
     and update, or append a new `UserDashboardTile`; set `settings.UserId`/`settings.LastModified`;
     `await _repository.UpdateAsync(settings)` unconditionally.
   - Return `UserDashboardSettingsMutationResult` (ignored by the caller).
3. `SaveUserSettingsHandler` returns `new SaveUserSettingsResponse()` unconditionally, exactly as
   today.

No new external dependency, no new database call, no new lock — this is a straight relocation of
existing logic from the handler into the mutator with an unchanged call sequence and count.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `MutateBulkAsync`'s "always call `UpdateAsync` when settings found" behavior is easy to get wrong if implemented by analogy to `MutateAsync`'s *conditional* write (which skips `UpdateAsync` when no tile matched and `onTileMissing` is null/returns null) | Medium | Spec FR-2 step 7 and NFR-2 call this out explicitly with a direct pointer to the two existing tests (`Handle_WhenNoTiles_ShouldSaveEmptySettings`, `Handle_WhenNullTiles_ShouldNotMutateExistingTiles`) that pin this exact behavior; the new `UserDashboardSettingsMutatorTests` fixture must include an equivalent "empty tiles still triggers UpdateAsync" case |
| Rewriting `SaveUserSettingsHandlerTests.cs` to mock the mutator instead of its dependencies could silently drop assertions that currently exercise scaffold-level behavior (lock-once, provision-order) with no replacement | Medium | Implementation guidance above mandates a new `UserDashboardSettingsMutatorTests.cs` fixture that takes over exactly those assertions against `MutateBulkAsync` directly, so no coverage is lost, only relocated |
| `MutateBulkAsync`'s `TileFound`/`TileAppended` semantics ("any" instead of "the one") could mislead a future caller that isn't `SaveUserSettingsHandler` if one is added later and assumes single-tile semantics | Low | XML-document the "any tile in the batch" semantics directly on `MutateBulkAsync`'s doc comment (not just inherited from the shared result type's existing docs, which describe the single-tile case) |
| None of `GetByUserIdAsync`/`UpdateAsync`/`AcquireAsync` signatures change, so no ripple into `IUserDashboardSettingsRepository`/`IUserDashboardSettingsLock` or their implementations/tests | N/A (not a risk) | — |

## Specification Amendments

None. The spec (`spec.r1.md`) already fully anticipates this design, including the
"unconditional `UpdateAsync`" nuance (FR-2 step 7) and the test-migration concern (NFR-2). One
clarification worth folding into the plan (not a spec change, since it's already implied by
NFR-2's phrasing): the planner should schedule the new
`Infrastructure/UserDashboardSettingsMutatorTests.cs` fixture as part of the same task/commit
that implements `MutateBulkAsync`, not as a follow-on, so the scaffold-level coverage is never
left uncovered even transiently between commits.

## Prerequisites

None. No migrations, no config, no new infrastructure. `IUserDashboardSettingsMutator` is
already DI-registered; widening its interface requires no `DashboardModule.cs` change since the
one concrete implementation (`UserDashboardSettingsMutator`) is edited in place. This can be
implemented and merged in a single self-contained change.
