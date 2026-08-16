# Design: Unify Transport Box Code Uniqueness Into a Single State Rule

Backend-only. Per `arch-review.r1.md` § Skip Design, there is no UI surface: no new or changed screens, components, or contracts, and the one user-visible consequence (an assignment that used to succeed now returns `TransportBoxDuplicateActiveBoxFound`) reuses an error code already mapped in `frontend/src/i18n.ts:155-156`. No UX/UI section follows.

This design implements the **amended** specification. Amendments A1, A2 and A3 from the architecture review are binding and are folded into the sections below; where the un-amended `spec.r1.md` differs, this document wins.

## Component Design

### 1. `TransportBoxStateRules` — new file (Domain)

`backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxStateRules.cs`
Namespace `Anela.Heblo.Domain.Features.Logistics.Transport`.

Sole owner of the answer to "does a box in this state still hold its code?". Follows the static-predicate convention already on the same aggregate (`TransportBox.IsInTransportPredicate` / `IsInReservePredicate` / `IsInQuarantinePredicate`, `TransportBox.cs:39-50`). `TransportBox.cs` already carries `using System.Linq.Expressions;`, so no new dependency is introduced in the project.

```csharp
using System.Linq.Expressions;

namespace Anela.Heblo.Domain.Features.Logistics.Transport;

/// <summary>
/// The single definition of transport-box code occupancy: whether a box in a given state
/// still holds its <see cref="TransportBox.Code"/> and therefore blocks that code from being
/// assigned to another box.
///
/// This rule must never be restated. Every consumer — today
/// <see cref="ITransportBoxRepository.IsBoxCodeActiveAsync"/>,
/// <see cref="ITransportBoxRepository.GetByCodeAsync"/> and OpenOrResumeBoxByCodeHandler —
/// calls into this type. Comparing against TransportBoxState.Closed/Stocked directly for
/// code-uniqueness purposes is a bug: that duplication is what allowed a Quarantine box's
/// code to be reassigned (issue #3887).
///
/// The rule is a deny-list on purpose. A newly added TransportBoxState occupies its code
/// until someone deliberately adds it to the releasing set, so the failure mode of
/// forgetting about this type is a false rejection, never a silent duplicate.
/// </summary>
public static class TransportBoxStateRules
{
    // Private: the array is an implementation detail, and `public static readonly T[]` is
    // only shallowly readonly — a public array would let any assembly overwrite an element
    // and silently reopen this bug. Kept as an array for consistency with the current
    // implementation and with memory/gotchas/postgres-partial-index-active-states.md's
    // `private static readonly int[] ActiveStates` precedent (NOT because EF Core 8
    // requires an array — List/HashSet/IEnumerable all translate; see amendment A5).
    private static readonly TransportBoxState[] CodeReleasingStates =
    {
        TransportBoxState.Closed,
        TransportBoxState.Stocked,
    };

    /// <summary>In-memory check, for handlers that already hold a state value.</summary>
    public static bool OccupiesCode(TransportBoxState state) =>
        !CodeReleasingStates.Contains(state);

    /// <summary>
    /// EF-composable form of <see cref="OccupiesCode"/>. Translates to a negated set
    /// membership over the HasConversion&lt;string&gt; "State" column.
    /// </summary>
    public static readonly Expression<Func<TransportBox, bool>> OccupiesCodePredicate =
        b => !CodeReleasingStates.Contains(b.State);
}
```

**Amendment A1 applied.** `CodeReleasingStates` is `private`, not public. The public surface is exactly two members — `OccupiesCode` for in-memory callers and `OccupiesCodePredicate` for EF composition — and both have a consumer on the day this merges. Declare `CodeReleasingStates` textually before `OccupiesCodePredicate`; the expression tree captures the field by member access rather than by value, so ordering is not strictly load-bearing, but relying on that is unnecessary subtlety.

`Anela.Heblo.Domain.csproj` carries `<InternalsVisibleTo Include="Anela.Heblo.Tests" />` (verified), so the drift-guard test *could* reach the array. It must not — see the test surface below.

### 2. `ITransportBoxRepository` — signature unchanged, XML doc added

`backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/ITransportBoxRepository.cs:19`

