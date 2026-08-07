# Specification: Unify Transport Box Code Uniqueness Into a Single State Rule

## Summary

The invariant "a box code may only be held by one non-terminal transport box at a time" is currently enforced by two independent, contradictory implementations — an allow-list in `TransportBoxRepository.IsBoxCodeActiveAsync` and a deny-list in `OpenOrResumeBoxByCodeHandler`. The allow-list has already drifted: `Quarantine` and `Error` are missing from it, so an operator can assign a code that is still physically in use by a quarantined box, producing two live rows with the same `Code` and silently mis-routing every subsequent barcode scan. This change introduces one domain-level definition of "this state still occupies the box code", makes both enforcement points and the code-based lookup consume it, and adds a drift guard so adding a new `TransportBoxState` cannot silently reopen the hole.

## Background

`TransportBoxState` has ten members: `New`, `Opened`, `InTransit`, `Received`, `InSwap`, `Stocked`, `Closed`, `Error`, `Reserve`, `Quarantine` (`backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxState.cs`).

Three places reason about "is this code still taken", and no two of them agree:

| Location | Rule as written | `Quarantine` | `Error` | `Stocked` |
| --- | --- | --- | --- | --- |
| `TransportBoxRepository.IsBoxCodeActiveAsync` (`TransportBoxRepository.cs:96-115`) | allow-list `{New, Opened, InTransit, Received, Reserve}` | **free (wrong)** | **free (wrong)** | free |
| `OpenOrResumeBoxByCodeHandler` (`OpenOrResumeBoxByCodeHandler.cs:62`) | deny-list `state != Closed && state != Stocked` | taken (correct) | taken (correct) | free |
| `TransportBoxRepository.GetByCodeAsync` (`TransportBoxRepository.cs:117-131`) | orders `Closed` last, then `Id` descending | ranked with active boxes | ranked with active boxes | **ranked with active boxes (wrong)** |

`IsBoxCodeActiveAsync` is the only guard on the `New → Opened` transition (`ChangeTransportBoxStateHandler.HandleNewToOpened`, `ChangeTransportBoxStateHandler.cs:213-247`), which is reachable from the admin UI at `frontend/src/components/pages/TransportBoxDetail.tsx` (`handleBoxNumberSubmit`, lines 172-203) independently of the warehouse-terminal barcode flow. There is no DB-level uniqueness constraint — `TransportBoxConfiguration` declares none — so the application-layer check is the only defence.

**Concrete failure today:** box A sits in `Quarantine` holding `B001`. An operator assigns `B001` to a fresh box from the box-detail screen; `IsBoxCodeActiveAsync("B001")` returns `false` because `Quarantine` is absent from the allow-list, and the assignment succeeds. Two rows now hold `B001`. When warehouse staff scan the physical `B001` label, `GetByCodeAsync` returns the newer, unrelated box (both are non-`Closed`; tie broken by `Id` descending), and every scan-driven action — fill, receive, stock-up — applies to the wrong aggregate with no error raised.

The root cause is not the missing enum members; it is that the invariant has no single owner. `Quarantine` was added to the enum and to the transition graph without anyone updating the allow-list. The same will happen to the next state added unless both call sites derive from one definition.

## Assumptions

These are decisions taken from the brief's "suggested direction"; they are settled, not open:

1. **The canonical rule is a deny-list, not an allow-list.** Codes are released only by `Closed` and `Stocked`; every other state — present or future — occupies the code. This makes the fail-safe direction the default: a newly added state occupies its code until someone deliberately decides otherwise, which is the opposite of the drift that caused this bug.
2. **`Stocked` continues to release the code.** This is existing, intentional behaviour on both sides: `OpenOrResumeBoxByCodeHandler` treats `Stocked` as reusable, and `HandleNewToOpened` explicitly closes any `Stocked` box carrying the same code before opening the new one (`ChangeTransportBoxStateHandler.cs:239-246`). Nothing in this change alters that.
3. **`InSwap` is dead** — it exists in the enum and in the i18n label maps only; no backend code produces or consumes it. Under the deny-list it becomes code-occupying. This is a no-op in practice and the fail-safe default.
4. **`IsBoxCodeActiveAsync` keeps its name and signature.** Renaming would churn the repository interface, mocks in `ChangeTransportBoxStateHandlerTests` and `TransportBoxRepositoryCaseHandlingTests` for no behavioural gain. Its XML doc will state that it delegates to the shared rule.
5. **No DB constraint in this change.** See Out of Scope for the reasoning.

