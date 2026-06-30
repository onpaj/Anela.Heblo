# Task Plan: Encapsulate ManufactureOrder State Transition Rules in the Domain Aggregate (feat-3116)

Backend-only, behavior-preserving DDD refactor (.NET 8). It moves the legal state-transition
matrix out of `UpdateManufactureOrderStatusHandler` and onto the `ManufactureOrder` aggregate as a
pure predicate (`CanTransitionTo`) plus a guarded mutator (`ChangeState`), tightens the `State`
setter so external code can no longer assign arbitrary states, and redirects the existing transition
tests to exercise the real entity. One cohesive task.

---

### task: encapsulate-state-transition

**Goal**

Relocate the `ManufactureOrder` legal state-transition rules from the Application handler into the
Domain aggregate, with no change to runtime behavior or the HTTP contract:

1. Add `public bool CanTransitionTo(ManufactureOrderState newState)` to `ManufactureOrder` —
   a pure predicate reproducing the current handler matrix exactly.
2. Add `public void ChangeState(ManufactureOrderState newState, DateTime changedAtUtc, string changedByUser)`
   — a guarded mutator that throws `ValidationException` on an illegal transition and otherwise sets
   `State`, `StateChangedAt`, `StateChangedByUser` atomically.
3. Add a sanctioned initial-state construction path so creation does not depend on a public `State`
   setter, then reduce the `State` setter visibility (target `internal set`) so Application/Test
   assembly code can no longer do `order.State = x`.
4. Rewire `UpdateManufactureOrderStatusHandler` to pre-check with `CanTransitionTo` and mutate via
   `ChangeState`; delete its private `IsValidStateTransition`.
5. Redirect `ManufactureOrderStateTransitionTests` to test the real entity methods; adapt the
   handful of test fixtures that the setter change touches.

**Context** (self-contained — you only read this section)

Today the rule lives in the Application layer and `State` is an unguarded public setter, which
violates the project rule "business logic belongs in the domain / don't create anemic domain
models." The canonical precedent in this codebase is `TransportBox`
(`backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBox.cs`): `State` has
`private set`, mutation goes through a guarded `ChangeState(...)` → `CheckState(...)` that throws
`System.ComponentModel.DataAnnotations.ValidationException` on an illegal transition. Align with that
pattern but do NOT add a state-log collection (out of scope — the order already records
`StateChangedAt`/`StateChangedByUser`).

Current entity (`backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureOrder.cs`):

```csharp
// State management
public ManufactureOrderState State { get; set; }
public DateTime StateChangedAt { get; set; }
public string StateChangedByUser { get; set; } = null!;
```

Enum `ManufactureOrderState`: `Draft = 1, Planned = 2, SemiProductManufactured = 4, Completed = 5,
Cancelled = 6`. Enum `ManufactureType`: `MultiPhase = 0` (default), `SinglePhase = 1`.

**Authoritative transition matrix** — reproduce verbatim from the live handler
(`UpdateManufactureOrderStatusHandler.IsValidStateTransition`, lines 165–173). It is
**type-agnostic** (does NOT depend on `ManufactureType`):

| From | Allowed To |
|------|-----------|
| `Draft` | `Planned`, `Cancelled` |
| `Planned` | `Draft`, `SemiProductManufactured`, `Cancelled`, `Completed` |
| `SemiProductManufactured` | `Planned`, `Completed`, `Cancelled` |
| `Completed` | `SemiProductManufactured`, `Cancelled`, `Planned` |
| `Cancelled` | (none) |
| any other | (none) |

Every arm omits its own state, so all self-transitions (e.g. `Planned → Planned`) return `false`.
Preserve that exactly.