The name `IsBoxCodeActiveAsync` is kept (renaming would churn the interface plus four test files for no behavioural gain), but it now actively misleads — `Error` and `Quarantine` are not colloquially "active" yet do occupy the code. The doc is therefore load-bearing and goes on the **interface**, which has no documentation at all today and is what every caller sees, not only on the implementation.

```csharp
/// <summary>
/// True when any box currently occupies <paramref name="boxCode"/> — i.e. holds it in a
/// state for which <see cref="TransportBoxStateRules.OccupiesCode"/> is true. Matching is
/// case-insensitive. The name predates the rule; "active" here means "occupying the code",
/// which includes Error and Quarantine.
/// </summary>
Task<bool> IsBoxCodeActiveAsync(string boxCode);
```

No other member of `ITransportBoxRepository` changes. No consumer outside the Logistics slice is affected.

### 3. `TransportBoxRepository` — two methods rewritten

`backend/src/Anela.Heblo.Persistence/Logistics/TransportBoxes/TransportBoxRepository.cs`

**`IsBoxCodeActiveAsync` (currently 96-115).** Delete the local `activeStates` array entirely; compose the shared predicate instead. `boxCode.ToUpper()` normalisation, the `AnyAsync` single-round-trip shape, and the debug log line are unchanged.

```csharp
public async Task<bool> IsBoxCodeActiveAsync(string boxCode)
{
    var upperBoxCode = boxCode.ToUpper();
    var exists = await DbSet
        .Where(x => x.Code == upperBoxCode)
        .Where(TransportBoxStateRules.OccupiesCodePredicate)
        .AnyAsync();

    _logger.LogDebug("Checked if box code {BoxCode} is active: {IsActive}", boxCode, exists);

    return exists;
}
```

**`GetByCodeAsync` (currently 117-131).** Replace the `State == Closed ? 1 : 0` ordering with `OrderByDescending` over the same predicate. `OrderByDescending` binds `TKey = bool`; PostgreSQL sorts `false < true`, so `DESC` puts code-occupying boxes first — FR-5's intent without restating the rule. Note the restructure from `FirstOrDefaultAsync(predicate)` to `.Where(...).FirstOrDefaultAsync()`: the code filter must be composed **before** the ordering so the `ORDER BY` applies to the already-filtered single-code set. `Include(x => x.Items)`, `Include(x => x.StateLog)`, and the debug log are unchanged.

```csharp
public async Task<TransportBox?> GetByCodeAsync(string boxCode)
{
    var upperBoxCode = boxCode.ToUpper();
    var transportBox = await DbSet
        .Include(x => x.Items)
        .Include(x => x.StateLog)
        .Where(x => x.Code == upperBoxCode)
        .OrderByDescending(TransportBoxStateRules.OccupiesCodePredicate)  // occupying first
        .ThenByDescending(o => o.Id)
        .FirstOrDefaultAsync();

    _logger.LogDebug("Retrieved transport box by code {BoxCode}: {Found}",
        boxCode, transportBox != null);

    return transportBox;
}
```

**Explicitly untouched in this file** (per spec § Out of Scope, and amendment A7): `GetPagedListAsync`'s `isActiveFilter` (`x.State != TransportBoxState.Closed`, line 39) is a UI list filter that deliberately shows `Stocked` boxes, and `GetStateSummaryAsync` / `GetReceivedBoxesAsync` reason about different concepts. Only the two methods above may change.

Post-change invariant, checkable by grep: the tokens `TransportBoxState.Closed` and `TransportBoxState.Stocked` must not appear in `IsBoxCodeActiveAsync` or `GetByCodeAsync`. They legitimately remain at line 39 (`isActiveFilter`).

### 4. `OpenOrResumeBoxByCodeHandler` — one predicate, one comment

`backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/OpenOrResumeBoxByCode/OpenOrResumeBoxByCodeHandler.cs`

Line 62, the inline deny-list, becomes a call into the rule. The three-branch structure is preserved verbatim: (1) `State == Opened` → resume, (2) code occupied → `TransportBoxDuplicateActiveBoxFound` with `code` and `state` params, (3) otherwise → create and open a fresh box. The `code` normalisation at line 44 and every `catch` block are unchanged.

