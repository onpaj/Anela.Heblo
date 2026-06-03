I have enough context. Producing the architecture review.

```markdown
# Architecture Review: Refactor EnableTileHandler / DisableTileHandler duplication

## Skip Design: true

Backend-only refactor inside the Application layer. No UI components, screens, layouts, or visual decisions are touched. The HTTP surface, MediatR contracts, and OpenAPI spec are explicitly preserved (FR-5), so the auto-generated TypeScript client is unchanged.

## Architectural Fit Assessment

The proposal aligns cleanly with three established patterns in this repository:

1. **Vertical Slice / module boundaries.** The new collaborator lives under `Application/Features/Dashboard/Infrastructure/` next to `IUserDashboardSettingsLock` / `UserDashboardSettingsLock`. Other modules use the same convention (`Catalog/Infrastructure/`, `KnowledgeBase/Infrastructure/`, `Manufacture/Infrastructure/`), so the placement is idiomatic. No cross-module reference is introduced.
2. **`internal` visibility for module-private collaborators.** The Application csproj already declares `<InternalsVisibleTo Include="Anela.Heblo.Tests" />`, and the codebase has 30+ internal types under `Features/**/Infrastructure/` and `Features/**/Services/` (e.g. `HebloFeatureProvider`, `FeatureFlagChecker`, `KnowledgeBaseLeafletSourceAdapter`). The mutator continues that convention — module-private, but reachable from tests.
3. **MediatR-handler-as-thin-shell.** Existing handlers in the project already delegate work to module-private collaborators (`GetUserSettingsHandler` → `ITileRegistry`; `ListFlagsHandler` → `IFeatureFlagChecker`). The proposed `EnableTileHandler` becoming a 1-dep shell is consistent.

The only **integration points** are `DashboardModule.AddDashboardModule` (one new `AddScoped` line) and the two test classes (fixture wiring only). No persistence, domain, controller, or contract change.

**One pre-existing finding the spec correctly defers:** `SaveUserSettingsHandler` carries the same scaffold (constructor, pre-lock `GetUserSettingsRequest`, lock acquire, null-guard, `LastModified` stamp, `UpdateAsync`). It is deliberately out of scope, but the chosen design — a delegate-driven mutator — leaves the door open to absorb it later without breaking the contract.

## Proposed Architecture

### Component Overview

```
DashboardController
       │
       │ POST /api/dashboard/tiles/{tileId}/{enable|disable}
       ▼
EnableTileRequest          DisableTileRequest          (unchanged MediatR contracts)
       │                          │
       ▼                          ▼
EnableTileHandler          DisableTileHandler          (thin shells; 1 dep each)
       │                          │
       └────────────┬─────────────┘
                    ▼
       IUserDashboardSettingsMutator           ← NEW (internal, scoped)
                    │
   ┌────────────────┼────────────────┬─────────────────┐
   ▼                ▼                ▼                 ▼
IMediator   IUserDashboardSettingsLock   IUserDashboardSettingsRepository   TimeProvider
(provision    (per-user write lock,         (EF-backed)                    (UTC clock)
 before       singleton, non-reentrant)
 lock)
