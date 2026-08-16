# Architecture Review: Unify Transport Box Code Uniqueness Into a Single State Rule

## Skip Design: true

Backend-only. No new or changed components, screens, or layouts. The single user-visible
consequence — an operation that used to succeed now returns
`TransportBoxDuplicateActiveBoxFound` — reuses an error code that is already surfaced and
already translated (`frontend/src/i18n.ts:155-156`, `"Box s číslem {code} již existuje a je
stále aktivní"`, verified). No contract change, so no OpenAPI regeneration and no
`frontend/src/api/generated/api-client.ts` churn.

## Architectural Fit Assessment

I verified every factual claim in `spec.r1.md` against the code. **All of them hold.**

| Spec claim | Verified at | Result |
| --- | --- | --- |
| `IsBoxCodeActiveAsync` allow-list is `{New, Opened, InTransit, Received, Reserve}` | `TransportBoxRepository.cs:96-115` | Confirmed. `Quarantine` and `Error` absent. |
| `OpenOrResumeBoxByCodeHandler` deny-list is `!= Closed && != Stocked` | `OpenOrResumeBoxByCodeHandler.cs:62` | Confirmed. |
| `GetByCodeAsync` orders only `Closed` last, then `Id` desc | `TransportBoxRepository.cs:117-131` | Confirmed. |
| `IsBoxCodeActiveAsync` is the *only* guard on `New → Opened` | `ChangeTransportBoxStateHandler.cs:226-237` | Confirmed; single call site in `backend/src`. |
| No DB uniqueness constraint on `Code` | `TransportBoxConfiguration.cs` (full file read) | Confirmed. Only `HasKey(Id)`, `HasConversion<string>()` on `State`, and the two `HasMany`. |
| `TransportBoxState` has the ten listed members | `TransportBoxState.cs` | Confirmed, in the stated order. |
| Admin UI reaches `New → Opened` independently of the terminal | `TransportBoxDetail.tsx:172-203` | Confirmed — `changeStateMutation` with `newState: Opened` + `boxNumber`. |
| `InSwap` has no backend producer/consumer | grep over `backend/src` | Confirmed — enum member only. |
| `TransportBox.Error(...)` accepts any source state | `TransportBox.cs:259-262` (`Array.Empty<TransportBoxState>()` ⇒ `CheckState` no-ops) | Confirmed. |

The proposal fits the codebase. `Anela.Heblo.Domain` already holds exactly this kind of
static state predicate on the same aggregate (`TransportBox.IsInTransportPredicate` /
`IsInReservePredicate` / `IsInQuarantinePredicate`, `TransportBox.cs:39-50`), it already
references `System.Linq.Expressions`, and both consumers (`Anela.Heblo.Persistence`,
`Anela.Heblo.Application`) already reference Domain. No new project reference, no new
package, no cross-module coupling — consistent with the Vertical Slice / module-independence
rules in `docs/architecture/development_guidelines.md`.

The deny-list-as-canonical decision (Assumption 1) is the right call and I would not
reopen it: it makes the *fail-safe* direction the default, which is precisely the property
whose absence caused this bug.

**Where the spec is architecturally weak** — three things, all fixable, detailed under
Specification Amendments:

1. **The "single definition" is not actually single.** As FR-1/FR-2/FR-5 are written,
   `OccupiesCodePredicate` has zero consumers (dead on arrival), while Persistence restates
   `CodeReleasingStates.Contains(...)` twice by hand. That is two hand-written restatements
   of the invariant in the layer that had the bug — a smaller version of the same problem.
2. **FR-2 and FR-5 make claims about generated SQL that none of the proposed tests can
   verify.** Every test named in § Testing runs on `UseInMemoryDatabase`, which evaluates
   LINQ in memory and will happily "translate" anything. The repo already has the right
   tool for this and it is used in this very module.
3. **One FR-3 acceptance criterion is a change-tracker trap** that will make an
   otherwise-correct implementation look broken.

## Proposed Architecture

### Component Overview