```csharp
// A box with this code is busy in a non-resumable state.
if (existing != null && TransportBoxStateRules.OccupiesCode(existing.State))
{
    return new OpenOrResumeBoxByCodeResponse(ErrorCodes.TransportBoxDuplicateActiveBoxFound,
        new Dictionary<string, string> { { "code", code }, { "state", existing.State.ToString() } });
}
```

The comment at line 69 — *"GetByCodeAsync returns any active box first, so reaching here means none exists"* — is **false today** and becomes true only because of the `GetByCodeAsync` change above. Amendment A4: restate it so it names its source of truth.

```csharp
// No box, or only a Closed/Stocked box with this code — create and open a fresh one.
// GetByCodeAsync orders on TransportBoxStateRules.OccupiesCodePredicate, so any
// code-occupying box outranks a released one; reaching here means none exists.
```

### 5. `ChangeTransportBoxStateHandler` — unchanged

`backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs`

`HandleNewToOpened` (214-248) needs no edit. It already calls `IsBoxCodeActiveAsync` (line 228) and returns `ErrorCodes.TransportBoxDuplicateActiveBoxFound` with `Params["code"]` (231-236); it inherits the fix. The `Stocked`-cleanup that follows (240-245, `GetPagedListAsync(state: Stocked)` then `Close`) stays exactly as it is, including its `Code.Contains` match — noted as an inconsistency in the spec's Out of Scope, not fixed here.

`New` joining the code-occupying set is verified harmless (amendment A6): `AssignBoxCodeIfAny` (`TransportBox.cs`, the only writer of `Code` on a `New` box) is called once, at `ChangeTransportBoxStateHandler.cs:71`, and is always followed in the same unit of work by `Open(...)` which moves the box to `Opened`; `Reset(...)` nulls `Code` on the `Opened → New` return. No path persists a `New` box carrying a code.

### 6. Data flow

**Path A — admin UI assigns a code (`New → Opened`); the path that carries the bug**

```
TransportBoxDetail.tsx handleBoxNumberSubmit (172-203)   [regex B\d{3} client-side]
  → PUT change-state { boxId, newState: Opened, boxNumber }
  → ChangeTransportBoxStateHandler.Handle
      :60  GetByIdWithDetailsAsync            → box is TRACKED
      :71  box.AssignBoxCodeIfAny(code)       → Code set IN MEMORY only, not saved
      :106 CallBackMap[(New, Opened)] → HandleNewToOpened
             :228 IsBoxCodeActiveAsync(CODE)
                    → .Where(Code == CODE).Where(OccupiesCodePredicate).AnyAsync()
                    → hits the DATABASE, so the in-flight box (Code unsaved) cannot self-match
             true  → TransportBoxDuplicateActiveBoxFound { code }   ◄── NEW for Quarantine/Error
             false → close same-code Stocked boxes, continue
      :126 transition.ChangeStateAsync → TransportBox.Open(...)
      :134 UpdateAsync + SaveChangesAsync
```

**Path B — warehouse terminal scans a barcode**

```
OpenOrResumeBoxByCodeHandler.Handle
  :49 GetByCodeAsync(code)
        → .Where(Code == CODE)
          .OrderByDescending(OccupiesCodePredicate)   ◄── occupying boxes outrank Stocked/Closed
          .ThenByDescending(Id)
  :52 state == Opened                        → resume (Resumed = true)
  :62 TransportBoxStateRules.OccupiesCode(s) → TransportBoxDuplicateActiveBoxFound { code, state }
  :70 otherwise                              → new TransportBox + Open(code)
```

The two changes interlock. Cascade that is reachable today and that the `GetByCodeAsync` fix stops: box #5 sits in `Quarantine` holding `B001`; an operator assigns `B001` to box #20 from the admin UI (the reported bug) and #20 runs through to `Stocked`; a terminal scan of `B001` ranks #5 and #20 equally (neither is `Closed`), `Id` desc picks #20, and branch 3 mints a **third** row holding `B001`. After the fix, the scan resolves to #5 and is correctly rejected.

### 7. Test surface