CRITICAL — do NOT introduce the type-aware rules. The existing test file
`ManufactureOrderStateTransitionTests.cs` contains private helpers
(`ValidateSinglePhaseTransition`, `ValidateMultiPhaseTransition`, and a 3-arg
`IsValidStateTransition(current, target, manufactureType)`) that "mirror the private methods in
`ManufactureOrderApplicationService`" — a service that no longer drives the live path. These never
executed production code. Adopting them would silently change behavior (e.g. start rejecting
`Planned → SemiProductManufactured` for single-phase). DELETE these three helpers and their
`[Theory]` test methods; replace with tests against the real `CanTransitionTo`. Do NOT keep them even
annotated.

Current handler control flow to preserve (`UpdateManufactureOrderStatusHandler.Handle`):
- Load order; if `null` → return `ErrorCodes.ResourceNotFound` with `{ id }`.
- Capture `var oldState = order.State;` BEFORE mutation (used in both error and success responses).
- If transition illegal → return `ErrorCodes.InvalidOperation` with
  `{ oldState, newState }` (string-ified). This early-return error shape MUST be preserved — it must
  NOT come from a thrown exception (which would fall into the `catch` and wrongly produce
  `InternalServerError`).
- On success: set state + audit fields, then run all existing side effects unchanged
  (`ManualActionRequired`, ERP order numbers, weight, Flexi doc codes + dates, `Note`, conditions
  reading at `SemiProductManufactured`/`Completed`, inventory write-down at `Completed`), call
  `_repository.UpdateOrderAsync`, and return `{ OldState, NewState, StateChangedAt, StateChangedByUser }`.

Setter-visibility constraint (IMPORTANT): `State` is assigned via object initializers in production
and in many test fixtures, so the setter must NOT be tightened in a way that breaks those
initializers without also giving them a working alternative. Confirmed seed/assignment sites:
- Production: `CreateManufactureOrderHandler` (line ~55) — `State = ManufactureOrderState.Draft`
  inside `new ManufactureOrder { ... }`.
- Production mutator: `UpdateManufactureOrderStatusHandler` (line 69) — `order.State = request.NewState;`
- Tests (9 files under `backend/test/Anela.Heblo.Tests/Features/Manufacture/`):
  `UpdateManufactureOrderHandlerTests`, `UpdateManufactureOrderScheduleHandlerTests` (post-construction
  `existingOrder.State = ...`), `UpdateManufactureOrderStatusHandlerConditionsTests`,
  `UpdateManufactureOrderStatusHandlerTests`, `GetManufactureOrderHandlerTests`,
  `GetManufactureOrdersHandlerTests`, `GetManufactureProtocolHandlerTests`,
  `DuplicateManufactureOrderHandlerTests`, `CreateManufactureOrderHandlerTests`.

The arch review's ideal is `private set` + a `CreateDraft`/`InitializeState` factory + a shared test
builder. The design doc marks setter tightening "optional / do not break initializers." To satisfy
FR-4 (forbid `order.State = x` from Application & Test assemblies) while keeping the change small and
the persistence path safe, the **chosen target is `internal set`** (the spec's documented fallback,
Assumption B). With `internal set`:
- Domain-assembly code (the new initial-state factory) can still assign `State`.
- EF Core materializes through the property accessor by convention (no config class exists —
  `ManufactureOrders` is a plain `DbSet` on `ApplicationDbContext`; `TransportBox` uses the same
  convention with a non-public setter and works).
- Application/Test assembly object-initializers that did `State = ...` will no longer compile and
  MUST be redirected to the new construction path / `ChangeState` (this is the bulk of the
  mechanical work). `InternalsVisibleTo` is NOT used for the test assembly here, so test fixtures use
  the public construction path, not the setter.

No DB migration. No DI/module changes. No DTO/error-code changes. No new files.

**Files to create/modify**

- Modify: `backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureOrder.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrderStatus/UpdateManufactureOrderStatusHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CreateManufactureOrder/CreateManufactureOrderHandler.cs`
- Modify (redirect/extend, do not create new file): `backend/test/Anela.Heblo.Tests/Features/Manufacture/ManufactureOrderStateTransitionTests.cs`
- Modify only as forced by the setter change (arrange-phase fixtures): the test files listed above
  under `backend/test/Anela.Heblo.Tests/Features/Manufacture/`.