```

Flow per request:

1. Handler validates `TileId`, calls `MutateAsync(userId, tileId, onTileFound, onTileMissing, ct)`.
2. Mutator normalizes `userId` (`"anonymous"` fallback), issues pre-lock `_mediator.Send(GetUserSettingsRequest)` to provision.
3. Mutator acquires the per-user lock.
4. Mutator loads settings; if null, returns `SettingsLoaded = false` without writing.
5. Mutator locates the tile; invokes the appropriate delegate.
6. Mutator stamps `tile.LastModified` + `settings.LastModified` (single `TimeProvider` read) and calls `UpdateAsync` iff a tile was found or appended.

### Key Design Decisions

#### Decision 1: Shared collaborator (Option A) over unified handler (Option B)

**Options considered:**
- A. Extract a `UserDashboardSettingsMutator` collaborator; keep both handlers.
- B. Collapse both handlers into a single `SetTileVisibilityHandler` driven by `bool IsVisible` on the request.

**Chosen approach:** Option A.

**Rationale:** Enable and Disable are not symmetric — Enable may *create*, Disable never does. Modeling them as one handler with a flag obscures that asymmetry and forces a runtime branch on a domain-shaped boundary. Option A keeps each intent as a distinct unit, preserves both MediatR contracts and the OpenAPI surface (zero TypeScript regeneration, zero controller diff), and matches the brief's explicit recommendation. The duplication being removed is the *infrastructure scaffold*, not the *domain intent*; Option A removes exactly the duplication that should be removed.

#### Decision 2: Delegate-based diff vs. strategy interface

**Options considered:**
- A. Pass `Action<UserDashboardSettings, UserDashboardTile>` + `Func<UserDashboardSettings, UserDashboardTile?>` delegates.
- B. Define a strategy interface (`ITileMutationStrategy`) with two methods; inject implementations.

**Chosen approach:** A (per spec).

**Rationale:** Two callers, both in the same module, both supplying tiny closures (4–7 LOC each). A strategy interface would multiply files, registrations, and indirection without buying anything testable that isn't already covered by the handler-level tests. Delegates keep the diff at the call site where domain intent is most readable.

#### Decision 3: `internal sealed` mutator + scoped lifetime

**Options considered:**
- A. `public` types, scoped.
- B. `internal sealed` types, scoped.
- C. `internal sealed`, singleton.

**Chosen approach:** B.

**Rationale:** `internal` enforces module privacy (consistent with `HebloFeatureProvider`, `FeatureFlagChecker`, etc.) while remaining test-reachable via `InternalsVisibleTo`. `sealed` is the C#-coding-style default. **Scoped** matches the request scope used by the MediatR handlers, EF `DbContext`, and `IUserDashboardSettingsRepository`. A singleton would risk capturing scoped dependencies and dragging EF context lifetime issues — avoid.

#### Decision 4: Centralize `LastModified` stamping inside the mutator

**Options considered:**
- A. Mutator stamps `tile.LastModified` and `settings.LastModified` from a single `TimeProvider` read.
- B. Delegates stamp `tile.LastModified`; mutator stamps `settings.LastModified`.

**Chosen approach:** A.

**Rationale:** Eliminates double-stamping risk, makes "all timestamps in a single mutation share the same instant" an invariant the mutator owns, and removes one more thing the delegate must remember. Spec aligns with this in FR-1.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Application/Features/Dashboard/
├── DashboardModule.cs                                  (modify: 1 new AddScoped line)
├── Infrastructure/
│   ├── IUserDashboardSettingsLock.cs                   (unchanged)
│   ├── UserDashboardSettingsLock.cs                    (unchanged)
│   ├── IUserDashboardSettingsMutator.cs                ← NEW
│   ├── UserDashboardSettingsMutator.cs                 ← NEW
│   └── UserDashboardSettingsMutationResult.cs          ← NEW (or co-located in interface file)
└── UseCases/
    ├── EnableTile/EnableTileHandler.cs                 (rewrite to thin shell)
    └── DisableTile/DisableTileHandler.cs               (rewrite to thin shell)
```

File naming follows the convention used by `IUserDashboardSettingsLock` / `UserDashboardSettingsLock` (interface and implementation in separate files). The `UserDashboardSettingsMutationResult` record struct may be co-located in the interface file (small type, one declaration site).

### Interfaces and Contracts

```csharp
// Anela.Heblo.Application.Features.Dashboard.Infrastructure

internal interface IUserDashboardSettingsMutator
{
    Task<UserDashboardSettingsMutationResult> MutateAsync(
        string? userId,
        string tileId,
        Action<UserDashboardSettings, UserDashboardTile> onTileFound,
        Func<UserDashboardSettings, UserDashboardTile?>? onTileMissing,
        CancellationToken cancellationToken);
}

internal readonly record struct UserDashboardSettingsMutationResult(
    bool SettingsLoaded,
    bool TileFound,
    bool TileAppended);
```

**Contract rules the implementation must honor (encode these in `<remarks>` XML doc):**