| Path | Action | Covers |
| --- | --- | --- |
| `backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxStateRulesTests.cs` | **new** | FR-1, FR-6 |
| `backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxUniquenessTests.cs` | extend | FR-3 (A3 wording) |
| `backend/test/Anela.Heblo.Tests/Repositories/TransportBoxRepositoryCaseHandlingTests.cs` | extend | FR-2 truth table, FR-5 ordering |
| `backend/test/Anela.Heblo.Tests/Repositories/TransportBoxRepositoryCodeOccupancySqlShapeTests.cs` | **new** | A2 — SQL translation |
| `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/OpenOrResumeBoxByCodeHandlerTests.cs` | extend | FR-4, A4 cascade |
| `memory/gotchas/transport-box-code-uniqueness-single-definition.md` | **new** | FR-7, A9 |

Placement matches what is already there: transport-box domain tests live in `Domain/Logistics/`, repository tests in `Repositories/`, handler tests in `Features/Logistics/Transport/`.

**`TransportBoxStateRulesTests` (new).** Two responsibilities, both asserting only through the public surface — never against `CodeReleasingStates`, which is private by design (Decision 1 / A1):

- *Classification and agreement.* For every value of `Enum.GetValues<TransportBoxState>()`, `OccupiesCode(state)` must equal the expected value and must equal `OccupiesCodePredicate.Compile()(box)` for a `TransportBox` in that state. Building an arbitrary-state `TransportBox` through the aggregate's guarded transitions is impractical for all ten values; construct the probe by compiling the predicate against a state value instead, or use a minimal fake — the predicate reads only `b.State`.
- *Drift guard (FR-6).* An exhaustive hard-coded expected map keyed by every current enum member, iterated over `Enum.GetValues<TransportBoxState>()` so an eleventh member fails on a **missing key**, not on a silently-skipped one. The assertion message must name `TransportBoxStateRules.CodeReleasingStates` and instruct the reader to *classify the new state in `TransportBoxStateRules`*, not to append it to the expected map.

**`TransportBoxUniquenessTests` (extend, InMemory).** New cases: a box holding `B001` in `Quarantine` (build via `Open(...)` then `ToQuarantine(...)`) and one in `Error` (`TransportBox.Error(date, user, message)` accepts any source state) must each make `New → Opened` with `B001` return `Success = false` / `TransportBoxDuplicateActiveBoxFound` / `Params["code"] == "B001"`.

**Amendment A3 is binding here.** Do **not** assert "the `New` box's persisted `Code` remains `null`". `AssignBoxCodeIfAny` mutates the tracked entity at `ChangeTransportBoxStateHandler.cs:71` *before* the guard runs, so the shared `ApplicationDbContext`'s tracked instance legitimately carries the rejected code and a correct implementation would fail that assertion. Assert the response only. If persistence must be asserted, re-read through a **second `ApplicationDbContext` bound to the same InMemory database name**, before any further `SaveChangesAsync` on the original context. The existing constructor inlines `Guid.NewGuid().ToString()` as the database name (line 34); capture it into a field so a second context can be constructed against it. Production is unaffected — the context is request-scoped and no `IPipelineBehavior` in `Application/Common/Behaviors/` calls `SaveChanges`.

The five existing tests in this file must pass **unmodified**, in particular `OpenTransportBoxWithCodeThenCloseItThenOpenAnotherWithSameCode_ShouldSucceed` and `OpenTwoTransportBoxesWithDifferentCodes_ShouldSucceed`.

**`TransportBoxRepositoryCaseHandlingTests` (extend, InMemory).** A per-state truth table for `IsBoxCodeActiveAsync` (`false` for `Closed`/`Stocked`, `true` for the other eight, `false` for an unheld code) and the `GetByCodeAsync` ordering case: a `Stocked` box with the **higher** `Id` and an `Opened` box with the **lower** `Id` both holding one code — `GetByCodeAsync` must return the `Opened` one. Seed these on codes distinct from the existing `B001`/`B123`/`B999` fixtures so the six existing mixed-case theories keep their exact expected counts.