**Implementation steps**

1. **Entity — `ManufactureOrder.cs`.** Replace the `State management` block. Tighten the `State`
   setter to `internal set`, add a sanctioned initial-state factory, `CanTransitionTo`, and
   `ChangeState`. Add `using System.ComponentModel.DataAnnotations;` at the top of the file.

   ```csharp
   // State management
   public ManufactureOrderState State { get; internal set; }
   public DateTime StateChangedAt { get; internal set; }
   public string StateChangedByUser { get; internal set; } = null!;
   ```

   Add these members to the class body:

   ```csharp
   /// <summary>
   /// Sanctioned construction path that seeds the initial state and audit fields without
   /// requiring an externally visible State setter. Use at creation time only.
   /// </summary>
   public void InitializeState(ManufactureOrderState initialState, DateTime changedAtUtc, string changedByUser)
   {
       State = initialState;
       StateChangedAt = changedAtUtc;
       StateChangedByUser = changedByUser;
   }

   /// <summary>
   /// Pure predicate: true iff a transition from the current State to <paramref name="newState"/>
   /// is legal. Reads State and the argument only; mutates nothing.
   /// </summary>
   public bool CanTransitionTo(ManufactureOrderState newState) => State switch
   {
       ManufactureOrderState.Draft => newState is ManufactureOrderState.Planned or ManufactureOrderState.Cancelled,
       ManufactureOrderState.Planned => newState is ManufactureOrderState.Draft or ManufactureOrderState.SemiProductManufactured or ManufactureOrderState.Cancelled or ManufactureOrderState.Completed,
       ManufactureOrderState.SemiProductManufactured => newState is ManufactureOrderState.Planned or ManufactureOrderState.Completed or ManufactureOrderState.Cancelled,
       ManufactureOrderState.Completed => newState is ManufactureOrderState.SemiProductManufactured or ManufactureOrderState.Cancelled or ManufactureOrderState.Planned,
       ManufactureOrderState.Cancelled => false,
       _ => false
   };

   /// <summary>
   /// Guarded state mutation. Throws when the transition is illegal and leaves the entity
   /// unchanged; on success sets State + audit fields atomically.
   /// </summary>
   public void ChangeState(ManufactureOrderState newState, DateTime changedAtUtc, string changedByUser)
   {
       if (!CanTransitionTo(newState))
       {
           throw new ValidationException($"Unable to change state from {State} to {newState}.");
       }

       State = newState;
       StateChangedAt = changedAtUtc;
       StateChangedByUser = changedByUser;
   }
   ```

   Validate-before-mutate ordering matters: nothing is assigned when the guard fails.

2. **Production creation — `CreateManufactureOrderHandler.cs`.** Remove `State`, `StateChangedAt`,
   `StateChangedByUser` from the `new ManufactureOrder { ... }` initializer (lines ~55–57) and seed
   them via the sanctioned path immediately after construction. Concretely, change:

   ```csharp
       ManufactureType = request.ManufactureType,
       State = ManufactureOrderState.Draft,
       StateChangedAt = now.DateTime,
       StateChangedByUser = currentUser.Name
   };
   ```
   to:
   ```csharp
       ManufactureType = request.ManufactureType
   };
   order.InitializeState(ManufactureOrderState.Draft, now.DateTime, currentUser.Name);
   ```
   (Adjust the local variable name to whatever the `new ManufactureOrder` is assigned to; verify
   `now` / `currentUser.Name` are the exact symbols already in scope — they are the current
   initializer values, so reuse them verbatim.)