```
Anela.Heblo.Domain / Features / Logistics / Transport
  TransportBoxState.cs            (enum — untouched)
  TransportBox.cs                 (aggregate — untouched)
  TransportBoxStateRules.cs       ◄── NEW: sole owner of "does this state hold the code?"
        private  static readonly TransportBoxState[] CodeReleasingStates = { Closed, Stocked }
        public   static bool OccupiesCode(TransportBoxState)
        public   static readonly Expression<Func<TransportBox,bool>> OccupiesCodePredicate
                         │                                    │
        in-memory checks │                    EF query composition
                         │                                    │
                         ▼                                    ▼
Anela.Heblo.Application                       Anela.Heblo.Persistence
  OpenOrResumeBoxByCodeHandler:62               TransportBoxRepository
    OccupiesCode(existing.State)                  IsBoxCodeActiveAsync  → .Where(OccupiesCodePredicate)
                                                  GetByCodeAsync        → .OrderByDescending(OccupiesCodePredicate)
                         ▲
        ChangeTransportBoxStateHandler.HandleNewToOpened
          (unchanged — inherits the fix through IsBoxCodeActiveAsync)
```

Every arrow into the rule is a *reference*, never a restatement. That is the whole point of
the change and it should be visible in the diff: after this change, the tokens
`TransportBoxState.Closed` and `TransportBoxState.Stocked` must not appear anywhere in
`TransportBoxRepository.IsBoxCodeActiveAsync`, `TransportBoxRepository.GetByCodeAsync`, or
`OpenOrResumeBoxByCodeHandler`.

### Key Design Decisions

#### Decision 1: The predicate, not the array, is the public surface

**Options considered:**
(a) Spec as written — `public static readonly TransportBoxState[] CodeReleasingStates`
consumed directly by both repository methods, plus `OccupiesCode` and an unused
`OccupiesCodePredicate`.
(b) Expose only `OccupiesCode(TransportBoxState)` + `OccupiesCodePredicate`; keep the array
`private`.

**Chosen approach:** (b).

**Rationale:** Three independent reasons, any one of which is sufficient.

*It removes a dead member.* Under the spec as written, FR-2 filters with
`!CodeReleasingStates.Contains(x.State)`, FR-4 calls `OccupiesCode`, and FR-5 uses
`CodeReleasingStates.Contains(o.State) ? 1 : 0`. Nothing consumes `OccupiesCodePredicate`.
A new type whose third member has no caller on the day it is merged is a defect.

*It makes the definition genuinely single.* Both repository call sites work directly:

```csharp
// IsBoxCodeActiveAsync
var exists = await DbSet
    .Where(x => x.Code == upperBoxCode)
    .Where(TransportBoxStateRules.OccupiesCodePredicate)
    .AnyAsync();

// GetByCodeAsync
var transportBox = await DbSet
    .Include(x => x.Items)
    .Include(x => x.StateLog)
    .Where(x => x.Code == upperBoxCode)
    .OrderByDescending(TransportBoxStateRules.OccupiesCodePredicate)   // true (occupying) first
    .ThenByDescending(o => o.Id)
    .FirstOrDefaultAsync();
```

`OrderByDescending` accepts `Expression<Func<TransportBox, bool>>` with `TKey = bool`;
PostgreSQL sorts `false < true`, so `DESC` puts code-occupying boxes first — exactly FR-5's
`? 1 : 0` intent, without restating the rule. Note the restructure from
`FirstOrDefaultAsync(predicate)` to `.Where(...).FirstOrDefaultAsync()`: keep the code filter
*before* the ordering so the `ORDER BY` applies to the already-filtered set.

*It closes a mutability hole.* `public static readonly T[]` is only shallowly readonly —
any assembly in the solution could execute `TransportBoxStateRules.CodeReleasingStates[0] =
TransportBoxState.Quarantine` and silently reopen the exact bug being fixed. For a type
whose entire job is to be the one immutable definition of an invariant, a publicly writable
backing array is the wrong shape.

`Anela.Heblo.Domain.csproj` carries `<InternalsVisibleTo Include="Anela.Heblo.Tests" />`, so
if the drift-guard test genuinely needs the partition it can take the array as `internal`.
It should not need to — see Decision 3.

#### Decision 2: SQL-shape verification runs against real PostgreSQL, not InMemory

**Options considered:** (a) rely on the InMemory-backed tests listed in the spec;
(b) add one Testcontainers-backed test class alongside them.

**Chosen approach:** (b), mandatory, not optional.

**Rationale:** FR-2 asserts "the generated SQL must be a `NOT IN ('Closed','Stocked')` over
the string column" and FR-5 introduces an `ORDER BY` over a boolean expression. Neither is
observable under `UseInMemoryDatabase` — and `TransportBoxUniquenessTests` and
`TransportBoxRepositoryCaseHandlingTests`, the two files the spec extends, both construct
their context with `.UseInMemoryDatabase(...)` (verified). InMemory runs LINQ-to-Objects; a
query that cannot be translated by Npgsql passes there and fails in staging.