**`TransportBoxRepositoryCodeOccupancySqlShapeTests` (new) — amendment A2, mandatory.** `[Collection("PostgresIntegration")]` + `[Trait("Category", "Integration")]`, `PostgresSharedContainerFixture.CreateDatabaseAsync`, and a private `CapturingCommandInterceptor` (`DbCommandInterceptor` overriding `ReaderExecuting`/`ReaderExecutingAsync`), following `Features/Purchase/PurchaseOrderRepositoryHistorySqlShapeTests.cs`. Reuse the `TransportBoxes` / `TransportBoxItems` / `TransportBoxStateLogs` DDL from `Features/Logistics/Transport/ChangeTransportBoxStateReceiveAtomicityIntegrationTests.cs` (all three tables are required — `GetByCodeAsync` `Include`s the two children). Three assertions:

1. `IsBoxCodeActiveAsync` emits a **single** statement referencing the `"State"` column under a negated set membership, with no client-side evaluation.
2. `GetByCodeAsync` emits an `ORDER BY` containing the occupancy expression and completes without `InvalidOperationException`.
3. With a `Quarantine` box at a lower `Id` and a `Stocked` box at a higher `Id` sharing `B001`, `GetByCodeAsync("B001")` returns the `Quarantine` box.

This exists because `UseInMemoryDatabase` runs LINQ-to-Objects and will happily "translate" anything: `Contains` over a `HasConversion<string>()` enum inside a `WHERE` is proven in this codebase, but the same construct inside an `ORDER BY` is not exercised anywhere in `backend/src` today. It is the one way this change can reach staging broken.

*SQL-assertion caveat:* Npgsql may render the negated membership either as inlined literals (`NOT ("State" IN ('Closed','Stocked'))`) or as a parameterised array (`NOT ("State" = ANY (@__CodeReleasingStates_0))`), since `CodeReleasingStates` is a captured static field rather than an inline constant. FR-2's prose says `NOT IN ('Closed','Stocked')`; the assertion must accept **either** form — match on `"State"` plus a negation plus set membership (`IN` or `= ANY`) — and must not pin the literal string, or it will fail on a correct implementation. What is being verified is that translation happens server-side at all, not its exact rendering.

**`OpenOrResumeBoxByCodeHandlerTests` (extend, mock repository).** Busy-state coverage for `Quarantine`, `Error`, `Reserve`, and `Received` alongside the existing `InTransit` case, each expecting `TransportBoxDuplicateActiveBoxFound` with `Params["state"]` equal to that state's name. Plus the A4 cascade case: with `GetByCodeAsync` mocked to return the `Quarantine` box (which is what the fixed repository now returns for that data), the handler returns `TransportBoxDuplicateActiveBoxFound` with `Params["state"] == "Quarantine"` and calls neither `AddAsync` nor `SaveChangesAsync`. The whole existing suite must pass unmodified — FR-4 is a pure de-duplication.

**Also unmodified:** `ChangeTransportBoxStateHandlerTests` (mocks `ITransportBoxRepository`, so the repository change is invisible to it), `GetTransportBoxByCodeHandlerTests`, `TransportBoxStateTransitionTests`, `TransportBoxCodeCaseHandlingTests`.

**Validation before completion:** `dotnet build`, `dotnet format`, and the full `Anela.Heblo.Tests` suite **including** `Category=Integration` (Docker required for `postgres:16`, already a prerequisite for the existing `PostgresIntegration` collection). No frontend file changes, so `npm run build` / `npm run lint` and the E2E suite are not required.

### 8. Memory note (FR-7)

`memory/gotchas/transport-box-code-uniqueness-single-definition.md`, following the front-matter + Symptom / Root cause / Fix / Rules / Related files shape of `memory/gotchas/postgres-partial-index-active-states.md`. It records the failure scenario, the rule that `TransportBoxStateRules` is the only place transport-box code occupancy may be defined, the three consuming call sites by path (`TransportBoxRepository.IsBoxCodeActiveAsync`, `TransportBoxRepository.GetByCodeAsync`, `OpenOrResumeBoxByCodeHandler`), and the read-only detection query below.