3. **Handler rewire — `UpdateManufactureOrderStatusHandler.cs`.**
   - Keep `var oldState = order.State;` as-is.
   - Replace the guard call (line 56) `if (!IsValidStateTransition(oldState, request.NewState))` with
     `if (!order.CanTransitionTo(request.NewState))`. Keep the identical `InvalidOperation` early
     return with `{ oldState, newState }`.
   - Replace the three direct assignments (lines 69–71):
     ```csharp
     order.State = request.NewState;
     order.StateChangedAt = _timeProvider.GetUtcNow().DateTime;
     order.StateChangedByUser = currentUserName;
     ```
     with a single guarded call (keep `currentUserName` resolved on the line above, as today, since
     it is reused by the note creation and inventory write-down):
     ```csharp
     order.ChangeState(request.NewState, _timeProvider.GetUtcNow().DateTime, currentUserName);
     ```
   - Delete the entire private `IsValidStateTransition(ManufactureOrderState, ManufactureOrderState)`
     method (lines 162–174).
   - Leave everything else in `Handle` (side effects, `CaptureConditionsReadingAsync`,
     `WriteDownInventoryAsync`) byte-for-byte unchanged. The `CanTransitionTo` pre-check guarantees
     `ChangeState` never throws on the live path, so the `try/catch` still only catches genuine
     unexpected failures.

4. **Redirect transition tests — `ManufactureOrderStateTransitionTests.cs`.** Delete the three
   `[Theory]` methods (`ValidateSinglePhaseTransition_*`, `ValidateMultiPhaseTransition_*`,
   `IsValidStateTransition_ShouldRespectManufactureType`) AND the three private mirror helpers.
   Replace with tests that drive the real entity (see "Tests to write"). To set the entity's "from"
   state in arrange, construct the order and seed via `InitializeState`, then (where a non-initial
   "from" state is needed) reach it with a legal `ChangeState` chain, OR — since these tests only
   assert `CanTransitionTo` which reads `State` — add a tiny local builder that uses
   `InitializeState` to place the order directly into the desired "from" state (legal because
   `InitializeState` is unguarded and represents seeding). Prefer the `InitializeState`-based builder
   for the `CanTransitionTo` theory to keep the matrix exhaustive without long chains:

   ```csharp
   private static ManufactureOrder OrderInState(ManufactureOrderState state)
   {
       var order = new ManufactureOrder();
       order.InitializeState(state, DateTime.UtcNow, "test");
       return order;
   }
   ```

5. **Fix arrange-phase fixtures broken by `internal set`.** Build the test project; every remaining
   compile error is a test fixture assigning `State`/`StateChangedAt`/`StateChangedByUser` in an
   initializer or post-construction. For each, replace the `State = X` initializer entry (and any
   companion `StateChangedAt`/`StateChangedByUser` initializer entries) with a post-construction
   `order.InitializeState(X, <existing-at-or-DateTime.UtcNow>, <existing-by-or-"test">)`. For
   `UpdateManufactureOrderScheduleHandlerTests` post-construction `existingOrder.State = ...` (around
   lines 85/107), replace with `existingOrder.InitializeState(...)` (these are seeds, not transitions).
   To minimize churn across the 9 files, you MAY add one shared internal test helper (e.g. a static
   `ManufactureOrderTestBuilder.InState(...)` in the Manufacture test folder) and use it where it
   reduces edits — but a direct `InitializeState` call per site is equally acceptable. Do NOT change
   any test's expected assertions or expected outcomes.

**Tests to write** (in `ManufactureOrderStateTransitionTests.cs`, replacing the deleted theories)

1. `CanTransitionTo_ReturnsExpected` — `[Theory]`/`[InlineData]` covering EVERY cell of the matrix,
   including the always-`false` `Cancelled` row and all self-transitions. Minimum data set (from,
   to, expected):
   - Draft→Planned = true; Draft→Cancelled = true; Draft→SemiProductManufactured = false;
     Draft→Completed = false; Draft→Draft = false.
   - Planned→Draft = true; Planned→SemiProductManufactured = true; Planned→Cancelled = true;
     Planned→Completed = true; Planned→Planned = false.
   - SemiProductManufactured→Planned = true; SemiProductManufactured→Completed = true;
     SemiProductManufactured→Cancelled = true; SemiProductManufactured→Draft = false;
     SemiProductManufactured→SemiProductManufactured = false.
   - Completed→SemiProductManufactured = true; Completed→Cancelled = true; Completed→Planned = true;
     Completed→Draft = false; Completed→Completed = false.
   - Cancelled→Draft = false; Cancelled→Planned = false; Cancelled→SemiProductManufactured = false;
     Cancelled→Completed = false; Cancelled→Cancelled = false.
   Arrange via `OrderInState(from)`, act `order.CanTransitionTo(to)`, assert equals expected.