The risk is not hypothetical for FR-5 specifically. `Contains` inside a `WHERE` over a
`HasConversion<string>()` enum column is proven in this codebase (it is what the *current*
`IsBoxCodeActiveAsync` does). A value-converted enum `Contains` inside an `ORDER BY`
expression is **not** exercised anywhere in `backend/src` today.

The convention already exists and is used in this module:
`backend/test/Anela.Heblo.Tests/Common/PostgresSharedContainerFixture.cs`,
`[Collection("PostgresIntegration")]`, `[Trait("Category", "Integration")]`, and a
`CapturingCommandInterceptor` to assert on emitted SQL — see
`Features/Purchase/PurchaseOrderRepositoryHistorySqlShapeTests.cs` for the shape, and
`Features/Logistics/Transport/ChangeTransportBoxStateReceiveAtomicityIntegrationTests.cs`
for the same fixture applied to transport boxes.

#### Decision 3: The drift guard asserts through `OccupiesCode`, never against the array

**Options considered:** (a) the test asserts the two sets `CodeReleasingStates` /
everything-else literally; (b) the test enumerates `Enum.GetValues<TransportBoxState>()`,
calls `OccupiesCode` on each, and compares against a hard-coded expected map.

**Chosen approach:** (b).

**Rationale:** It keeps the array private (Decision 1), it is what FR-6's own second
acceptance criterion already asks for, and it tests the observable contract rather than the
implementation detail. The expected map must be exhaustive so that adding an eleventh enum
member fails on a missing key, and the assertion message must name
`TransportBoxStateRules` and tell the reader to *classify the new state*, not to append it
to the expected map.

#### Decision 4: `IsBoxCodeActiveAsync` keeps its name — and gains its doc on the interface

Agreed with Assumption 4: renaming churns `ITransportBoxRepository` plus four test files for
no behavioural gain. But the name now actively misleads (it answers "is the code occupied",
and `Error`/`Quarantine` are not colloquially "active"), so the XML summary is load-bearing.
Put it on `ITransportBoxRepository.cs:19` — the interface has no documentation at all today
and that is what every caller sees — not only on the implementation.

## Implementation Guidance

### Directory / Module Structure

| Path | Action |
| --- | --- |
| `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxStateRules.cs` | **new** — the rule type |
| `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/ITransportBoxRepository.cs` | edit — XML doc on `IsBoxCodeActiveAsync` |
| `backend/src/Anela.Heblo.Persistence/Logistics/TransportBoxes/TransportBoxRepository.cs` | edit — `IsBoxCodeActiveAsync` (96-115), `GetByCodeAsync` (117-131) |
| `backend/src/Anela.Heblo.Application/.../OpenOrResumeBoxByCode/OpenOrResumeBoxByCodeHandler.cs` | edit — line 62 predicate, and the now-true comment on line 69 |
| `backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxStateRulesTests.cs` | **new** — FR-1 + FR-6 |
| `backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxUniquenessTests.cs` | extend — FR-3 |
| `backend/test/Anela.Heblo.Tests/Repositories/TransportBoxRepositoryCaseHandlingTests.cs` | extend — FR-2 truth table, FR-5 ordering |
| `backend/test/Anela.Heblo.Tests/Repositories/TransportBoxRepositoryCodeOccupancySqlShapeTests.cs` | **new** — Decision 2 |
| `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/OpenOrResumeBoxByCodeHandlerTests.cs` | extend — FR-4 |
| `memory/gotchas/transport-box-code-uniqueness-single-definition.md` | **new** — FR-7 |

Placement matches what is already there: the existing transport-box domain tests live in
`Domain/Logistics/` (`TransportBoxUniquenessTests.cs`, `TransportBoxCodeCaseHandlingTests.cs`)
and the repository tests in `Repositories/`.

Nothing under `backend/src/Anela.Heblo.API/` changes. No `Contracts/` type changes, so the
DTO-are-classes-never-records rule is not engaged.

### Interfaces and Contracts