## Functional Requirements

### FR-1: Single domain-level definition of code occupancy

Introduce one static rule type in the Logistics domain — `TransportBoxStateRules` at
`backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxStateRules.cs` — that owns the answer to "does a box in this state still hold its code?".

It must expose exactly three members:

- `public static readonly TransportBoxState[] CodeReleasingStates` — `{ Closed, Stocked }`, the *only* states that free a code. Must be an array (not `HashSet`/`List`) so EF Core translates `Contains` against it.
- `public static bool OccupiesCode(TransportBoxState state)` — `!CodeReleasingStates.Contains(state)`; for in-memory checks inside handlers.
- `public static readonly Expression<Func<TransportBox, bool>> OccupiesCodePredicate` — `b => !CodeReleasingStates.Contains(b.State)`; for EF query composition.

The type carries an XML summary stating that it is the single definition of box-code occupancy and that both `IsBoxCodeActiveAsync` and `OpenOrResumeBoxByCodeHandler` must consume it rather than restate it. It follows the existing static-predicate convention already present on `TransportBox` (`IsInTransportPredicate`, `IsInReservePredicate`, `IsInQuarantinePredicate` — `TransportBox.cs:39-50`).

**Acceptance criteria:**
- `TransportBoxStateRules.OccupiesCode` returns `false` for `Closed` and `Stocked`, and `true` for `New`, `Opened`, `InTransit`, `Received`, `InSwap`, `Error`, `Reserve`, `Quarantine`.
- `OccupiesCodePredicate` compiled against an in-memory `TransportBox` agrees with `OccupiesCode(box.State)` for every enum value.
- The type lives in `Anela.Heblo.Domain` and takes no dependency on Application or Persistence.
- No allow-list or deny-list of transport-box states for the purpose of code uniqueness exists anywhere else in `backend/src` after this change (grep for `TransportBoxState.Closed` / `TransportBoxState.Stocked` in the two call sites shows no literal comparisons remaining for this purpose).

### FR-2: `IsBoxCodeActiveAsync` derives from the shared rule

`TransportBoxRepository.IsBoxCodeActiveAsync` (`backend/src/Anela.Heblo.Persistence/Logistics/TransportBoxes/TransportBoxRepository.cs:96-115`) drops its local `activeStates` array and filters with the shared rule instead — code equality on the upper-cased code AND `!TransportBoxStateRules.CodeReleasingStates.Contains(x.State)`.

`State` is mapped with `HasConversion<string>()` (`TransportBoxConfiguration.cs:16-17`), so the generated SQL must be a `NOT IN ('Closed','Stocked')` over the string column. Existing case-insensitivity behaviour (`boxCode.ToUpper()`) and the existing debug log line are unchanged.

**Acceptance criteria:**
- `IsBoxCodeActiveAsync` returns `true` for a box holding the code in `Quarantine` (previously `false` — this is the bug fix).
- Returns `true` in `Error`, and continues to return `true` in `New`, `Opened`, `InTransit`, `Received`, `Reserve`.
- Returns `false` in `Closed` and `Stocked` (unchanged).
- Returns `false` when no box holds the code.
- Case-insensitive matching still holds: existing `TransportBoxRepositoryCaseHandlingTests.IsBoxCodeActiveAsync_WithMixedCase_ShouldFindMatch` passes unmodified.
- The query is still a single `AnyAsync` round trip — no client-side evaluation, no `ToListAsync` materialisation.

### FR-3: `New → Opened` rejects codes held by quarantined or errored boxes

With FR-2 in place, `ChangeTransportBoxStateHandler.HandleNewToOpened` (`ChangeTransportBoxStateHandler.cs:213-247`) needs no logic change — it already calls `IsBoxCodeActiveAsync` and returns `ErrorCodes.TransportBoxDuplicateActiveBoxFound` (1405). This FR pins the resulting end-to-end behaviour so it is covered by tests.

The `Stocked`-cleanup step that follows the check (closing same-code `Stocked` boxes) stays exactly as it is.

