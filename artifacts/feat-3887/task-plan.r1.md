# Unify Transport Box Code Uniqueness Into a Single State Rule — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the two contradictory, hand-written definitions of "this transport-box state still holds its code" (an allow-list in `TransportBoxRepository.IsBoxCodeActiveAsync`, a deny-list in `OpenOrResumeBoxByCodeHandler`) with one domain-level rule type, `TransportBoxStateRules`, that both call sites — plus `GetByCodeAsync`'s resolution ordering — consume by reference. This fixes the reported bug (a `Quarantine` or `Error` box's code can currently be reassigned, producing two live rows with the same `Code` and silently mis-routing every subsequent barcode scan) and adds a drift guard so the next `TransportBoxState` member cannot silently reopen the hole.

**Architecture:** One new static type in `Anela.Heblo.Domain/Features/Logistics/Transport/`, following the static-predicate convention already on the same aggregate (`TransportBox.IsInTransportPredicate` / `IsInReservePredicate` / `IsInQuarantinePredicate`, `TransportBox.cs:39-50`). It exposes exactly two public members — `OccupiesCode(TransportBoxState)` for in-memory checks and `OccupiesCodePredicate` (an `Expression<Func<TransportBox,bool>>`) for EF composition — over a **private** backing array of the two code-releasing states (`Closed`, `Stocked`). Persistence and Application both already reference Domain; no new project reference, no new package, no schema change, no EF migration, no contract change, no frontend change.

**Binding amendments:** This plan implements the **amended** spec. `arch-review.r1.md` amendments A1 (private array; both repository call sites compose the predicate, not the array), A2 (mandatory PostgreSQL SQL-shape test), A3 (do not assert "persisted Code remains null" against the shared tracked context), A4 (cascade coverage + fix the stale comment), A5 (correct the array rationale), A6 (`New` never carries a persisted code), A7 (scope the "no other deny-list" criterion to code uniqueness), A8 (follow-up for the DB partial unique index), A9 (name the unavoidable SQL duplication) are all folded into the tasks below. Where the un-amended `spec.r1.md` differs, `design.r1.md` and this plan win.

**Tech Stack:** .NET 8, C#, EF Core 8 + Npgsql, xUnit, FluentAssertions, Moq, Testcontainers (`postgres:16` via `PostgresSharedContainerFixture`).

**Task order and dependencies:**

```
add-transport-box-state-rules          (no deps — everything else consumes it)
  ├── consume-rule-in-transport-box-repository
  │     ├── cover-new-to-opened-code-occupancy      (depends on repository change)
  │     └── add-code-occupancy-sql-shape-test       (depends on repository change)
  └── consume-rule-in-open-or-resume-handler
document-code-uniqueness-invariant     (docs only — can run last, independent)
```

**Repo-wide validation gate (`CLAUDE.md` § Validation before completion).** Backend-only change, so `dotnet build` + `dotnet format` + all touched tests. No frontend file changes ⇒ `npm run build` / `npm run lint` and the Playwright E2E suite are **not** required. Note that PR CI runs `--filter "Category!=Playwright&Category!=Integration"` (`.github/workflows/ci-feature-branch.yml:93`), so the `Category=Integration` test added by `add-code-occupancy-sql-shape-test` will **not** run in CI — it must be run locally with Docker available before declaring the work done.

---

### task: add-transport-box-state-rules

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxStateRules.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/ITransportBoxRepository.cs:19`
- Create (test): `backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxStateRulesTests.cs`

#### Goal

Introduce the single, domain-level definition of transport-box code occupancy (FR-1) and the drift guard that makes adding a new `TransportBoxState` a deliberate decision (FR-6). No behaviour changes yet — this task adds the type and its tests only; the three call sites are rewired in the two tasks that follow.

#### Context

- `TransportBoxState` (`backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxState.cs`) has exactly ten members, in this declaration order: `New`, `Opened`, `InTransit`, `Received`, `InSwap`, `Stocked`, `Closed`, `Error`, `Reserve`, `Quarantine`.
- The canonical rule is a **deny-list**: only `Closed` and `Stocked` release a code; every other state — present or future — occupies it. This is the fail-safe direction, and its absence is what caused the bug.
- `TransportBox.cs` already carries `using System.Linq.Expressions;` and hosts three sibling static predicates (`TransportBox.cs:39-50`), so this is an established convention in this file's namespace, not a new pattern.
- **Amendment A1 is binding:** the backing array is `private`. `public static readonly T[]` is only shallowly readonly — any assembly in the solution could overwrite an element and silently reopen this bug. The public surface is exactly `OccupiesCode` + `OccupiesCodePredicate`, and both have a real consumer by the end of this feature.
- **Amendment A5 is binding:** the spec's stated reason for using an array ("so EF Core translates `Contains`") is **false** for EF Core 8 — `List<T>`, `HashSet<T>` and `IEnumerable<T>` all translate. Keep the array, but for the real reason: consistency with the current implementation and with the `private static readonly int[] ActiveStates` precedent in `memory/gotchas/postgres-partial-index-active-states.md`. Do not carry the false constraint into a comment.
- `Anela.Heblo.Domain.csproj` carries `<InternalsVisibleTo Include="Anela.Heblo.Tests" />`, so the test *could* reach an `internal` array. It must not — assert only through the public surface (Decision 3).
- `ITransportBoxRepository` currently has **no XML documentation at all**. The name `IsBoxCodeActiveAsync` is deliberately kept (renaming would churn the interface plus four test files for zero behavioural gain), but it now actively misleads: `Error` and `Quarantine` are not colloquially "active" yet do occupy the code. The doc therefore goes on the **interface** — what every caller sees — not only on the implementation (Decision 4).

#### Implementation steps

- [ ] **Step 1: Create `TransportBoxStateRules.cs`**

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
    // and silently reopen this bug. Kept as an array for consistency with the previous
    // implementation and with memory/gotchas/postgres-partial-index-active-states.md's
    // `private static readonly int[] ActiveStates` precedent.
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

Declare `CodeReleasingStates` textually **before** `OccupiesCodePredicate`. The expression tree captures the field by member access rather than by value so ordering is not strictly load-bearing, but relying on that is unnecessary subtlety.

- [ ] **Step 2: Add the XML doc to `ITransportBoxRepository.IsBoxCodeActiveAsync`**

In `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/ITransportBoxRepository.cs`, replace line 19 (`Task<bool> IsBoxCodeActiveAsync(string boxCode);`) with:

```csharp
    /// <summary>
    /// True when any box currently occupies <paramref name="boxCode"/> — i.e. holds it in a
    /// state for which <see cref="TransportBoxStateRules.OccupiesCode"/> is true. Matching is
    /// case-insensitive. The name predates the rule; "active" here means "occupying the code",
    /// which includes Error and Quarantine.
    /// </summary>
    Task<bool> IsBoxCodeActiveAsync(string boxCode);
