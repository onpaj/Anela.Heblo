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