**Acceptance criteria:**
- Assigning code `B001` to a `New` box while another box holds `B001` in `Quarantine` returns `Success = false` with `ErrorCode = TransportBoxDuplicateActiveBoxFound` and `Params["code"] == "B001"`; the `New` box's persisted `Code` remains `null` and its state remains `New`.
- Same for a box holding `B001` in `Error`.
- Assigning a code held only by a `Closed` box still succeeds (existing `TransportBoxUniquenessTests.OpenTransportBoxWithCodeThenCloseItThenOpenAnotherWithSameCode_ShouldSucceed` passes unmodified).
- Assigning a code held only by a `Stocked` box still succeeds and that `Stocked` box is transitioned to `Closed` (unchanged behaviour).
- Assigning an unused code to a `New` box still succeeds — no self-match against the in-flight box, whose `Code` is assigned in memory by `AssignBoxCodeIfAny` before the check runs but is not persisted (existing `TransportBoxUniquenessTests.OpenTwoTransportBoxesWithDifferentCodes_ShouldSucceed` passes unmodified).
- No frontend change is required: `TransportBoxDuplicateActiveBoxFound` is already mapped in `frontend/src/i18n.ts:155-156` to `"Box s číslem {code} již existuje a je stále aktivní"`.

### FR-4: `OpenOrResumeBoxByCodeHandler` derives from the shared rule

Replace the inline deny-list at `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/OpenOrResumeBoxByCode/OpenOrResumeBoxByCodeHandler.cs:62` (`existing.State != TransportBoxState.Closed && existing.State != TransportBoxState.Stocked`) with `TransportBoxStateRules.OccupiesCode(existing.State)`.

The three-branch structure is preserved verbatim: (1) `existing.State == Opened` → resume, (2) code occupied → `TransportBoxDuplicateActiveBoxFound` with `code` and `state` params, (3) otherwise → create and open a fresh box.

**Acceptance criteria:**
- Behaviour is byte-for-byte identical to today for every current enum value — this is a pure de-duplication, verified by the existing `OpenOrResumeBoxByCodeHandlerTests` suite passing unmodified.
- Scanning a code held by a `Quarantine`, `Error`, `Reserve`, `Received`, or `InTransit` box returns `TransportBoxDuplicateActiveBoxFound` with `Params["state"]` set to that state's name.
- Scanning a code held only by a `Closed` or `Stocked` box creates and opens a new box.
- Scanning a code held by an `Opened` box resumes it (`Resumed == true`) and creates nothing.

### FR-5: `GetByCodeAsync` resolves to the box that actually holds the code

`TransportBoxRepository.GetByCodeAsync` (`TransportBoxRepository.cs:117-131`) currently orders only `Closed` last, which places a `Stocked` box in the same rank as genuinely active boxes and lets `Id`-descending pick the wrong one. Re-order it on the same shared rule: code-occupying boxes first (`TransportBoxStateRules.CodeReleasingStates.Contains(o.State) ? 1 : 0`), then `Id` descending as today.

**Acceptance criteria:**
- Given a `Stocked` box with code `B001` (higher `Id`) and an `Opened` box with code `B001` (lower `Id`), `GetByCodeAsync("B001")` returns the `Opened` box.
- Given only terminal/released boxes for a code, the newest (`Id` descending) is still returned — no behaviour change when nothing occupies the code.
- Existing `TransportBoxRepositoryCaseHandlingTests.GetByCodeAsync_WithMixedCase_ShouldFindMatch` passes unmodified.
- `Include(x => x.Items)` / `Include(x => x.StateLog)` and the existing debug log are unchanged.
- Consumers `GetTransportBoxByCodeHandler` (`GetTransportBoxByCodeHandler.cs:42`) and `OpenOrResumeBoxByCodeHandler` (line 49) are not modified — they inherit the corrected resolution.

### FR-6: Drift guard on `TransportBoxState`

Add a test that fails when a member is added to or removed from `TransportBoxState` without a conscious decision about code occupancy. It asserts the exact expected membership of the enum and the exact partition into releasing / occupying sets, with a message telling the reader to classify the new state in `TransportBoxStateRules` rather than to just update the expected list.

**Acceptance criteria:**
- Adding a hypothetical eleventh member to `TransportBoxState` makes exactly this test fail, with an assertion message naming `TransportBoxStateRules.CodeReleasingStates`.
- Every member of `TransportBoxState` is covered by `OccupiesCode` — the test enumerates `Enum.GetValues<TransportBoxState>()` rather than a hard-coded call list.

### FR-7: Record the invariant in project memory

Add `memory/gotchas/transport-box-code-uniqueness-single-definition.md` documenting: the failure scenario, the rule that `TransportBoxStateRules` is the only place transport-box code occupancy may be defined, the two call sites that must consume it, and the read-only detection SQL for pre-existing duplicates:

```sql
SELECT "Code", COUNT(*), array_agg("Id"), array_agg("State")
FROM public."TransportBoxes"
WHERE "Code" IS NOT NULL AND "State" NOT IN ('Closed','Stocked')
GROUP BY "Code" HAVING COUNT(*) > 1;
```