```csharp
namespace Anela.Heblo.Domain.Features.Logistics.Transport;

/// <summary>
/// The single definition of transport-box code occupancy: whether a box in a given state
/// still holds its <see cref="TransportBox.Code"/> and therefore blocks that code from
/// being assigned to another box.
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
    // Private: the array is an implementation detail, and `public static readonly T[]`
    // would be publicly mutable.
    private static readonly TransportBoxState[] CodeReleasingStates =
    {
        TransportBoxState.Closed,
        TransportBoxState.Stocked,
    };

    /// <summary>In-memory check, for handlers that already hold a state value.</summary>
    public static bool OccupiesCode(TransportBoxState state) =>
        !CodeReleasingStates.Contains(state);

    /// <summary>
    /// EF-composable form of <see cref="OccupiesCode"/>. Translates to
    /// <c>NOT ("State" = ANY(...))</c> against the HasConversion&lt;string&gt; column.
    /// </summary>
    public static readonly Expression<Func<TransportBox, bool>> OccupiesCodePredicate =
        b => !CodeReleasingStates.Contains(b.State);
}
```

Declare `CodeReleasingStates` textually before `OccupiesCodePredicate`. The expression tree
captures the field as a member access rather than a value, so initialization order is not
strictly load-bearing, but relying on that is unnecessary subtlety.

`ITransportBoxRepository` — signature unchanged, documentation added:

```csharp
/// <summary>
/// True when any box currently occupies <paramref name="boxCode"/> — i.e. holds it in a
/// state for which <see cref="TransportBoxStateRules.OccupiesCode"/> is true. Matching is
/// case-insensitive. The name predates the rule; "active" here means "occupying the code",
/// which includes Error and Quarantine.
/// </summary>
Task<bool> IsBoxCodeActiveAsync(string boxCode);
```

No public HTTP contract changes. `ErrorCodes.TransportBoxDuplicateActiveBoxFound` (1405) is
reused as-is.

### Data Flow

**Path A — admin UI assigns a code (`New → Opened`), the path that carries the bug**

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
             true  → return TransportBoxDuplicateActiveBoxFound { code }   ◄── NEW for Quarantine/Error
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
  :52 state == Opened            → resume
  :62 OccupiesCode(state)        → TransportBoxDuplicateActiveBoxFound { code, state }
  :70 otherwise                  → new TransportBox + Open(code)