2. `ChangeState_OnIllegalTransition_ThrowsAndLeavesEntityUnchanged` — arrange `OrderInState(Cancelled)`
   with known `StateChangedAt`/`StateChangedByUser`; assert `Assert.Throws<ValidationException>(() =>
   order.ChangeState(ManufactureOrderState.Planned, DateTime.UtcNow, "user"))`; then assert `State`,
   `StateChangedAt`, and `StateChangedByUser` are all unchanged from the arranged values.

3. `ChangeState_OnLegalTransition_UpdatesAllThreeFields` — arrange `OrderInState(Draft)`; call
   `order.ChangeState(ManufactureOrderState.Planned, someUtc, "alice")`; assert
   `State == Planned`, `StateChangedAt == someUtc`, `StateChangedByUser == "alice"`.

Note: the handler-level tests (`UpdateManufactureOrderStatusHandlerTests`,
`UpdateManufactureOrderStatusHandlerConditionsTests`) keep their existing expected outcomes — an
illegal transition must still assert `ErrorCodes.InvalidOperation` (NOT a thrown exception /
`InternalServerError`). Only their arrange-phase `State = ...` seeding may need the `InitializeState`
redirect; expectations are unchanged.

**Acceptance criteria**

- `CanTransitionTo` is `public`, pure (reads `State` + arg only, mutates nothing), and returns the
  exact same boolean as the old `IsValidStateTransition(from, to)` for every (from, to) pair,
  including `false` for all self-transitions and all `Cancelled` rows.
- `ChangeState` throws `ValidationException` on an illegal transition and leaves `State`,
  `StateChangedAt`, `StateChangedByUser` unmodified; on a legal transition it sets all three.
- `UpdateManufactureOrderStatusHandler` no longer declares `IsValidStateTransition`; it pre-checks
  with `order.CanTransitionTo(...)` (same `InvalidOperation { oldState, newState }` early return) and
  mutates via `order.ChangeState(...)`; `oldState` is still captured before mutation; all other
  side effects and the success/error response shapes are unchanged.
- The `State` setter is `internal set` (or, if EF materialization unexpectedly fails, fall back to
  the least-permissive level that compiles and passes the persistence tests, with `ChangeState` /
  `InitializeState` documented as the sanctioned mutation paths). `order.State = x` no longer
  compiles from the Application or Test assemblies.
- `CreateManufactureOrderHandler` seeds the initial `Draft` state via `InitializeState`; no behavior
  change to the create flow.
- `ManufactureOrderStateTransitionTests` no longer contains the three private mirror helpers or the
  type-aware theories; it tests `CanTransitionTo`/`ChangeState` against the real entity.
- All touched fixtures compile by routing through `InitializeState` (or the shared test builder);
  no test's expected outcome is changed.
- `cd backend && dotnet build` succeeds.
- `cd backend && dotnet format --verify-no-changes` reports no changes (run `dotnet format` to fix
  if needed).
- Touched tests pass: `dotnet test backend/test/Anela.Heblo.Tests` filtered to at minimum
  `ManufactureOrderStateTransitionTests`, `UpdateManufactureOrderStatusHandlerTests`,
  `UpdateManufactureOrderStatusHandlerConditionsTests`, `CreateManufactureOrderHandlerTests`,
  `UpdateManufactureOrderScheduleHandlerTests`, and `ManufactureOrderRepositoryTests` (verifies EF
  materialization still binds `State` after the visibility change). All green.