This follows the repo's existing memory convention (`CLAUDE.md` § Memory) and mirrors `memory/gotchas/postgres-partial-index-active-states.md`, which established the "single source of truth keeps the handler and the schema in lockstep" rule for the same class of problem.

**Acceptance criteria:**
- The file exists, names the two consuming call sites by path, and contains the detection query verbatim.
- The query is read-only — no `UPDATE`, no `DELETE`, no DDL.

## Non-Functional Requirements

### NFR-1: Performance

`IsBoxCodeActiveAsync` remains a single `AnyAsync` with an `Id`-free predicate over `TransportBoxes`; the allow-list `IN (5 values)` becomes a deny-list `NOT IN (2 values)` on the same string-converted `State` column. `TransportBoxes` is a small, operator-scale table (hundreds to low thousands of rows) and there is no index on `Code` today; the plan shape is unchanged and no regression is expected. `GetByCodeAsync` gains one extra term in its `ORDER BY` `CASE` expression over an already-filtered single-code result set — negligible.

No new query is introduced anywhere. Target: box-code checks stay well under 50 ms p95, i.e. indistinguishable from today.

### NFR-2: Security

No change to the auth surface, no new endpoints, no new DTOs, no contract change, therefore no OpenAPI regeneration and no `frontend/src/api/generated/api-client.ts` churn. The change is strictly tighter than current behaviour — it rejects assignments that previously succeeded and never permits one that previously failed, so it cannot widen access.

### NFR-3: Backward compatibility and data safety

- No schema change, no EF migration, no data migration. Migrations in this repo are manual (`CLAUDE.md` § Project facts, `memory/gotchas/ef-migration-codebase-drift.md`), so a change that needs none is strictly preferable.
- Rows that already violate the invariant in production (created by the bug being fixed) are left untouched and continue to function; FR-5 makes barcode scans resolve to the occupying box rather than the newest one, which is the correct behaviour for exactly that corrupt data. FR-7's detection query lets the operator find them.
- The public repository interface `ITransportBoxRepository` is unchanged, so no consumer outside the Logistics slice is affected.

### NFR-4: Module boundaries

The rule lives in `Anela.Heblo.Domain/Features/Logistics/Transport/` and is consumed by `Anela.Heblo.Persistence` and `Anela.Heblo.Application`, both of which already reference Domain. No new project reference, no shared/global type, no cross-module coupling — consistent with `docs/architecture/development_guidelines.md` (Vertical Slice, module independence).

## Data Model

No changes.

Entities involved (unchanged): `TransportBox` (`Id`, `Code`, `State`, `Location`, `LastStateChanged`, audit fields, `ConcurrencyStamp`, `ExtraProperties`), owning `TransportBoxItem` and `TransportBoxStateLog` collections.

`TransportBoxState` partition introduced by this change (classification only — the enum itself is untouched):

| Partition | Members | Meaning |
| --- | --- | --- |
| Code-releasing | `Closed`, `Stocked` | Box no longer holds its code; the code may be assigned to another box. |
| Code-occupying (default) | `New`, `Opened`, `InTransit`, `Received`, `InSwap`, `Error`, `Reserve`, `Quarantine` | Box still holds its code; assigning it elsewhere is rejected. |

## API / Interface Design

No endpoint, request, or response shape changes.

Observable API behaviour changes, both tightenings:

| Flow | Endpoint | Before | After |
| --- | --- | --- | --- |
| Assign box code (`New → Opened`) while the code is held by a `Quarantine` or `Error` box | `ChangeTransportBoxState` (via `TransportBoxDetail.tsx` `handleBoxNumberSubmit`) | 200, `Success = true`, duplicate code created | 200, `Success = false`, `ErrorCode = TransportBoxDuplicateActiveBoxFound`, `Params["code"]` |
| Scan a code held by both an `Opened` box and a newer `Stocked` box | `GetTransportBoxByCode`, `OpenOrResumeBoxByCode` | returns the `Stocked` box | returns the `Opened` box |

New internal interface (Domain, not exposed over HTTP):

```csharp
public static class TransportBoxStateRules
{
    public static readonly TransportBoxState[] CodeReleasingStates;
    public static bool OccupiesCode(TransportBoxState state);
    public static readonly Expression<Func<TransportBox, bool>> OccupiesCodePredicate;
}
```

## Dependencies