```

The signature is unchanged. Do not touch any other member of the interface.

- [ ] **Step 3: Create `TransportBoxStateRulesTests.cs`**

New file `backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxStateRulesTests.cs`, namespace `Anela.Heblo.Tests.Domain.Logistics` (matches the sibling `TransportBoxUniquenessTests.cs` / `TransportBoxCodeCaseHandlingTests.cs` in that folder).

Three tests, all asserting **only** through the public surface — never against `CodeReleasingStates`, which is private by design:

1. **Drift guard (FR-6).** An exhaustive hard-coded expected map keyed by every current enum member, then iterate `Enum.GetValues<TransportBoxState>()` and look each value up in the map. An eleventh member must fail on a **missing key**, not be silently skipped. Sketch:

```csharp
private static readonly IReadOnlyDictionary<TransportBoxState, bool> ExpectedOccupancy =
    new Dictionary<TransportBoxState, bool>
    {
        [TransportBoxState.New] = true,
        [TransportBoxState.Opened] = true,
        [TransportBoxState.InTransit] = true,
        [TransportBoxState.Received] = true,
        [TransportBoxState.InSwap] = true,
        [TransportBoxState.Stocked] = false,
        [TransportBoxState.Closed] = false,
        [TransportBoxState.Error] = true,
        [TransportBoxState.Reserve] = true,
        [TransportBoxState.Quarantine] = true,
    };

[Fact]
public void EveryTransportBoxState_IsClassifiedByOccupiesCode()
{
    foreach (var state in Enum.GetValues<TransportBoxState>())
    {
        ExpectedOccupancy.Should().ContainKey(state,
            "TransportBoxState.{0} is new. Do not just add it to this map — decide whether it " +
            "releases the transport box code and classify it in " +
            "TransportBoxStateRules.CodeReleasingStates first. The deny-list default is that a " +
            "new state OCCUPIES its code (issue #3887).", state);

        TransportBoxStateRules.OccupiesCode(state).Should().Be(ExpectedOccupancy[state],
            "TransportBoxStateRules.CodeReleasingStates must classify {0} as {1}",
            state, ExpectedOccupancy[state] ? "code-occupying" : "code-releasing");
    }
}
```

2. **Releasing set is exactly `{Closed, Stocked}`.** Assert `OccupiesCode(Closed)` and `OccupiesCode(Stocked)` are `false` and that every other enum value is `true` — derived from `Enum.GetValues<TransportBoxState>()`, not from a hard-coded list.

3. **Predicate/function agreement (FR-1).** For every value of `Enum.GetValues<TransportBoxState>()`, `OccupiesCodePredicate.Compile()(box)` must equal `OccupiesCode(state)` for a `TransportBox` whose `State` is that value.

   `TransportBox.State` has a **private setter** and several enum values (notably `InSwap`) are unreachable through the aggregate's guarded transitions, so build the probe by forcing the property via reflection — `PropertyInfo.SetValue` alone throws for a private setter, so go through the non-public set method:

```csharp
private static TransportBox BoxInState(TransportBoxState state)
{
    var box = new TransportBox();
    typeof(TransportBox)
        .GetProperty(nameof(TransportBox.State))!
        .GetSetMethod(nonPublic: true)!
        .Invoke(box, new object[] { state });
    return box;
}
```

   Compile the predicate **once** into a static/local `Func<TransportBox, bool>` and reuse it across the loop; do not call `.Compile()` inside the loop body.

- [ ] **Step 4: Run the new tests**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxStateRulesTests"
```

Expected: all PASS.

- [ ] **Step 5: Build and format**

```bash
cd backend && dotnet build && dotnet format
```

Expected: build succeeds with no new warnings; `dotnet format` makes no changes beyond whitespace in the new files.

#### Acceptance criteria

- `TransportBoxStateRules.OccupiesCode` returns `false` for `Closed` and `Stocked`, `true` for `New`, `Opened`, `InTransit`, `Received`, `InSwap`, `Error`, `Reserve`, `Quarantine`.
- `OccupiesCodePredicate.Compile()` agrees with `OccupiesCode` for every one of the ten enum values.
- `CodeReleasingStates` is `private` — `grep -n "CodeReleasingStates" backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxStateRules.cs` shows it declared `private static readonly` and referenced only inside that file.
- No test references `CodeReleasingStates` as a member; the drift guard asserts through `OccupiesCode` only.
- The type lives in `Anela.Heblo.Domain` and takes no dependency on Application or Persistence — no new `using` beyond `System.Linq.Expressions`, no new `ProjectReference` in `Anela.Heblo.Domain.csproj`.
- `ITransportBoxRepository.IsBoxCodeActiveAsync` carries the XML summary above; its signature is byte-identical to before.
- `dotnet build` and `dotnet format` succeed with no new warnings.

#### Tests to run

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxStateRulesTests"
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBox&Category!=Integration"
```

The second command is a regression sweep — every existing transport-box test must still pass, since this task changes no behaviour.

---

### task: consume-rule-in-transport-box-repository

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Logistics/TransportBoxes/TransportBoxRepository.cs` — `IsBoxCodeActiveAsync` (lines 96-115), `GetByCodeAsync` (lines 117-131)
- Modify (test): `backend/test/Anela.Heblo.Tests/Repositories/TransportBoxRepositoryCaseHandlingTests.cs`

**Depends on:** `add-transport-box-state-rules` (the rule type must exist).

#### Goal

Make both code-lookup paths in the repository derive from `TransportBoxStateRules` instead of restating the partition (FR-2, FR-5, amendment A1). This is where the reported bug is actually fixed: `IsBoxCodeActiveAsync` starts reporting `Quarantine` and `Error` boxes as occupying their code, and `GetByCodeAsync` starts resolving a scanned code to the box that actually holds it rather than to the newest non-`Closed` row.

#### Context

Current code (`TransportBoxRepository.cs:96-131`):