Per amendment A9, it must also state that the `'Closed'`/`'Stocked'` literals in that SQL — and in any future partial-index predicate — are a **deliberate second copy** of the rule that cannot reference `TransportBoxStateRules`, so whoever repartitions the states must update the query and the follow-up index alongside the type. That is the one place the duplication is unavoidable, and naming it beats pretending otherwise.

## Data Schemas

**No database schema change, no EF migration, no data migration.** `TransportBoxConfiguration` is untouched: `HasKey(x => x.Id)`, `HasConversion<string>()` on `State` and `DefaultReceiveState`, the `ConcurrencyStamp` / `ExtraProperties` / timestamp column configs, and the two `HasMany` relationships all stay as they are. No uniqueness constraint on `Code` is added — see Out of Scope in the spec, and amendment A8's follow-up.

**No API request/response shape changes.** No new endpoint, no DTO added or altered, therefore no OpenAPI regeneration and no `frontend/src/api/generated/api-client.ts` churn. The DTO-are-classes-never-records rule is not engaged because no `Contracts/` type is touched. `ErrorCodes.TransportBoxDuplicateActiveBoxFound` (1405) is reused as-is, already translated in `frontend/src/i18n.ts:155-156`.

Entities involved, unchanged: `TransportBox` (`Id`, `Code`, `State`, `DefaultReceiveState`, `Description`, `LastStateChanged`, `Location`, audit fields, `ConcurrencyStamp`, `ExtraProperties`) owning `TransportBoxItem` and `TransportBoxStateLog` collections. `TransportBoxState` keeps all ten members in their current order.

### State partition (classification only — the enum is untouched)

| Partition | Members | Meaning |
| --- | --- | --- |
| Code-releasing | `Closed`, `Stocked` | Box no longer holds its code; the code may be assigned to another box. |
| Code-occupying (default) | `New`, `Opened`, `InTransit`, `Received`, `InSwap`, `Error`, `Reserve`, `Quarantine` | Box still holds its code; assigning it elsewhere is rejected. |

`Quarantine` and `Error` move from *free* to *occupying* — this is the bug fix. `InSwap` also becomes occupying; it is dead (enum and i18n label maps only, no backend producer or consumer, verified) so this is a no-op in practice, and the fail-safe default. `New` becomes occupying but no path persists a `New` box carrying a code (§ 5 above).

### Observable behaviour deltas — both tightenings

| Flow | Endpoint | Before | After |
| --- | --- | --- | --- |
| Assign box code (`New → Opened`) while the code is held by a `Quarantine` or `Error` box | `ChangeTransportBoxState` | 200, `Success = true`, duplicate row created | 200, `Success = false`, `ErrorCode = TransportBoxDuplicateActiveBoxFound`, `Params["code"]` |
| Scan a code held by both an occupying box and a newer `Stocked`/`Closed` box | `GetTransportBoxByCode`, `OpenOrResumeBoxByCode` | returns the released box | returns the occupying box |

The change is strictly tighter than current behaviour: it rejects assignments that previously succeeded and never permits one that previously failed, so it cannot widen access. Consumers `GetTransportBoxByCodeHandler` (line 42) and `OpenOrResumeBoxByCodeHandler` (line 49) are not modified — they inherit the corrected resolution. `GetTransportBoxByCodeHandler`'s `isReceivable` check (`{InTransit, Reserve, Quarantine}`, lines 51-54) is a different concept and stays untouched.

Rows that already violate the invariant in production are left as they are and keep functioning; the re-ordering makes scans resolve to the occupying box, which is the correct answer for exactly that corrupt data.

### Detection query (read-only, verbatim in the memory note)

Matches the actual mapping — `State` is a string column and the table is `public."TransportBoxes"`.

```sql
SELECT "Code", COUNT(*), array_agg("Id"), array_agg("State")
FROM public."TransportBoxes"
WHERE "Code" IS NOT NULL AND "State" NOT IN ('Closed','Stocked')
GROUP BY "Code" HAVING COUNT(*) > 1;
```

No `UPDATE`, no `DELETE`, no DDL. Run it against staging and production **before** merge, per § Prerequisites of the architecture review: it sizes the pre-existing corruption, says whether the re-resolution will visibly change behaviour for real operators, and gates the A8 follow-up for the partial unique index.
