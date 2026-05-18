```markdown
# Architecture Review: Runtime Hot-Reload for Marketing `PushEnabled` Kill-Switch

## Skip Design: true

Backend-only DI-mechanism change. No UI surface, no new visual components, no design decisions required.

## Architectural Fit Assessment

The change aligns cleanly with the existing Marketing module conventions:

- `MarketingCategoryMapper` already consumes `IOptionsMonitor<MarketingCalendarOptions>` (file: `backend/src/Anela.Heblo.Application/Features/Marketing/Services/MarketingCategoryMapper.cs:17`), establishing the hot-reload pattern as **intentional module policy**, not an isolated outlier.
- `MarketingModule.AddMarketingModule` registers options via `services.AddOptions<MarketingCalendarOptions>().Bind(...).Validate(...).ValidateOnStart()` (file: `backend/src/Anela.Heblo.Application/Features/Marketing/MarketingModule.cs:17-27`). This registration makes both `IOptions<T>` and `IOptionsMonitor<T>` available automatically — **no DI changes required**.
- The global `csharp-patterns.md` Options Pattern guidance favors strongly-typed options; switching the access pattern from `Value` to `CurrentValue` is a localized refinement, not a structural change.

**Integration points:** two MediatR command handlers in the Application layer, plus the test doubles that construct them. No persistence, no API contract, no module boundaries crossed.

**Critical scope gap found:** `DeleteMarketingActionHandler` (file: `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/DeleteMarketingAction/DeleteMarketingActionHandler.cs:20,54`) **also** reads `_options.Value.PushEnabled` to gate an outbound Graph `DeleteEventAsync` call. The brief and spec list only Create and Update. Leaving Delete on a startup snapshot defeats the stated operational goal ("operators can disable outbound Graph traffic immediately during an incident"): an operator flipping `PushEnabled = false` would still see DELETE calls to Graph as long as users perform soft-deletes. This is the same class of write handler making the same outbound call — it must be in scope, or the rationale for excluding it must be explicitly documented.

## Proposed Architecture

### Component Overview

```
        MediatR pipeline
              │
   ┌──────────┴──────────┬──────────────────────┐
   ▼                     ▼                      ▼
CreateMarketingAction UpdateMarketingAction DeleteMarketingAction
   Handler              Handler              Handler
   │ │                    │ │                    │ │
   │ └─ IOptionsMonitor<MarketingCalendarOptions> ─┘ │       ← change: was IOptions<T>
   │                                                  │
   └────────── IOutlookCalendarSync ─────────────────┘
                       │
                       ▼
               Microsoft Graph API
                       ▲
                       │
       (separate path — already correct)
                       │
              MarketingCategoryMapper
                       │
                       └─ IOptionsMonitor<MarketingCalendarOptions>
                          + OnChange subscription + Snapshot fallback

Configuration source (appsettings + Azure App Config)
   └─ services.AddOptions<MarketingCalendarOptions>().Bind(...).ValidateOnStart()
      └─ provides both IOptions<T> and IOptionsMonitor<T> to DI
```

### Key Design Decisions

#### Decision 1: Direct `CurrentValue` read vs. subscription with cached projection
**Options considered:**
- A. Read `_options.CurrentValue.PushEnabled` directly at the check site.
- B. Mirror `MarketingCategoryMapper`: subscribe via `OnChange`, cache a `volatile bool` field, fall back on failure.

**Chosen approach:** A — direct `CurrentValue` read.

**Rationale:** The mapper subscribes because it builds a derived structure (case-insensitive dictionaries) whose **construction can fail** on malformed input; a snapshot-on-failure strategy is meaningful there. `PushEnabled` is a primitive boolean — there is no projection to cache, no construction to fail, and no measurable read cost (`CurrentValue` is a cached field updated by change tokens). Adding a subscription would import the mapper's complexity without any benefit and re-introduce a separate cached state that could drift. FR-3 explicitly endorses this asymmetry.

#### Decision 2: Treat `IOptionsMonitor<T>` as available-by-default; no DI changes
**Options considered:**
- A. Rely on existing `AddOptions<T>().Bind(...)` registration; `IOptionsMonitor<T>` is automatically resolvable.
- B. Add explicit registration in `MarketingModule`.

**Chosen approach:** A.

**Rationale:** The .NET options abstraction guarantees `IOptionsMonitor<T>` whenever `Configure`/`Bind` has been called on the same type. The Marketing module already uses this for the mapper — confirmed in `MarketingModule.cs:17-27`. No new registration is needed and adding one would be misleading.

#### Decision 3: Do **not** add defensive `try/catch` around `CurrentValue`
**Options considered:**
- A. Read `CurrentValue.PushEnabled` directly; let any (improbable) exception bubble up.
- B. Wrap in `try/catch` and "fail closed" (treat as `false`) per NFR-4.

**Chosen approach:** A — no defensive wrapping.

**Rationale:** `CurrentValue` returns a cached, already-bound value; in practice it does not throw for a successfully-bound options class. Startup binding/validation is already enforced by `.ValidateOnStart()` (`MarketingModule.cs:27`) — that is where binding failure must surface, loudly, at the correct layer. Wrapping the read in `try/catch` would (1) hide the *next* class of bugs (e.g., a future `IValidateOptions` rejection during reload) behind a silent kill-switch, and (2) introduce a second failure path with no symmetry to the mapper's design (which keeps the prior snapshot — equivalent to **fail-open** for the prior value, not fail-closed). The spec's NFR-4 should be reworded; see Specification Amendments.

#### Decision 4: Promote `TestOptionsMonitor<T>` to a shared test helper
**Options considered:**
- A. Re-implement `TestOptionsMonitor<T>` inline in each handler test file.
- B. Lift the existing private class from `MarketingCategoryMapperTests.cs:20-68` to a shared test-utility namespace.

**Chosen approach:** B.

**Rationale:** Three test files (`Application/Marketing/CreateMarketingActionHandlerTests.cs`, `Application/Marketing/UpdateMarketingActionHandlerTests.cs`, and the older duplicate `Features/Marketing/CreateMarketingActionHandlerTests.cs` plus `Features/Marketing/MarketingActionHandlerSyncTests.cs`) will need to construct an `IOptionsMonitor<MarketingCalendarOptions>` with mutation support. DRY (per global `coding-style.md`) and the spec's FR-5 acceptance criterion ("standard test-double approach … or introduce a minimal helper") favor extraction.

## Implementation Guidance

### Directory / Module Structure

**Modified source files (handlers — three, not two):**
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/DeleteMarketingAction/DeleteMarketingActionHandler.cs` *(new — see Specification Amendments)*