```csharp
public async Task<bool> IsBoxCodeActiveAsync(string boxCode)
{
    var activeStates = new[]
    {
        TransportBoxState.New,
        TransportBoxState.Opened,
        TransportBoxState.InTransit,
        TransportBoxState.Received,
        TransportBoxState.Reserve,
    };

    var upperBoxCode = boxCode.ToUpper();
    var exists = await DbSet
        .Where(x => x.Code == upperBoxCode && activeStates.Contains(x.State))
        .AnyAsync();
    ...
}

public async Task<TransportBox?> GetByCodeAsync(string boxCode)
{
    var upperBoxCode = boxCode.ToUpper();
    var transportBox = await DbSet
        .Include(x => x.Items)
        .Include(x => x.StateLog)
        .OrderBy(o => o.State == TransportBoxState.Closed ? 1 : 0)
        .ThenByDescending(o => o.Id)
        .FirstOrDefaultAsync(x => x.Code == upperBoxCode);
    ...
}
```

- `activeStates` is the drifted allow-list — `Quarantine` and `Error` are missing, which is the bug.
- `GetByCodeAsync` currently ranks a `Stocked` box equally with genuinely occupying boxes (only `Closed` is demoted), so `Id`-descending can pick the wrong aggregate.
- `State` is mapped `HasConversion<string>()` (`TransportBoxConfiguration.cs:16-17`), so the emitted SQL compares the `"State"` column against string literals/parameters.
- **Amendment A1 is binding:** compose `TransportBoxStateRules.OccupiesCodePredicate` directly. Do **not** hand-write `!CodeReleasingStates.Contains(...)` or a `? 1 : 0` restatement — that would be two fresh restatements of the invariant in the layer that had the bug.
- `OrderByDescending` binds `TKey = bool` when handed an `Expression<Func<TransportBox, bool>>`; PostgreSQL sorts `false < true`, so `DESC` puts code-occupying boxes first — exactly the intent, without restating the rule.
- Note the **restructure** from `FirstOrDefaultAsync(predicate)` to `.Where(...).FirstOrDefaultAsync()`: the code filter must be composed *before* the ordering so the `ORDER BY` applies to the already-filtered single-code set.
- **Amendment A7 / spec § Out of Scope:** `GetPagedListAsync`'s `isActiveFilter` (`x.State != TransportBoxState.Closed`, line 39) is a **UI list filter** that deliberately shows `Stocked` boxes, and `GetReceivedBoxesAsync` / `GetStateSummaryAsync` reason about different concepts. They must remain untouched. Only the two methods above may change in this file.

#### Implementation steps

- [ ] **Step 1: Add the failing InMemory tests first**

Add to `backend/test/Anela.Heblo.Tests/Repositories/TransportBoxRepositoryCaseHandlingTests.cs`.