```

Note how FR-5 and FR-4 interlock. The comment at `OpenOrResumeBoxByCodeHandler.cs:69` —
*"GetByCodeAsync returns any active box first, so reaching here means none exists"* — is
**false today** and becomes true only because of FR-5. That is the strongest argument for
FR-5 and the spec does not make it. Concrete cascade, all three steps reachable today:

1. Box #5 sits in `Quarantine` holding `B001`.
2. Operator assigns `B001` to box #20 from the admin UI — succeeds (the reported bug).
   Box #20 runs InTransit → Received → `Stocked`.
3. Terminal scans `B001`. `GetByCodeAsync` ranks #5 and #20 equally (neither is `Closed`),
   `Id` desc picks #20 (`Stocked`) → the handler falls through to branch 3 and mints a
   **third** row holding `B001`.

After FR-5, step 3 returns box #5 and the scan is correctly rejected. FR-5 is not a
cosmetic tie-break; it stops the corruption from compounding.

## Risks and Mitigations

| Risk | Severity | Mitigation |
| --- | --- | --- |
| `OrderByDescending(Expression<Func<TransportBox,bool>>)` over a `HasConversion<string>()` enum fails to translate on Npgsql. Not exercised anywhere in `backend/src` today, and invisible to every InMemory test in the spec. | **High** | Decision 2 / Amendment A2 — a `[Collection("PostgresIntegration")]` SQL-shape test on `GetByCodeAsync`. Non-negotiable; this is the one way this change can reach staging broken. |
| FR-3's "persisted `Code` remains null" assertion fails against a shared `ApplicationDbContext` because `AssignBoxCodeIfAny` dirties the tracked entity at `ChangeTransportBoxStateHandler.cs:71` before the guard runs. | **Medium** | Amendment A3 — reword the criterion; assert through a second context bound to the same InMemory database name, before any further `SaveChangesAsync`. Production is safe (request-scoped context, and no `IPipelineBehavior` in `Application/Common/Behaviors/` calls `SaveChanges`). |
| `New` joins the code-occupying set and blocks on a persisted `New` row that carries a `Code`. | **Low** | Verified impossible: `AssignBoxCodeIfAny` (`TransportBox.cs:264-273`) is the only writer of `Code` on a `New` box, is called once (`ChangeTransportBoxStateHandler.cs:71`) and is always followed in the same unit of work by `Open(...)`, which moves the box to `Opened`; `Reset()` (`TransportBox.cs:167-174`) nulls `Code` on the `Opened → New` return. `CreateNewTransportBoxHandler` and `OpenOrResumeBoxByCodeHandler` never persist a `New` box with a code. Record this in the spec (A6) so it is not re-litigated. |
| Pre-existing duplicate rows in production. The tightened rule does not clean them up; FR-5 changes which one a scan resolves to. | **Medium** | Accepted and correct — FR-5 resolves to the occupying box, which is the right answer for exactly this corrupt data. FR-7's detection query is the operator's tool. Run it against staging and production **before** merge so the blast radius is known, not after. |
| TOCTOU between `IsBoxCodeActiveAsync` and `SaveChangesAsync` — two operators assigning the same code concurrently. | **Low** | Correctly out of scope. Single-warehouse, millisecond window, and the DB constraint that would close it cannot be applied while duplicates may exist. A8 makes the follow-up explicit rather than implicit. |
| The rule drifts *again* via a fourth call site added later that hand-writes the comparison. | **Medium** | Decision 1 keeps the array private, so a new call site physically cannot restate the set without adding a public member — the reviewable moment. Plus FR-7's memory note and FR-6's guard. |
| FR-1's acceptance criterion ("no allow-list or deny-list of transport-box states … exists anywhere else in `backend/src`") contradicts the spec's own Out of Scope. | **Low** | Amendment A7 — scope the criterion to code-uniqueness. `GetPagedListAsync`'s `State != Closed` (list filter) and `GetTransportBoxByCodeHandler`'s `isReceivable` (`{InTransit, Reserve, Quarantine}`) are different concepts and must survive. |

## Specification Amendments

**A1 — Rewrite FR-1's member list and FR-2/FR-5's call sites (Decision 1).**
`CodeReleasingStates` becomes `private`. FR-2 composes `.Where(x => x.Code == upperBoxCode)
.Where(TransportBoxStateRules.OccupiesCodePredicate)`. FR-5 becomes
`.Where(x => x.Code == upperBoxCode).OrderByDescending(TransportBoxStateRules.OccupiesCodePredicate)
.ThenByDescending(o => o.Id).FirstOrDefaultAsync()` — drop the `? 1 : 0` restatement and the
`FirstOrDefaultAsync(predicate)` overload. Without this, `OccupiesCodePredicate` ships with
zero consumers and Persistence hand-writes the invariant twice.

**A2 — Add a mandatory PostgreSQL SQL-shape test (Decision 2).** New file
`backend/test/Anela.Heblo.Tests/Repositories/TransportBoxRepositoryCodeOccupancySqlShapeTests.cs`,
`[Collection("PostgresIntegration")]` + `[Trait("Category", "Integration")]`, using
`PostgresSharedContainerFixture.CreateDatabaseAsync` and a `CapturingCommandInterceptor`.
It must assert: (i) `IsBoxCodeActiveAsync` emits a single statement with the negated set
membership over the string `State` column and no client-side evaluation; (ii)
`GetByCodeAsync` emits an `ORDER BY` containing the occupancy expression and executes
without an `InvalidOperationException`; (iii) with a `Quarantine` box at a lower `Id` and a
`Stocked` box at a higher `Id` sharing `B001`, `GetByCodeAsync` returns the `Quarantine` one.
Add this to § Testing and to § Validation before completion. Keep the InMemory tests too —
they cover the truth table cheaply; this one covers translation.

**A3 — Reword the FR-3 acceptance criterion.** Replace *"the `New` box's persisted `Code`
remains `null` and its state remains `New`"* with: *"the response is
`Success = false` / `TransportBoxDuplicateActiveBoxFound` / `Params["code"] == "B001"`. If
persistence is asserted, re-read the box through a second `ApplicationDbContext` bound to
the same InMemory database name and do so before any further `SaveChangesAsync` on the
original context — `AssignBoxCodeIfAny` mutates the tracked entity at
`ChangeTransportBoxStateHandler.cs:71` before the guard runs, so the first context's tracked
instance legitimately carries the rejected code."* As written, a correct implementation
fails this criterion.

**A4 — Strengthen FR-5's rationale and add the cascade test.** Add the three-step cascade
from § Data Flow to FR-5's justification, and one acceptance criterion: *given a
`Quarantine` box (lower `Id`) and a `Stocked` box (higher `Id`) both holding `B001`,
`OpenOrResumeBoxByCode("B001")` returns `TransportBoxDuplicateActiveBoxFound` with
`Params["state"] == "Quarantine"` and creates no box* — today it silently creates a third
row. Also update the stale comment at `OpenOrResumeBoxByCodeHandler.cs:69` to state that the
guarantee comes from `TransportBoxStateRules.OccupiesCodePredicate` ordering in
`GetByCodeAsync`.

**A5 — Correct FR-1's rationale for the array type.** *"Must be an array (not
`HashSet`/`List`) so EF Core translates `Contains`"* is not true of EF Core 8 — `List<T>`,
`IEnumerable<T>` and `HashSet<T>` all translate. Keep the array (it matches the existing
code and is optimal for two elements), but state the real reason: consistency with the
current implementation and with `memory/gotchas/postgres-partial-index-active-states.md`'s
`private static readonly int[] ActiveStates` precedent. Leaving a false constraint in a spec
that is explicitly about preventing future drift is self-defeating.

**A6 — Add an explicit note under § Assumptions that `New` never carries a persisted code**,
with the three-call-site justification from the risk table. Assumption 3 covers `InSwap`;
`New` is the other member whose reclassification deserves a written "verified harmless".

**A7 — Scope FR-1's fourth acceptance criterion.** Change *"No allow-list or deny-list of
transport-box states … exists anywhere else in `backend/src`"* to *"…for the purpose of
code uniqueness. `GetPagedListAsync`'s `isActiveFilter` and `GetTransportBoxByCodeHandler`'s
`isReceivable` are different concepts and must remain untouched (see Out of Scope)."*
The criterion as written contradicts the Out of Scope section and a naive grep will flag
both.

**A8 — Promote the DB constraint from a prose aside to a filed follow-up.** § Out of Scope
says "file a follow-up for the constraint"; make that a numbered deliverable so it is not
lost. The follow-up must note that
`memory/gotchas/postgres-partial-index-active-states.md` rule 1 applies —
`CREATE INDEX CONCURRENTLY` requires `migrationBuilder.Sql(sql, suppressTransaction: true)`
or PostgreSQL rejects it with SQLSTATE 25001 — and that its predicate must be generated
from the same partition (`WHERE "State" NOT IN ('Closed','Stocked')`, string literals, since
`State` is `HasConversion<string>()`, not int).

**A9 — FR-7 nit.** The detection query is correct against the actual mapping (`State` is a
string column, table is `public."TransportBoxes"`, verified in `TransportBoxConfiguration.cs`).
Add one line to the memory file recording that the `Closed`/`Stocked` literals in that SQL
are a *deliberate second copy* of the rule that cannot reference `TransportBoxStateRules`,
so whoever changes the partition must update the query and the follow-up index too. That is
the one place duplication is unavoidable, and naming it is better than pretending otherwise.

## Prerequisites

Nothing blocking. Specifically:

- **No EF migration, no schema change, no data migration.** Confirmed against
  `TransportBoxConfiguration.cs` — the change touches only LINQ composition. This matters
  because migrations here are manual and out of band
  (`CLAUDE.md` § Project facts, `memory/gotchas/ef-migration-codebase-drift.md`).
- **No new NuGet or npm package, no feature flag, no config, no Key Vault secret.**
- **Docker must be available on the machine running the test suite** for the A2 integration
  test (`PostgresSharedContainerFixture` pulls `postgres:16`). This is already true for the
  existing `PostgresIntegration` collection, including
  `ChangeTransportBoxStateReceiveAtomicityIntegrationTests` in this same module — so it is a
  prerequisite of the test run, not a new one for the repo.
- **Run FR-7's detection query against staging and production before merge.** Not a code
  dependency, but it sizes the pre-existing corruption, tells you whether FR-5's
  re-resolution will visibly change behaviour for real operators, and gates the A8 follow-up.

Validation before completion, per `CLAUDE.md`: `dotnet build`, `dotnet format`, the full
`Anela.Heblo.Tests` suite **including** `Category=Integration`. No frontend files change, so
`npm run build` / `npm run lint` and the E2E suite are correctly not required.

## Verdict

**Approve with amendments.** The diagnosis is right, every load-bearing claim checks out
against the code, the deny-list decision is correct, and the scope boundaries are drawn in
the right places. A1, A2 and A3 must land before implementation starts: A1 is what makes
the "single definition" claim actually true, A2 is the only defence against a translation
failure reaching staging, and A3 prevents a correct implementation from being judged broken
by its own acceptance criteria.