- `userId` normalization to `"anonymous"` lives inside the mutator. Handlers must not pre-normalize — that would create two sources of truth.
- `_mediator.Send(GetUserSettingsRequest)` must execute **before** `_lock.AcquireAsync`. The lock is non-reentrant; calling it from inside `GetUserSettingsHandler` (which itself takes the lock) deadlocks. This invariant is locked down by `Handle_SendsGetUserSettingsBeforeAcquiringLock` in both test classes — they must remain green.
- A single `_timeProvider.GetUtcNow().DateTime` read per mutation; reuse it for both `tile.LastModified` and `settings.LastModified`.
- `onTileFound` **must not** set `tile.LastModified`. The mutator owns timestamp stamping.
- `onTileMissing` **must not** set `LastModified` on the returned tile; the mutator stamps it before `UpdateAsync`.
- When `onTileMissing` returns `null`, no write occurs (`TileAppended = false`, no `UpdateAsync` call). This preserves today's Disable-when-missing no-op behavior.

**Handler shapes (illustrative, not literal code):**

```csharp
// EnableTileHandler
return await _mutator.MutateAsync(
    request.UserId,
    request.TileId,
    onTileFound: (_, tile) => tile.IsVisible = true,
    onTileMissing: settings =>
    {
        var maxOrder = settings.Tiles.Any() ? settings.Tiles.Max(t => t.DisplayOrder) : -1;
        return new UserDashboardTile
        {
            UserId = /* resolved userId from result or closure */,
            TileId = request.TileId,
            IsVisible = true,
            DisplayOrder = maxOrder + 1,
            DashboardSettings = settings
            // LastModified stamped by mutator
        };
    },
    cancellationToken)
    is { } _result
    ? new EnableTileResponse()
    : new EnableTileResponse();
```

**One subtle point the spec omits:** the Enable closure needs the *resolved* `userId` (so `UserDashboardTile.UserId` is `"anonymous"` when the request `UserId` was null). Two clean ways to expose it:

- **Preferred:** add `string ResolvedUserId` to `UserDashboardSettingsMutationResult` *and* pass it into `onTileMissing` by changing its signature to `Func<UserDashboardSettings, string, UserDashboardTile?>`. (Cleanest — no closure on handler-side normalization.)
- Acceptable alternative: have handlers pre-normalize `userId` themselves and pass the normalized value both into `MutateAsync` *and* into the closure. The mutator still re-normalizes (defense in depth). This duplicates the fallback, which is the smell the refactor exists to remove — prefer the first option.

This is a small but real ambiguity in the spec; flagged in **Specification Amendments**.

### Data Flow

**Enable, tile exists (happy path):**
```
Controller → EnableTileHandler.Handle
           → IUserDashboardSettingsMutator.MutateAsync("user123", "tile1", onFound, onMissing, ct)
              → _mediator.Send(GetUserSettingsRequest{UserId="user123"}, ct)   [provisioning]
              → _lock.AcquireAsync("user123", ct)                              [serialize writes]
              → _repository.GetByUserIdAsync("user123")                        [load aggregate]
              → settings.Tiles.FirstOrDefault(tileId="tile1") → existingTile
              → onFound(settings, existingTile) → existingTile.IsVisible = true
              → existingTile.LastModified = now; settings.LastModified = now
              → _repository.UpdateAsync(settings)
              → return MutationResult(SettingsLoaded:true, TileFound:true, TileAppended:false)
           → return new EnableTileResponse()
```

**Disable, tile missing (no-op):**
```
Controller → DisableTileHandler.Handle
           → IUserDashboardSettingsMutator.MutateAsync("user123", "ghost", onFound, onMissing:null, ct)
              → _mediator.Send(GetUserSettingsRequest, ct)
              → _lock.AcquireAsync("user123", ct)
              → _repository.GetByUserIdAsync("user123")
              → settings.Tiles.FirstOrDefault(tileId="ghost") → null
              → onMissing == null → skip
              → No UpdateAsync call
              → return MutationResult(SettingsLoaded:true, TileFound:false, TileAppended:false)
           → return new DisableTileResponse()
```