**Critical seeding note (this supersedes the design doc's "seed on distinct codes" phrasing):** do **not** add boxes to `SeedTestData()`. That method seeds `B001`/`B123`/`B999` and the existing theory `GetPagedListAsync_WithCodeFilter_ShouldBeCaseInsensitive` asserts `"B" → 3` / `"b" → 3` — *any* extra box seeded there breaks it, because every valid box code starts with `B`. xUnit constructs a fresh test-class instance (and therefore a fresh `Guid.NewGuid()` InMemory database) per test method, so seed the new fixtures **inside the new test methods** and the existing six theories keep their exact expected counts.

Helper builders to add to the test class (all transitions verified against `TransportBox.cs`):

```csharp
private TransportBox NewBoxWithCode(string code)      // AssignBoxCodeIfAny requires State == New
{
    var box = new TransportBox();
    box.AssignBoxCodeIfAny(code);                     // does NOT upper-case: pass upper-case in
    return box;
}
private TransportBox OpenedBox(string code)           { var b = new TransportBox(); b.Open(code, _testDate, TestUser); return b; }
private TransportBox InTransitBox(string code)        { var b = OpenedBox(code); b.AddItem("P-1", "P", 1, _testDate, TestUser); b.ToTransit(_testDate, TestUser); return b; }
private TransportBox ReceivedBox(string code)         { var b = InTransitBox(code); b.Receive(_testDate, TestUser); return b; }
private TransportBox StockedBox(string code)          { var b = ReceivedBox(code); b.ToPick(_testDate, TestUser); return b; }
private TransportBox ClosedBox(string code)           { var b = StockedBox(code); b.Close(_testDate, TestUser); return b; }
private TransportBox ReserveBox(string code)          { var b = OpenedBox(code); b.ToReserve(_testDate, TestUser, "L1"); return b; }
private TransportBox QuarantineBox(string code)       { var b = OpenedBox(code); b.ToQuarantine(_testDate, TestUser); return b; }
private TransportBox ErrorBox(string code)            { var b = OpenedBox(code); b.Error(_testDate, TestUser, "boom"); return b; }
```

`InSwap` is unreachable through the aggregate and is covered by `TransportBoxStateRulesTests` — omit it from the repository truth table rather than reflecting into the entity here; this file tests the *query*, not the classification.

Tests to add:

```csharp
[Fact] IsBoxCodeActiveAsync_QuarantineBox_ReturnsTrue          // THE BUG FIX — fails before the change
[Fact] IsBoxCodeActiveAsync_ErrorBox_ReturnsTrue               // THE BUG FIX — fails before the change
[Fact] IsBoxCodeActiveAsync_NewBoxWithCode_ReturnsTrue
[Fact] IsBoxCodeActiveAsync_OpenedBox_ReturnsTrue
[Fact] IsBoxCodeActiveAsync_InTransitBox_ReturnsTrue
[Fact] IsBoxCodeActiveAsync_ReceivedBox_ReturnsTrue
[Fact] IsBoxCodeActiveAsync_ReserveBox_ReturnsTrue
[Fact] IsBoxCodeActiveAsync_StockedBox_ReturnsFalse
[Fact] IsBoxCodeActiveAsync_ClosedBox_ReturnsFalse
[Fact] IsBoxCodeActiveAsync_CodeHeldByNobody_ReturnsFalse      // e.g. "B777"
```

Each seeds exactly one box on a code not used by `SeedTestData` (e.g. `B500`…`B509`) via `_context.TransportBoxes.Add(...)` + `await _context.SaveChangesAsync()`, then asserts `await _repository.IsBoxCodeActiveAsync(<code>)`.

Plus the FR-5 ordering test:

```csharp
[Fact]
public async Task GetByCodeAsync_StockedBoxWithHigherId_ReturnsOccupyingOpenedBox()
{
    // Opened box saved FIRST so it gets the LOWER Id; Stocked box saved second (higher Id).
    var opened = OpenedBox("B510");
    _context.TransportBoxes.Add(opened);
    await _context.SaveChangesAsync();

    var stocked = StockedBox("B510");
    _context.TransportBoxes.Add(stocked);
    await _context.SaveChangesAsync();

    stocked.Id.Should().BeGreaterThan(opened.Id, "the test's premise is that the released box is newer");

    var found = await _repository.GetByCodeAsync("B510");

    found.Should().NotBeNull();
    found!.Id.Should().Be(opened.Id);
    found.State.Should().Be(TransportBoxState.Opened);
}

[Fact]
public async Task GetByCodeAsync_OnlyReleasedBoxes_ReturnsNewest()
{
    // No occupying box: Id-descending still wins, i.e. no behaviour change for released-only data.
    // Seed a Closed box then a Stocked box on the same code; expect the Stocked (higher Id) one.
}
```

- [ ] **Step 2: Run them and confirm the bug-fix tests fail against current code**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxRepositoryCaseHandlingTests"
```

Expected: `IsBoxCodeActiveAsync_QuarantineBox_ReturnsTrue`, `IsBoxCodeActiveAsync_ErrorBox_ReturnsTrue` and `GetByCodeAsync_StockedBoxWithHigherId_ReturnsOccupyingOpenedBox` **FAIL**. Everything else passes.

- [ ] **Step 3: Rewrite `IsBoxCodeActiveAsync`**

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

Delete the local `activeStates` array entirely. Keep the `boxCode.ToUpper()` normalisation, the single-`AnyAsync`-round-trip shape, and the debug log line exactly as they are.

- [ ] **Step 4: Rewrite `GetByCodeAsync`**

```csharp
public async Task<TransportBox?> GetByCodeAsync(string boxCode)
{
    var upperBoxCode = boxCode.ToUpper();
    var transportBox = await DbSet
        .Include(x => x.Items)
        .Include(x => x.StateLog)
        .Where(x => x.Code == upperBoxCode)
        .OrderByDescending(TransportBoxStateRules.OccupiesCodePredicate)  // occupying boxes first
        .ThenByDescending(o => o.Id)
        .FirstOrDefaultAsync();

    _logger.LogDebug("Retrieved transport box by code {BoxCode}: {Found}",
        boxCode, transportBox != null);

    return transportBox;
}
```

`Include(x => x.Items)` / `Include(x => x.StateLog)` and the debug log are unchanged. Add `using Anela.Heblo.Domain.Features.Logistics.Transport;` only if not already present — it is (line 1).

- [ ] **Step 5: Re-run the repository tests**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxRepositoryCaseHandlingTests"
```

Expected: **all** PASS, including the six pre-existing mixed-case theories, which must not have been modified.

- [ ] **Step 6: Regression sweep across the transport-box area**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBox&Category!=Integration"
```

Expected: all PASS. `ChangeTransportBoxStateHandlerTests` mocks `ITransportBoxRepository`, so this change is invisible to it; `GetTransportBoxByCodeHandlerTests` and `TransportBoxCodeCaseHandlingTests` must pass unmodified.

- [ ] **Step 7: Build and format**

```bash
cd backend && dotnet build && dotnet format
```

#### Acceptance criteria

- `IsBoxCodeActiveAsync` returns `true` for a box holding the code in `Quarantine` (previously `false` — the bug fix) and in `Error`; still `true` for `New`, `Opened`, `InTransit`, `Received`, `Reserve`; still `false` for `Closed`, `Stocked`, and for a code no box holds.
- Case-insensitive matching still holds — `IsBoxCodeActiveAsync_WithMixedCase_ShouldFindMatch` and `GetByCodeAsync_WithMixedCase_ShouldFindMatch` pass **unmodified**.
- `IsBoxCodeActiveAsync` is still a single `AnyAsync` round trip — no `ToListAsync` materialisation, no client-side evaluation.
- `GetByCodeAsync` returns the `Opened` box when a `Stocked` box with a higher `Id` shares the code; when only released boxes hold the code, the newest (`Id` desc) is still returned.
- `grep -n "TransportBoxState.Closed\|TransportBoxState.Stocked" backend/src/Anela.Heblo.Persistence/Logistics/TransportBoxes/TransportBoxRepository.cs` returns **only** the `isActiveFilter` line (`x.State != TransportBoxState.Closed`, ~line 39) — no literal state comparison survives inside `IsBoxCodeActiveAsync` or `GetByCodeAsync`.
- `GetPagedListAsync`, `GetReceivedBoxesAsync`, `GetStateSummaryAsync`, `FindAsync`, `GetByIdWithDetailsAsync` are byte-identical to before.
- `dotnet build` and `dotnet format` succeed with no new warnings.

#### Tests to run

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxRepositoryCaseHandlingTests"
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBox&Category!=Integration"
```

---

### task: consume-rule-in-open-or-resume-handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/OpenOrResumeBoxByCode/OpenOrResumeBoxByCodeHandler.cs` — line 62 predicate, lines 68-69 comment
- Modify (test): `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/OpenOrResumeBoxByCodeHandlerTests.cs`

**Depends on:** `add-transport-box-state-rules`. Independent of the repository task (the handler test mocks `ITransportBoxRepository`), but the corrected comment describes behaviour that `consume-rule-in-transport-box-repository` delivers.

#### Goal

De-duplicate the handler's inline deny-list into the shared rule (FR-4) and correct the stale comment that documents a guarantee the code did not previously provide (amendment A4). This is a **pure de-duplication** — behaviour is byte-for-byte identical for every current enum value.

#### Context

Current code (`OpenOrResumeBoxByCodeHandler.cs:61-69`):

```csharp
            // A box with this code is busy in a non-resumable state.
            if (existing != null && existing.State != TransportBoxState.Closed && existing.State != TransportBoxState.Stocked)
            {
                return new OpenOrResumeBoxByCodeResponse(ErrorCodes.TransportBoxDuplicateActiveBoxFound,
                    new Dictionary<string, string> { { "code", code }, { "state", existing.State.ToString() } });
            }

            // No box, or only a Closed/Stocked box with this code — create and open a fresh one.
            // GetByCodeAsync returns any active box first, so reaching here means none exists.
```

- The three-branch structure must be preserved verbatim: (1) `existing.State == Opened` → resume (line 52), (2) code occupied → `TransportBoxDuplicateActiveBoxFound` with `code` and `state` params, (3) otherwise → create and open a fresh box. The `code` normalisation at line 44 and every `catch` block are unchanged.
- The comment at line 69 — *"GetByCodeAsync returns any active box first, so reaching here means none exists"* — is **false today** and becomes true only because of the `GetByCodeAsync` re-ordering in `consume-rule-in-transport-box-repository`. Amendment A4 requires restating it so it names its source of truth.
- The cascade this closes, all three steps reachable today: box #5 sits in `Quarantine` holding `B001`; an operator assigns `B001` to box #20 from the admin UI (the reported bug) and #20 runs through to `Stocked`; a terminal scan of `B001` ranks #5 and #20 equally (neither is `Closed`), `Id` desc picks #20, and branch 3 mints a **third** row holding `B001`.
- `ErrorCodes.TransportBoxDuplicateActiveBoxFound` is 1405 (`backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs:153`), already mapped in `frontend/src/i18n.ts:155-156` — no frontend change.

#### Implementation steps

- [ ] **Step 1: Replace the inline deny-list (line 62)**

```csharp
            // A box with this code is busy in a non-resumable state.
            if (existing != null && TransportBoxStateRules.OccupiesCode(existing.State))
            {
                return new OpenOrResumeBoxByCodeResponse(ErrorCodes.TransportBoxDuplicateActiveBoxFound,
                    new Dictionary<string, string> { { "code", code }, { "state", existing.State.ToString() } });
            }
```

`using Anela.Heblo.Domain.Features.Logistics.Transport;` is already present (line 4).

- [ ] **Step 2: Correct the comment (amendment A4)**

```csharp
            // No box, or only a Closed/Stocked box with this code — create and open a fresh one.
            // GetByCodeAsync orders on TransportBoxStateRules.OccupiesCodePredicate, so any
            // code-occupying box outranks a released one; reaching here means none exists.
```

- [ ] **Step 3: Extend `OpenOrResumeBoxByCodeHandlerTests`**

Add these builders alongside the existing `OpenedBox` / `ClosedBox` / `InTransitBox` / `StockedBox` helpers (transitions verified against `TransportBox.cs`):

```csharp
private static TransportBox ReceivedBox(string code)    { var b = InTransitBox(code); b.Receive(FixedTime, "Test User"); return b; }
private static TransportBox ReserveBox(string code)     { var b = OpenedBox(code); b.ToReserve(FixedTime, "Test User", "L1"); return b; }
private static TransportBox QuarantineBox(string code)  { var b = OpenedBox(code); b.ToQuarantine(FixedTime, "Test User"); return b; }
private static TransportBox ErrorBox(string code)       { var b = OpenedBox(code); b.Error(FixedTime, "Test User", "boom"); return b; }
```

Add busy-state coverage for `Quarantine`, `Error`, `Reserve` and `Received` alongside the existing `Handle_BoxBusyInTransit_ReturnsDuplicateActiveBoxFound`. Each mocks `GetByCodeAsync("B001")` to return the corresponding box and asserts:

- `result.Success` is `false`
- `result.ErrorCode == ErrorCodes.TransportBoxDuplicateActiveBoxFound`
- `result.Params["state"]` equals that state's `ToString()` (`"Quarantine"`, `"Error"`, `"Reserve"`, `"Received"`)
- `result.Params["code"] == "B001"`
- `AddAsync` and `SaveChangesAsync` are each verified `Times.Never`

Plus the **A4 cascade test**: with `GetByCodeAsync` mocked to return the `Quarantine` box — which is what the fixed repository now returns for a code shared by a lower-`Id` `Quarantine` box and a higher-`Id` `Stocked` box — the handler returns `TransportBoxDuplicateActiveBoxFound` with `Params["state"] == "Quarantine"` and creates nothing. Name it so its intent survives, e.g. `Handle_QuarantineBoxResolvedOverNewerStockedBox_DoesNotMintThirdBox`.

Do **not** modify any existing test in this file — FR-4 is a pure de-duplication and the existing suite passing unchanged is the proof.

- [ ] **Step 4: Run the handler tests**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~OpenOrResumeBoxByCodeHandlerTests"
```

Expected: all PASS, old and new. The new busy-state tests pass both before and after the line-62 edit — that is the point; they pin the equivalence.

- [ ] **Step 5: Build and format**

```bash
cd backend && dotnet build && dotnet format
```

#### Acceptance criteria

- Scanning a code held by a `Quarantine`, `Error`, `Reserve`, `Received` or `InTransit` box returns `TransportBoxDuplicateActiveBoxFound` with `Params["state"]` set to that state's name and `Params["code"]` set to the normalised code; no box is created.
- Scanning a code held only by a `Closed` or `Stocked` box creates and opens a new box (`Resumed == false`).
- Scanning a code held by an `Opened` box resumes it (`Resumed == true`) and calls neither `AddAsync` nor `SaveChangesAsync`.
- Every pre-existing test in `OpenOrResumeBoxByCodeHandlerTests` passes **unmodified**.
- `grep -n "TransportBoxState.Closed\|TransportBoxState.Stocked" backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/OpenOrResumeBoxByCode/OpenOrResumeBoxByCodeHandler.cs` returns nothing (the `Opened` comparison at line 52 stays — it is the resume branch, not the occupancy rule).
- The line-68/69 comment names `TransportBoxStateRules.OccupiesCodePredicate` as the source of the ordering guarantee.
- `dotnet build` and `dotnet format` succeed with no new warnings.

#### Tests to run

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~OpenOrResumeBoxByCodeHandlerTests"
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBox&Category!=Integration"
```

---

### task: cover-new-to-opened-code-occupancy

**Files:**
- Modify (test): `backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxUniquenessTests.cs`
- No production file changes.

**Depends on:** `consume-rule-in-transport-box-repository`.

#### Goal

Pin the end-to-end consequence of the repository fix on the `New → Opened` path (FR-3) — the path the admin UI drives and the one that carries the reported bug. `ChangeTransportBoxStateHandler.HandleNewToOpened` needs **no code change**; it already calls `IsBoxCodeActiveAsync` and returns `TransportBoxDuplicateActiveBoxFound`. This task is tests only.

#### Context

- `TransportBoxUniquenessTests` wires the **real** `TransportBoxRepository` over an in-memory `ApplicationDbContext` into the **real** `ChangeTransportBoxStateHandler` (constructor, lines 31-67) — the right level for FR-3. The database name is inlined as `Guid.NewGuid().ToString()` at line 34.
- `HandleNewToOpened` (`ChangeTransportBoxStateHandler.cs:214-247`): checks `string.IsNullOrEmpty(request.BoxCode)`, normalises with `ToUpper()`, calls `IsBoxCodeActiveAsync`, and on `true` returns `Success = false` / `ErrorCode = TransportBoxDuplicateActiveBoxFound` / `Params { { "code", normalizedCode } }`. On `false` it closes same-code `Stocked` boxes and continues. Leave all of it alone.
- **Amendment A3 is binding.** Do **not** assert *"the `New` box's persisted `Code` remains `null`"*. `box.AssignBoxCodeIfAny(code)` mutates the tracked entity at `ChangeTransportBoxStateHandler.cs:71` — *before* the guard runs — so the shared `ApplicationDbContext`'s tracked instance legitimately carries the rejected code, and a **correct** implementation would fail that assertion. Assert the response only. If persistence must be asserted, re-read through a **second `ApplicationDbContext` bound to the same InMemory database name**, before any further `SaveChangesAsync` on the original context; to do that, capture the database name into a field instead of inlining it at line 34. Production is unaffected — the context is request-scoped and no `IPipelineBehavior` in `Application/Common/Behaviors/` calls `SaveChanges`.
- **Amendment A6:** `New` joining the code-occupying set is verified harmless. `AssignBoxCodeIfAny` (the only writer of `Code` on a `New` box) is called once, at `ChangeTransportBoxStateHandler.cs:71`, always followed in the same unit of work by `Open(...)` which moves the box to `Opened`; `Reset(...)` nulls `Code` on the `Opened → New` return. No path persists a `New` box carrying a code. Do not add a test that depends on one existing.
- Transitions available for building fixtures: `Quarantine` = `Open(code, date, user)` then `ToQuarantine(date, user)`; `Error` = `Open(code, date, user)` then `Error(date, user, message)` (`Error` accepts any source state — `CheckState` no-ops on `Array.Empty<TransportBoxState>()`).

#### Implementation steps

- [ ] **Step 1: Add the two FR-3 tests**

```csharp
[Fact]
public async Task OpenTransportBox_WhenCodeHeldByQuarantinedBox_ShouldPreventDuplicate()
{
    // Arrange — an existing box holds B001 in Quarantine.
    var quarantined = new TransportBox();
    quarantined.Open("B001", DateTime.UtcNow, TestUser);
    quarantined.ToQuarantine(DateTime.UtcNow, TestUser);
    await _repository.AddAsync(quarantined);
    await _repository.SaveChangesAsync();

    var freshBox = new TransportBox();
    await _repository.AddAsync(freshBox);
    await _repository.SaveChangesAsync();

    // Act
    var result = await _handler.Handle(new ChangeTransportBoxStateRequest
    {
        BoxId = freshBox.Id,
        NewState = TransportBoxState.Opened,
        BoxCode = "B001"
    }, CancellationToken.None);

    // Assert — response only (amendment A3: do NOT assert the tracked box's Code here).
    result.Success.Should().BeFalse();
    result.ErrorCode.Should().Be(ErrorCodes.TransportBoxDuplicateActiveBoxFound);
    result.Params.Should().ContainKey("code").WhoseValue.Should().Be("B001");
}
```

Add the `Error` twin, `OpenTransportBox_WhenCodeHeldByErroredBox_ShouldPreventDuplicate`, built with `Open("B001", ...)` then `Error(DateTime.UtcNow, TestUser, "boom")`.

- [ ] **Step 2 (optional, only if persistence assertion is wanted): capture the InMemory database name**

Change line 34's inlined `Guid.NewGuid().ToString()` into a `private readonly string _databaseName = Guid.NewGuid().ToString();` field used by the options builder, then construct a second `ApplicationDbContext` on the same name inside the test to re-read the box. This is the **only** sanctioned way to assert persistence here. If you skip this step, do not assert persistence at all.

- [ ] **Step 3: Run the file and confirm the two new tests fail before the repository fix, pass after**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxUniquenessTests"
```

Expected after `consume-rule-in-transport-box-repository` has landed: **all** PASS — the two new ones plus the five pre-existing ones, none of which may be modified.

- [ ] **Step 4: Build and format**

```bash
cd backend && dotnet build && dotnet format
```

#### Acceptance criteria

- Assigning code `B001` to a `New` box while another box holds `B001` in `Quarantine` returns `Success = false`, `ErrorCode = TransportBoxDuplicateActiveBoxFound`, `Params["code"] == "B001"`.
- Same for a box holding `B001` in `Error`.
- No assertion anywhere in the file claims the rejected `New` box's `Code` is `null` on the *original* context's tracked instance (amendment A3).
- All five pre-existing tests pass **unmodified**, in particular `OpenTransportBoxWithCodeThenCloseItThenOpenAnotherWithSameCode_ShouldSucceed` (a `Closed` box still frees the code) and `OpenTwoTransportBoxesWithDifferentCodes_ShouldSucceed` (no self-match against the in-flight box).
- `ChangeTransportBoxStateHandler.cs` is **not** modified by this task.
- `dotnet build` and `dotnet format` succeed with no new warnings.

#### Tests to run

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxUniquenessTests"
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ChangeTransportBoxStateHandlerTests"
```

The second must pass unmodified — it mocks `ITransportBoxRepository`, so the repository change is invisible to it.

---

### task: add-code-occupancy-sql-shape-test

**Files:**
- Create (test): `backend/test/Anela.Heblo.Tests/Repositories/TransportBoxRepositoryCodeOccupancySqlShapeTests.cs`
- No production file changes.

**Depends on:** `consume-rule-in-transport-box-repository`.

#### Goal

Amendment A2, **mandatory**: prove against real PostgreSQL that both rewritten queries translate server-side. Every other test in this feature runs on `UseInMemoryDatabase`, which evaluates LINQ in memory and will happily "translate" anything — a query Npgsql cannot translate passes there and fails in staging. `Contains` over a `HasConversion<string>()` enum inside a `WHERE` is proven in this codebase (it is what the *old* `IsBoxCodeActiveAsync` did), but the same construct inside an `ORDER BY` is **not exercised anywhere in `backend/src` today**. This is the one way this change can reach staging broken.

#### Context

- Conventions to follow, all already used in this repo:
  - `[Collection("PostgresIntegration")]` (definition: `backend/test/Anela.Heblo.Tests/Common/PostgresIntegrationCollection.cs`) + `[Trait("Category", "Integration")]`.
  - Constructor-injected `PostgresSharedContainerFixture`, `IAsyncLifetime`, and `await _fixture.CreateDatabaseAsync("<hint>")` for an isolated database in the shared `postgres:16` container.
  - A private `CapturingCommandInterceptor : DbCommandInterceptor` overriding `ReaderExecuting` / `ReaderExecutingAsync` and collecting `command.CommandText`, wired via `DbContextOptionsBuilder.AddInterceptors(...)`.
  - Reference shape: `backend/test/Anela.Heblo.Tests/Features/Purchase/PurchaseOrderRepositoryHistorySqlShapeTests.cs` (copy its interceptor class and lifecycle verbatim).
- DDL: copy the `TransportBoxes` / `TransportBoxItems` / `TransportBoxStateLogs` `CREATE TABLE` block verbatim from `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateReceiveAtomicityIntegrationTests.cs` (`InitializeAsync`). **All three tables are required** — `GetByCodeAsync` `Include`s both child collections. Note `TransportBoxes."State"` is `text` (`HasConversion<string>()`) while `TransportBoxStateLogs."State"` is `integer` (default enum mapping); the copied DDL already gets this right. `StockUpOperations` is not needed.
- Construct the repository as `new TransportBoxRepository(context, NullLogger<TransportBoxRepository>.Instance)`.
- Seed via the aggregate + `SaveChangesAsync` (so the value converter runs) rather than raw SQL — see `memory/gotchas/raw-sql-insert-must-match-ef-mapping.md`.

#### Implementation steps

- [ ] **Step 1: Create the test class skeleton**

`[Collection("PostgresIntegration")]`, `[Trait("Category", "Integration")]`, namespace `Anela.Heblo.Tests.Repositories`, `IAsyncLifetime` creating the database + DDL + a `CapturingCommandInterceptor`-wired `ApplicationDbContext` in `InitializeAsync`, disposing the context in `DisposeAsync`.

- [ ] **Step 2: Assertion 1 — `IsBoxCodeActiveAsync` translates server-side**

Seed one `Quarantine` box holding `B001`. `_interceptor.Reset()`, call `await _repository.IsBoxCodeActiveAsync("B001")`, then assert:

- the result is `true` (the bug fix, now proven against real Postgres);
- `_interceptor.Commands` contains exactly **one** statement (single round trip, no client-side evaluation);
- that statement references the `"State"` column **and** contains a negation combined with set membership.

**SQL-assertion caveat — this is load-bearing.** Npgsql may render the negated membership either as inlined literals (`NOT ("State" IN ('Closed','Stocked'))` / `"State" NOT IN (...)`) or as a parameterised array (`NOT ("State" = ANY (@__CodeReleasingStates_0))`), because `CodeReleasingStates` is a captured static field rather than an inline constant. The spec's prose says `NOT IN ('Closed','Stocked')` — **do not pin that literal string**, or the assertion will fail on a correct implementation. Match on `"State"` plus a negation plus set membership (`IN` **or** `= ANY`), e.g.:

```csharp
var sql = _interceptor.Commands.Should().ContainSingle().Subject;
sql.Should().Contain("\"State\"");
sql.Should().MatchRegex("NOT\\s*\\(?[^)]*\"State\"|\"State\"\\s+NOT\\s+IN|NOT\\s*\\(\\s*[a-z0-9_.\"]*\"State\"\\s*=\\s*ANY");
```

Prefer a readable pair of `Should().Contain(...)` assertions over a brittle regex if that expresses it more clearly. What is being verified is that translation happens server-side **at all**, not its exact rendering.

- [ ] **Step 3: Assertion 2 — `GetByCodeAsync` emits the occupancy `ORDER BY` and does not throw**

`_interceptor.Reset()`, call `await _repository.GetByCodeAsync("B001")`, assert it completes without `InvalidOperationException` (an untranslatable `ORDER BY` throws here — that is the primary signal) and that the emitted SQL contains an `ORDER BY` referencing the `"State"` column.

- [ ] **Step 4: Assertion 3 — resolution order against real Postgres**

Seed a `Quarantine` box holding `B001` **first** (lower `Id`), then a `Stocked` box holding `B001` (higher `Id`). Assert `GetByCodeAsync("B001")` returns the `Quarantine` box — i.e. `false < true` under `DESC` puts the occupying box first, as designed.

- [ ] **Step 5: Run the integration test (Docker required)**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxRepositoryCodeOccupancySqlShapeTests"
```

Expected: all PASS. Docker must be available (`postgres:16` is pulled by `PostgresSharedContainerFixture`) — this is already a prerequisite of the existing `PostgresIntegration` collection, including `ChangeTransportBoxStateReceiveAtomicityIntegrationTests` in this same module.

If Docker is genuinely unavailable in the execution environment, say so explicitly in the completion report and record the test as **unverified** — do **not** delete, skip, or weaken it, and do not declare the feature done on the strength of the InMemory tests alone. Note that PR CI runs `--filter "Category!=Playwright&Category!=Integration"`, so CI will not cover this gap for you.

- [ ] **Step 6: Build and format**

```bash
cd backend && dotnet build && dotnet format
```

#### Acceptance criteria

- The new class carries both `[Collection("PostgresIntegration")]` and `[Trait("Category", "Integration")]`.
- `IsBoxCodeActiveAsync("B001")` against a real `Quarantine` row returns `true` and emits exactly one statement whose text references the `"State"` column under a negated set membership.
- `GetByCodeAsync` completes without `InvalidOperationException` and emits an `ORDER BY` referencing `"State"`.
- With a `Quarantine` box at a lower `Id` and a `Stocked` box at a higher `Id` sharing `B001`, `GetByCodeAsync("B001")` returns the `Quarantine` box.
- The SQL assertions accept **both** the inlined-literal and the `= ANY(...)` parameterised renderings; no assertion pins the exact string `NOT IN ('Closed','Stocked')`.
- No production file is modified by this task.
- `dotnet build` and `dotnet format` succeed with no new warnings.

#### Tests to run

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxRepositoryCodeOccupancySqlShapeTests"
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBox"
```

The second command intentionally omits `Category!=Integration` so the whole transport-box surface, integration tests included, runs together.

---

### task: document-code-uniqueness-invariant

**Files:**
- Create: `memory/gotchas/transport-box-code-uniqueness-single-definition.md`
- No source or test changes.

**Depends on:** nothing. Can run at any point; do it last so the paths it names are final.

#### Goal

FR-7 plus amendments A8 and A9: record the invariant, its single owner, its consuming call sites, the read-only detection query for pre-existing duplicates, and the DB-constraint follow-up — so the next person to add a `TransportBoxState` finds the rule before they find the bug.

#### Context

- The repo's memory convention lives in `CLAUDE.md` § Memory. Follow the front-matter + **Symptom / Root cause / Fix / Rules / Related files** shape of `memory/gotchas/postgres-partial-index-active-states.md` (read it first — it established the "a single source of truth keeps the handler and the schema in lockstep" rule for exactly this class of problem, and is the closest sibling).
- Front-matter shape used by that file:

```
---
name: <short title>
description: <one-line summary>
type: project
---
```

- **Amendment A9 is binding:** the `'Closed'` / `'Stocked'` literals in the detection SQL — and in any future partial-index predicate — are a **deliberate second copy** of the rule that cannot reference `TransportBoxStateRules` from SQL. Say so explicitly, and say that whoever repartitions the states must update the query and the follow-up index alongside the type. That is the one place the duplication is unavoidable, and naming it beats pretending otherwise.
- **Amendment A8 is binding:** the DB-level partial unique index is out of scope here but must be a written, findable follow-up rather than a prose aside. It must record that rule 1 of `memory/gotchas/postgres-partial-index-active-states.md` applies (`CREATE INDEX CONCURRENTLY` needs `migrationBuilder.Sql(sql, suppressTransaction: true)` or PostgreSQL rejects it with SQLSTATE 25001), that the predicate must use **string** literals (`State` is `HasConversion<string>()`, not int), and that the index cannot be applied while duplicate rows may still exist — run the detection query first.

#### Implementation steps

- [ ] **Step 1: Write `memory/gotchas/transport-box-code-uniqueness-single-definition.md`**

It must contain:

- **Symptom.** Box A sits in `Quarantine` holding `B001`. An operator assigns `B001` to a fresh box from the box-detail screen (`frontend/src/components/pages/TransportBoxDetail.tsx`, `handleBoxNumberSubmit`); the assignment succeeds and two live rows now hold `B001`. Every subsequent scan of the physical `B001` label resolves to the wrong aggregate, and every scan-driven action — fill, receive, stock-up — applies to it with no error raised.
- **Root cause.** The invariant had no single owner: an allow-list in `TransportBoxRepository.IsBoxCodeActiveAsync` (missing `Quarantine` and `Error`) and a deny-list in `OpenOrResumeBoxByCodeHandler`, drifting apart as states were added. There is no DB-level uniqueness constraint on `Code`, so the application layer is the only defence.
- **Fix.** `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxStateRules.cs` is the **single** definition. It is a deny-list: only `Closed` and `Stocked` release a code; every other state — present or future — occupies it, so forgetting about the type yields a false rejection, never a silent duplicate. Its backing array is private; the public surface is `OccupiesCode(TransportBoxState)` and `OccupiesCodePredicate`.
- **Rules.** (1) Transport-box code occupancy may only be defined in `TransportBoxStateRules`. The three consuming call sites, by path, are `backend/src/Anela.Heblo.Persistence/Logistics/TransportBoxes/TransportBoxRepository.cs` → `IsBoxCodeActiveAsync` and `GetByCodeAsync`, and `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/OpenOrResumeBoxByCode/OpenOrResumeBoxByCodeHandler.cs`. Comparing against `TransportBoxState.Closed`/`Stocked` directly for code-uniqueness purposes is a bug. (2) `GetPagedListAsync`'s `isActiveFilter` (`State != Closed`, a UI list filter that deliberately shows `Stocked` boxes) and `GetTransportBoxByCodeHandler`'s `isReceivable` (`{InTransit, Reserve, Quarantine}`) are **different concepts** and must stay as they are. (3) Adding a `TransportBoxState` member fails `TransportBoxStateRulesTests` by design — classify the new state in `TransportBoxStateRules`, do not just append it to the test's expected map.
- **Detection query**, verbatim, marked read-only (no `UPDATE`, no `DELETE`, no DDL):

```sql
SELECT "Code", COUNT(*), array_agg("Id"), array_agg("State")
FROM public."TransportBoxes"
WHERE "Code" IS NOT NULL AND "State" NOT IN ('Closed','Stocked')
GROUP BY "Code" HAVING COUNT(*) > 1;
```

- **The A9 note**, immediately after the query: these `'Closed'`/`'Stocked'` literals are a deliberate second copy of the partition that SQL cannot take from `TransportBoxStateRules`. Repartitioning the states means updating this query and the follow-up index in the same change.
- **Follow-up (A8).** A partial unique index — `CREATE UNIQUE INDEX CONCURRENTLY ... ON public."TransportBoxes" ("Code") WHERE "Code" IS NOT NULL AND "State" NOT IN ('Closed','Stocked')` — would close the TOCTOU window between the check and the save. Deferred because migrations here are applied manually and out of band (`memory/gotchas/ef-migration-codebase-drift.md`) and production may already hold duplicate rows created by this bug, which would make the index creation fail late. Prerequisites: run the detection query against staging **and** production and confirm zero rows; use `migrationBuilder.Sql(sql, suppressTransaction: true)` (SQLSTATE 25001 otherwise, per rule 1 of `memory/gotchas/postgres-partial-index-active-states.md`); use **string** literals because `State` is `HasConversion<string>()`.
- **Related files.** `TransportBoxStateRules.cs`, `TransportBoxRepository.cs`, `OpenOrResumeBoxByCodeHandler.cs`, `ChangeTransportBoxStateHandler.cs` (`HandleNewToOpened`, the guarded path), `backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxStateRulesTests.cs`, `backend/test/Anela.Heblo.Tests/Repositories/TransportBoxRepositoryCodeOccupancySqlShapeTests.cs`.

- [ ] **Step 2: Verify every path named in the file exists**

```bash
cd /home/user/worktrees/feature-3887-Arch-Review-Transportboxes-Box-Code-Uniqueness-Is && \
  grep -o 'backend/[a-zA-Z0-9_./-]*' memory/gotchas/transport-box-code-uniqueness-single-definition.md | sort -u | xargs -I{} test -e {} && echo "all paths OK"
```

- [ ] **Step 3: Final full-solution validation gate**

With every other task complete:

```bash
cd backend && dotnet build
cd backend && dotnet format
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "Category!=Integration"
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBox"
```

The third command is the broad regression sweep; the fourth runs the transport-box surface **including** the new `Category=Integration` SQL-shape test. Pre-existing, change-unrelated failures elsewhere in the suite (there are known timing-flaky tests) should be identified as such by re-running them on a clean checkout rather than papered over. No frontend files change, so `npm run build` / `npm run lint` and the E2E suite are not required.

#### Acceptance criteria

- `memory/gotchas/transport-box-code-uniqueness-single-definition.md` exists, carries the `name` / `description` / `type: project` front matter, and follows the Symptom / Root cause / Fix / Rules / Related files shape of its sibling.
- It names all three consuming call sites by full path.
- It contains the detection query verbatim, and the query is read-only — no `UPDATE`, no `DELETE`, no DDL.
- It carries the A9 note that the SQL's `'Closed'`/`'Stocked'` literals are a deliberate, unavoidable second copy of the partition.
- It carries the A8 follow-up for the partial unique index, including the `suppressTransaction: true` requirement, the string-literal requirement, and the "run the detection query first" precondition.
- Every file path it references resolves.
- No source or test file is modified by this task.

#### Tests to run

No test targets this file. Run the final validation gate from Step 3 as the feature-level completion check.