None beyond what is already in the solution. No new NuGet packages, no npm packages, no external services, no feature flag. EF Core's translation of `Contains` over a static readonly array against a `HasConversion<string>()` enum column is already relied on by the current `IsBoxCodeActiveAsync` implementation, so it is proven in this codebase.

## Testing

All backend, in `backend/test/Anela.Heblo.Tests/`, following the existing file layout:

- `Domain/Logistics/TransportBoxStateRulesTests.cs` (new) — FR-1 classification per enum value, predicate/function agreement, and the FR-6 drift guard.
- `Domain/Logistics/TransportBoxUniquenessTests.cs` (extend) — `Quarantine` and `Error` now block `New → Opened`; `Stocked` and `Closed` still do not. This file already exercises the real `TransportBoxRepository` over an in-memory `ApplicationDbContext` with the real `ChangeTransportBoxStateHandler`, which is the right level for FR-3.
- `Repositories/TransportBoxRepositoryCaseHandlingTests.cs` (extend) or a sibling repository test file — FR-2 per-state truth table for `IsBoxCodeActiveAsync`, and the FR-5 `GetByCodeAsync` ordering case (`Stocked` with higher `Id` vs `Opened` with lower `Id`).
- `Features/Logistics/Transport/OpenOrResumeBoxByCodeHandlerTests.cs` (extend) — FR-4 busy-state coverage for `Quarantine`, `Error`, `Reserve`, `Received` alongside the existing `InTransit` case.

Existing tests that must pass **without modification** (they encode the behaviour this change must not break): `TransportBoxUniquenessTests` (all 5), `OpenOrResumeBoxByCodeHandlerTests` (all), `TransportBoxRepositoryCaseHandlingTests` (all), `ChangeTransportBoxStateHandlerTests` (all — it mocks `ITransportBoxRepository`, so FR-2 is invisible to it), `GetTransportBoxByCodeHandlerTests` (all).

To build a box in `Error` state in tests, use `TransportBox.Error(date, user, message)` — it accepts any source state (`TransportBox.cs:259-262`). For `Quarantine`, use `Open(...)` then `ToQuarantine(...)`.

**Validation before completion** (per `CLAUDE.md`): `dotnet build`, `dotnet format`, and the full `Anela.Heblo.Tests` suite. No frontend files are touched, so `npm run build` / `npm run lint` and the E2E suite are not required for this change.

## Out of Scope

- **DB-level partial unique index on `Code`.** A `CREATE UNIQUE INDEX ... WHERE "State" NOT IN ('Closed','Stocked')` would close the TOCTOU race between the check and the save, but migrations in this repo are applied manually and out of band (`memory/gotchas/ef-migration-codebase-drift.md`), and production may already contain duplicate rows produced by the bug being fixed — the index creation would fail on apply, in an environment where that failure is discovered late. Ship the application-layer unification first, use FR-7's detection query to confirm the table is clean, then file a follow-up for the constraint. The concurrency window is two operators assigning the same code within the same few milliseconds on a single-warehouse system, which is not the failure being reported here.
- **The `isActiveFilter` in `GetPagedListAsync`** (`TransportBoxRepository.cs:36-40`, `State != Closed`). This is a UI list filter — "show me everything that isn't archived" — not the code-uniqueness invariant, and it deliberately shows `Stocked` boxes. Unifying it would silently change what the transport box list displays. Leave it alone.
- **`isReceivable` in `GetTransportBoxByCodeHandler`** (`{InTransit, Reserve, Quarantine}`, `GetTransportBoxByCodeHandler.cs:51-54`). A different concept (which states may be received), correctly stated, out of scope.
- **Renaming `IsBoxCodeActiveAsync`** to something matching the new vocabulary (e.g. `IsBoxCodeOccupiedAsync`). Cosmetic; would churn the interface and four test files.
- **Removing the unused `InSwap` state.** Noted as dead (enum + i18n labels only, no backend producer or consumer) but not touched — per `CLAUDE.md`, unrelated dead code is reported, not deleted.
- **The `Contains`-based code match in `HandleNewToOpened`'s `Stocked` cleanup** (`GetPagedListAsync(code: request.BoxCode, ...)` filters with `Code.Contains(...)`, not equality — `TransportBoxRepository.cs:31-34`). Harmless for the fixed `B` + 3-digit code format, but noted here as an observed inconsistency for a future pass.
- Any frontend change. Any change to the transport box state machine, transitions, or `TransportBoxState` membership.

## Open Questions

None.

## Status: COMPLETE