**Validation failure (TileId missing):**
Handler short-circuits *before* invoking the mutator — no mediator call, no lock acquisition, no repo read. This is locked down by `Handle_WhenTileIdIsNull_ShouldReturnFailure` / `…IsEmpty…` and must remain so.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Delegate closure inside `onTileMissing` needs resolved `userId`; current spec signature doesn't expose it cleanly | Medium | Pass `ResolvedUserId` into `onTileMissing` (see Spec Amendment #1). Reviewer must reject any implementation that re-normalizes `userId` in the handler. |
| Mutator forgets to stamp `tile.LastModified` for an appended tile, or stamps it twice (once in delegate, once in mutator) | Medium | Single source of truth: mutator stamps both timestamps; XML doc on `onTileFound` / `onTileMissing` explicitly forbids delegates from setting `LastModified`. Verified by existing `Handle_WhenTileDoesNotExist_ShouldAddNewTile` assertion (`newTile.LastModified.Should().Be(FixedUtcNow)`). |
| Two `TimeProvider.GetUtcNow()` reads return different values, breaking `disabledTile.LastModified == settings.LastModified` assertion in `DisableTileHandlerTests` | Low | Read once per mutation into a local `var now`. Pure in-process read so the bug is unlikely, but tests pin equality. |
| Future "move tile" / "pin tile" handler doesn't fit the `(Action, Func)` delegate shape | Low | Two call sites today, both fit. If a third shape lands and warrants extension, evolve the contract then — don't speculate now (YAGNI). |
| Test refactor accidentally weakens an assertion when fixtures are rewired | Medium | Spec mandates "preferred option" (handler→real-mutator→mocks). Reviewer checks that every `[Fact]` body is byte-identical to today; only the `ctor` block changes. |
| `SaveUserSettingsHandler` still carries the same scaffold post-refactor | Low | Explicitly out of scope. Acknowledge in PR description so a future change can absorb it via the same mutator (loop-style: `foreach (tileDto) Mutate(…)`). Don't expand scope in this PR. |
| Mutator's `Scoped` registration conflicts with the singleton `IUserDashboardSettingsLock` | None | DI tolerates a scoped service depending on a singleton — the documented and correct direction. |

## Specification Amendments

1. **Expose the resolved `userId` to `onTileMissing`.** The Enable-when-missing branch needs the normalized `userId` to populate `UserDashboardTile.UserId`. Two options; recommend changing the delegate signature to:
   ```csharp
   Func<UserDashboardSettings, string, UserDashboardTile?>? onTileMissing
   ```
   where the `string` is the resolved (post-`"anonymous"`-fallback) user id. This keeps `userId` normalization a single-source responsibility of the mutator. Update FR-1 surface accordingly. Existing tests don't need to change — they pass `userId = "user123"` non-null in the missing-tile path.

2. **Strengthen FR-1 invariants.** Add explicit XML doc / acceptance bullets:
   - The mutator MUST read `_timeProvider.GetUtcNow()` exactly once per `MutateAsync` invocation and reuse the value.
   - `onTileFound` MUST NOT mutate `tile.LastModified`.
   - The `UserDashboardTile` returned by `onTileMissing` MUST NOT set `LastModified`; the mutator stamps it.
   These are implied by the FR-1 prose but not stated as acceptance criteria.

3. **Clarify `userId` parameter type.** FR-1's signature reads `string userId`, but handlers pass `request.UserId` which is `string?`. Either declare the parameter `string? userId` (recommended, since the normalization-fallback is the mutator's job) or document that handlers must not pass null. Recommend the former.

4. **Make the mutator interface XML-document the deadlock invariant.** Copy the `<remarks>` warning from `IUserDashboardSettingsLock` ("non-reentrant — never call from inside the lock") so the pre-lock `GetUserSettingsRequest` ordering is self-documenting. Mechanically catches the issue if a future contributor adds the call inside the lock by mistake.

5. **Decide test scope explicitly.** FR-6 picks the "preferred" option (real mutator + mocked infrastructure). Add an explicit acceptance criterion: *no new `UserDashboardSettingsMutatorTests.cs` test class is added in this PR* — coverage is achieved transitively. (Or, if reviewers prefer dedicated mutator tests, state that and accept the duplication. Don't leave it ambiguous.)

6. **Note `SaveUserSettingsHandler` as a follow-up candidate.** The spec correctly puts it out of scope. Add a one-line `Out of Scope` bullet that *names* this as a known future opportunity using the same mutator (with a loop-shaped invocation), so it's not silently forgotten.

## Prerequisites

None. The refactor is self-contained:

- No new packages.
- No new DI lifetimes other than the one `AddScoped` registration.
- No database schema, migration, configuration, or Key Vault entry.
- No new infrastructure (Hangfire job, queue, secret, feature flag).
- No frontend regeneration (`npm run build` produces a byte-identical generated client per FR-5).

Implementation can start immediately against the current branch state.
```