**New test helper:**
- `backend/test/Anela.Heblo.Tests/TestHelpers/TestOptionsMonitor.cs` — lifted verbatim from `MarketingCategoryMapperTests.TestOptionsMonitor<T>`, made `public` (or `internal` with `InternalsVisibleTo`-free positioning under the test project).

**Modified test files:**
- `backend/test/Anela.Heblo.Tests/Application/Marketing/CreateMarketingActionHandlerTests.cs` — replace `Options.Create(...)` (line 49) with `new TestOptionsMonitor<MarketingCalendarOptions>(...)`; add the two new hot-reload tests required by FR-5.
- `backend/test/Anela.Heblo.Tests/Application/Marketing/UpdateMarketingActionHandlerTests.cs` — same substitution at line 69; add FR-5 tests.
- `backend/test/Anela.Heblo.Tests/Features/Marketing/CreateMarketingActionHandlerTests.cs` — older duplicate, replace `Mock<IOptions<MarketingCalendarOptions>>` (line 35). **Flag to maintainer:** this duplicate test class exists alongside the canonical one in `Application/Marketing/`; mention but don't delete (per project rule: don't touch unrelated code, surface dead/duplicate code instead).
- `backend/test/Anela.Heblo.Tests/Features/Marketing/MarketingActionHandlerSyncTests.cs` — replace `Mock<IOptions<MarketingCalendarOptions>>` at lines 54, 68, 82.

**Not modified:**
- `MarketingCalendarOptions.cs` — no fields added or changed.
- `MarketingModule.cs` — no DI changes.
- `OutlookCalendarSyncService.cs` — out of scope per spec; its `_options.Value` snapshot of `GroupId` at construction (line 43) is acceptable because it is scoped per-request and `GroupId` is not the kill-switch.

### Interfaces and Contracts

No public contracts change. The MediatR request/response types for Create/Update/Delete are unchanged.

The only contract change is internal-to-DI: handler constructors swap one type parameter.

```csharp
// Before
private readonly IOptions<MarketingCalendarOptions> _options;

// After
private readonly IOptionsMonitor<MarketingCalendarOptions> _options;
```

```csharp
// Before
if (_options.Value.PushEnabled)

// After
if (_options.CurrentValue.PushEnabled)
```

Field name `_options` is retained — it matches the mapper's pattern of holding the monitor and reading via `.CurrentValue` at the use site.

### Data Flow

For the create path with hot-reload (representative; update/delete are symmetric):

```
1. Request arrives → MediatR dispatches → CreateMarketingActionHandler.Handle
2. Authentication check (unchanged)
3. Build MarketingAction (unchanged)
4. Read _options.CurrentValue.PushEnabled        ← was _options.Value.PushEnabled
   │
   ├─ true  → call IOutlookCalendarSync.CreateEventAsync
   │         (Graph API call; reflects the *current* config snapshot
   │          at handler invocation time, not startup)
   │
   └─ false → skip Graph call
5. Persist via repository (unchanged)
6. Return response (unchanged)
```

Runtime sequence when an operator flips the kill-switch:

```
T0: PushEnabled = true; handler call N performs Graph push.
T1: Operator changes Azure App Configuration: PushEnabled = false.
T2: Refresh interval elapses (configured by the Azure App Config provider,
    same wiring already used by MarketingCategoryMapper).
T3: IOptionsMonitor<MarketingCalendarOptions>.CurrentValue now reports
    PushEnabled = false (change-token cache updated).
T4: Handler call N+1 reads CurrentValue → skips Graph push. No restart.
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Spec excludes `DeleteMarketingActionHandler`, leaving the kill-switch incomplete: deletes continue to call Graph after operator flips the flag | **HIGH** | Amend spec to include Delete (preferred). If explicitly out of scope, document the rationale — but the brief's "kill-switch for Outlook calendar sync" framing makes exclusion hard to justify. |
| NFR-4's "fail closed if `IOptionsMonitor` throws" risks the implementer wrapping `CurrentValue` in `try/catch`, silently turning a binding/validation bug into a silent "push disabled" state | MEDIUM | Reword NFR-4 (see Specification Amendments). Rely on `.ValidateOnStart()` for boot-time validation; let any unexpected runtime exception propagate so it surfaces in logs as a real bug. |
| Two duplicate handler-test classes (`Application/Marketing/...` and `Features/Marketing/...`) — easy to update one and leave the other failing the build | MEDIUM | Update both. Flag the duplication in the PR description; do not silently delete the older file (not in scope). |
| `Options.Create(...)` calls in current tests return `IOptions<T>` and will break compile after the constructor change | LOW | Search-and-replace via the new `TestOptionsMonitor<T>` helper. Compile error surfaces immediately — low risk of silent regression. |
| `MarketingCalendarOptions.CategoryMappings` lacks a setter (it's `init`), so a `TestOptionsMonitor<T>.Set(new MarketingCalendarOptions { ... })` is required between toggles | LOW | The existing helper already supports `.Set(next)`; no extra work. |
| Performance regression from `CurrentValue` property access | NEGLIGIBLE | `CurrentValue` is a cached field read backed by `OptionsCache<T>`; equivalent cost to `IOptions<T>.Value`. NFR-1 already states this; no mitigation needed. |
| Hot-reload depends on whatever Azure App Configuration / change-token wiring already powers the mapper — if that wiring is broken globally, the kill-switch is broken too | LOW (existing risk) | Out of scope to fix; smoke-test by toggling a `CategoryMappings` value in a staging environment and confirming the mapper picks it up. If the mapper hot-reloads, this fix hot-reloads. |

## Specification Amendments

1. **Extend FR scope to include `DeleteMarketingActionHandler`.**
   Add an `FR-1b` (or extend FR-1/FR-2 to "all three write handlers") covering Delete. The acceptance criteria mirror FR-1 verbatim: constructor injects `IOptionsMonitor<MarketingCalendarOptions>`, the check at line 54 reads `_options.CurrentValue.PushEnabled`, runtime toggle is honored.
   If the implementer/owner decides to defer Delete, the spec must explicitly justify why the kill-switch is acceptable to be partial (e.g., delete frequency, idempotency-at-Graph). The brief's framing — "the only kill-switch for Outlook calendar sync" — argues against deferral.

2. **Rewrite NFR-4.**
   Current text says the handler "should fail closed — i.e., behave as if `PushEnabled = false` and skip the push — rather than throwing". Replace with:
   > `IOptionsMonitor<MarketingCalendarOptions>.CurrentValue` is a cached read and is not expected to throw under normal operation. No defensive wrapping is added at the call site. Configuration binding and validation errors are caught at application startup by `.ValidateOnStart()` in `MarketingModule`. Any unexpected runtime exception is allowed to propagate and surface in standard error logging.

3. **Extend FR-5 to cover Delete** (if amendment 1 is accepted): add a symmetric hot-reload test for `DeleteMarketingActionHandler` covering `true → false` and `false → true` transitions across two invocations on the same handler instance.

4. **Acknowledge the shared test helper.**
   Add a note to FR-5 that the existing `TestOptionsMonitor<T>` from `MarketingCategoryMapperTests` is to be promoted to a shared test-utility class under `backend/test/Anela.Heblo.Tests/TestHelpers/` and reused — not re-implemented per test file.

5. **Acknowledge duplicate test files.**
   Note that `Features/Marketing/CreateMarketingActionHandlerTests.cs` and `Features/Marketing/MarketingActionHandlerSyncTests.cs` (older duplicates of the canonical `Application/Marketing/` tests) must also be updated to compile against the new constructor signature. Do not delete them in this change; surface the duplication for separate cleanup.

## Prerequisites

None.

- `Microsoft.Extensions.Options` is already referenced (the mapper uses it).
- `IOptionsMonitor<MarketingCalendarOptions>` is already resolvable via the existing `AddOptions<MarketingCalendarOptions>().Bind(...)` registration in `MarketingModule.cs`.
- The Azure App Configuration / change-token plumbing is already in production for `MarketingCategoryMapper`; this work depends on it being functional but does not modify it.
- No database migration, no infrastructure change, no new package, no config-schema change.
```