# Remove `OrderIds` from `PrintPickingListResult` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete the unused `OrderIds` property from the application-layer DTO `PrintPickingListResult` and remove the corresponding dead arrange step in `LogisticsExpeditionPickingAdapterTests`, eliminating a misleading API surface that no producer writes and no consumer reads.

**Architecture:** Surgical two-file deletion confined to `Anela.Heblo.Application.Features.Logistics.Picking` and its tests. No production runtime behaviour change: `OrderIds` was always initialized empty, never populated by `ShoptetApiExpeditionListSource.CreatePickingList`, and never read by `LogisticsExpeditionPickingAdapter.CreatePickingListAsync` when mapping to the outer `ExpeditionPickingResult`. The cross-feature contract `ExpeditionPickingResult` is untouched (it never had `OrderIds`). No OpenAPI surface change (the DTO is internal, not on a controller, MediatR result, or persisted entity).

**Tech Stack:** .NET 8, xUnit, FluentAssertions, Moq. Solution path: `backend/Anela.Heblo.sln`.

---

## Pre-flight Context (read once before Task 1)

Files in scope for this entire plan:

| Path | Role |
|------|------|
| `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListResult.cs` | DTO definition — delete `OrderIds` property. |
| `backend/test/Anela.Heblo.Tests/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapterTests.cs` | Tests — delete `OrderIds = new List<int> { 1, 2, 3 }` arrange line in `CreatePickingListAsync_TranslatesResultFields`. |

Files **NOT** to modify (verified by spec + arch review):
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs` — does not reference `OrderIds`.
- `backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapter.cs` — does not reference `OrderIds`.
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingResult.cs` — never had `OrderIds`.
- `frontend/src/api/` — generated OpenAPI client; the DTO is not on the API surface, no regeneration needed.

Current state of `PrintPickingListResult.cs` (8 lines total):

```csharp
namespace Anela.Heblo.Application.Features.Logistics.Picking;

public class PrintPickingListResult
{
    public IList<string> ExportedFiles { get; set; } = new List<string>();
    public int TotalCount { get; set; }
    public IList<int> OrderIds { get; set; } = new List<int>();
}
```

Current state of the relevant test (`CreatePickingListAsync_TranslatesResultFields`, lines 52–76):

```csharp
[Fact]
public async Task CreatePickingListAsync_TranslatesResultFields()
{
    // Arrange
    var innerResult = new PrintPickingListResult
    {
        ExportedFiles = new List<string> { "/tmp/a.pdf", "/tmp/b.pdf" },
        TotalCount = 12,
        OrderIds = new List<int> { 1, 2, 3 },
    };
    _innerSource
        .Setup(x => x.CreatePickingList(
            It.IsAny<PrintPickingListRequest>(),
            It.IsAny<Func<IList<string>, Task>?>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(innerResult);

    // Act
    var result = await CreateAdapter().CreatePickingListAsync(
        new ExpeditionPickingRequest(), onBatchFilesReady: null);

    // Assert
    result.ExportedFiles.Should().BeEquivalentTo(new[] { "/tmp/a.pdf", "/tmp/b.pdf" });
    result.TotalCount.Should().Be(12);
}
```

The other three tests in the same file construct `new PrintPickingListResult()` with no property initializers — they remain compile-clean.

---

## Task 1: Establish baseline — confirm all touched tests pass before changing anything

**Why this step exists:** The arch review states the four tests in `LogisticsExpeditionPickingAdapterTests` currently pass. Verify that baseline so a post-change failure can be attributed to this plan, not pre-existing breakage.

**Files:** none modified (read-only verification).

- [ ] **Step 1.1: Run the entire adapter test class against `main` state**

Run:

```bash
dotnet test backend/Anela.Heblo.sln \
  --filter "FullyQualifiedName~LogisticsExpeditionPickingAdapterTests" \
  --nologo
```

Expected output (substring): `Passed!  - Failed: 0, Passed: 4, Skipped: 0`

If any test fails here, **stop**. The failure is pre-existing and out of scope; report it and do not proceed.

- [ ] **Step 1.2: Verify the solution builds clean before edits**

Run:

```bash
dotnet build backend/Anela.Heblo.sln --nologo
```

Expected: `Build succeeded` with `0 Error(s)`. Warning count must be recorded — the post-change build must not introduce new warnings.

---

## Task 2: Repository-wide search — confirm only the three expected `OrderIds` callsites on `PrintPickingListResult` exist

**Why this step exists:** FR-3 of the spec demands proof that no hidden consumer reads `OrderIds`. Arch review expects exactly two production-relevant matches (definition + test arrange) plus historical planning artifacts and unrelated symbols (the `newOrderIds` React callback, the `ChangePurchaseOrderIdsToInt` EF migration).

**Files:** none modified.

- [ ] **Step 2.1: Grep the entire repository for `OrderIds`**

Run:

```bash
grep -rn "OrderIds" backend/src backend/test \
  --include="*.cs" \
  --exclude-dir=bin --exclude-dir=obj
```

Expected matches (exactly these two files for `PrintPickingListResult`):

```
backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListResult.cs:7:    public IList<int> OrderIds { get; set; } = new List<int>();
backend/test/Anela.Heblo.Tests/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapterTests.cs:60:            OrderIds = new List<int> { 1, 2, 3 },
```

Other `OrderIds` hits inside `backend/` (if any) must come from unrelated symbols — most commonly a `ChangePurchaseOrderIdsToInt` EF migration or other domain types. They must **not** reference `PrintPickingListResult.OrderIds`.

If any *new* production reference to `PrintPickingListResult.OrderIds` appears (e.g. a producer setting it, a consumer reading it), **stop**. The spec's premise no longer holds and this plan is invalid.

- [ ] **Step 2.2: Confirm the frontend generated client does not reference `OrderIds` from this DTO**

Run:

```bash
grep -rn "OrderIds" frontend/src/api/ 2>/dev/null || echo "no matches"
```

Expected: `no matches` (the DTO is internal — not on the OpenAPI surface).

If matches appear, inspect them. They must not relate to `PrintPickingListResult`; if any do, stop and escalate — the spec's assumption that the DTO is internal-only would be wrong.

---

## Task 3: Delete `OrderIds` from `PrintPickingListResult`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListResult.cs`

- [ ] **Step 3.1: Remove the `OrderIds` property line**

Edit `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListResult.cs`.

Replace the entire file contents with:

```csharp
namespace Anela.Heblo.Application.Features.Logistics.Picking;

public class PrintPickingListResult
{
    public IList<string> ExportedFiles { get; set; } = new List<string>();
    public int TotalCount { get; set; }
}
```

The change is deleting line 7 (`public IList<int> OrderIds { get; set; } = new List<int>();`). No other line is altered. Namespace and class declaration are unchanged.

- [ ] **Step 3.2: Verify the build now fails on the dead test arrange line (RED)**

Run:

```bash
dotnet build backend/Anela.Heblo.sln --nologo
```

Expected: build fails with a `CS0117` (or equivalent) error pointing at `LogisticsExpeditionPickingAdapterTests.cs` line 60 — `'PrintPickingListResult' does not contain a definition for 'OrderIds'`.

This is the intended RED state for the next task. If the build instead **succeeds**, that means the test arrange was not where this plan thinks it is — stop and re-read both files before proceeding.

If the build fails with errors in any file other than `LogisticsExpeditionPickingAdapterTests.cs`, stop. A hidden consumer exists that the architecture review missed; revert Step 3.1 and escalate.

---

## Task 4: Remove the dead test arrange step

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapterTests.cs`

- [ ] **Step 4.1: Delete the `OrderIds` initializer line in `CreatePickingListAsync_TranslatesResultFields`**

Edit `backend/test/Anela.Heblo.Tests/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapterTests.cs`.

Find the arrange block in `CreatePickingListAsync_TranslatesResultFields` (line 56–61) which currently reads:

```csharp
        var innerResult = new PrintPickingListResult
        {
            ExportedFiles = new List<string> { "/tmp/a.pdf", "/tmp/b.pdf" },
            TotalCount = 12,
            OrderIds = new List<int> { 1, 2, 3 },
        };
```

Replace with:

```csharp
        var innerResult = new PrintPickingListResult
        {
            ExportedFiles = new List<string> { "/tmp/a.pdf", "/tmp/b.pdf" },
            TotalCount = 12,
        };
```

That is, drop the `OrderIds = new List<int> { 1, 2, 3 },` line. Do not touch any other line in the method, the file, the assertions, or the other three tests (`CreatePickingListAsync_TranslatesRequestFieldsOneToOne`, `CreatePickingListAsync_PassesCallbackThroughVerbatim`, `CreatePickingListAsync_PassesCancellationTokenThrough`).

- [ ] **Step 4.2: Verify the solution builds clean (GREEN compile)**

Run:

```bash
dotnet build backend/Anela.Heblo.sln --nologo
```

Expected: `Build succeeded` with `0 Error(s)`. Warning count must equal the baseline recorded in Step 1.2 — no new warnings introduced.

If the build still fails, the failure location reveals an unexpected consumer. Stop and escalate; do not paper over by editing additional files outside the plan's two-file scope.

- [ ] **Step 4.3: Run the adapter test class — all four tests must still pass**

Run:

```bash
dotnet test backend/Anela.Heblo.sln \
  --filter "FullyQualifiedName~LogisticsExpeditionPickingAdapterTests" \
  --nologo
```

Expected output (substring): `Passed!  - Failed: 0, Passed: 4, Skipped: 0`

The assertions in `CreatePickingListAsync_TranslatesResultFields` were never about `OrderIds`; removing the arrange line cannot change behaviour. If any test now fails, the change in Task 3 or Task 4 deviated from the plan — diff against the snippets above before doing anything else.

---

## Task 5: Final verification — repository-wide grep is clean

**Why this step exists:** FR-1 and FR-3 both require post-change repository search to confirm no lingering `PrintPickingListResult.OrderIds` references.

**Files:** none modified.

- [ ] **Step 5.1: Confirm `OrderIds` no longer appears in the Logistics namespace**

Run:

```bash
grep -rn "OrderIds" \
  backend/src/Anela.Heblo.Application/Features/Logistics \
  backend/test/Anela.Heblo.Tests/Features/Logistics \
  --include="*.cs"
```

Expected: **no output**. Any remaining match means either Task 3 or Task 4 was applied incorrectly — re-read the file and re-apply.

- [ ] **Step 5.2: Confirm no producer-side regression in the Shoptet adapter**

Run:

```bash
grep -n "OrderIds" \
  backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs
```

Expected: **no output**. (Arch review verified this file never referenced `OrderIds`. This grep is a defensive double-check.)

- [ ] **Step 5.3: Confirm OpenAPI client is unchanged**

Run:

```bash
git status frontend/src/api/
```

Expected: clean — no modifications. Confirms the DTO was not on the API surface.

---

## Task 6: Validate full project gates (per `CLAUDE.md`)

**Files:** none modified.

- [ ] **Step 6.1: Run `dotnet build` across the solution**

Run:

```bash
dotnet build backend/Anela.Heblo.sln --nologo
```

Expected: `Build succeeded` with `0 Error(s)`.

- [ ] **Step 6.2: Run `dotnet format` and confirm no diff on the two edited files**

Run:

```bash
dotnet format backend/Anela.Heblo.sln \
  --include backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListResult.cs \
            backend/test/Anela.Heblo.Tests/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapterTests.cs \
  --verify-no-changes
```

Expected: exits 0 with no reported formatting changes. If `dotnet format` reports changes, re-run **without** `--verify-no-changes` to let it fix the formatting, then re-run with the flag to confirm clean.

- [ ] **Step 6.3: Run the full backend test suite**

Run:

```bash
dotnet test backend/Anela.Heblo.sln --nologo
```

Expected: all tests pass. The plan touched only an internal DTO field that has no producers or consumers in production code, so no other test should be affected. If any unrelated test fails, that failure is pre-existing or environmental — do not absorb its fix into this PR.

---

## Task 7: Commit

**Files:** none modified beyond Tasks 3 and 4.

- [ ] **Step 7.1: Stage exactly the two edited files**

Run:

```bash
git add \
  backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListResult.cs \
  backend/test/Anela.Heblo.Tests/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapterTests.cs
```

- [ ] **Step 7.2: Confirm the staged diff is exactly what is expected**

Run:

```bash
git diff --cached
```

Expected diff:

```diff
diff --git a/backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListResult.cs b/backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListResult.cs
@@
 public class PrintPickingListResult
 {
     public IList<string> ExportedFiles { get; set; } = new List<string>();
     public int TotalCount { get; set; }
-    public IList<int> OrderIds { get; set; } = new List<int>();
 }
diff --git a/backend/test/Anela.Heblo.Tests/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapterTests.cs b/backend/test/Anela.Heblo.Tests/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapterTests.cs
@@
         var innerResult = new PrintPickingListResult
         {
             ExportedFiles = new List<string> { "/tmp/a.pdf", "/tmp/b.pdf" },
             TotalCount = 12,
-            OrderIds = new List<int> { 1, 2, 3 },
         };
```

Only those two hunks. If the diff contains anything else — whitespace, reordering, an unrelated file — `git restore --staged` the extras and re-stage only the two intended files.

- [ ] **Step 7.3: Commit**

Run:

```bash
git commit -m "refactor: remove unused OrderIds field from PrintPickingListResult

Drop the OrderIds property from the internal application-layer DTO
PrintPickingListResult and its dead arrange step in
LogisticsExpeditionPickingAdapterTests. The field was initialized empty,
never written by ShoptetApiExpeditionListSource.CreatePickingList, and
never read by LogisticsExpeditionPickingAdapter.CreatePickingListAsync.
No runtime behaviour change; ExpeditionPickingResult (the cross-feature
contract) is untouched and the OpenAPI surface is unaffected."
```

- [ ] **Step 7.4: Verify the working tree is clean and the commit landed**

Run:

```bash
git status
git log -1 --stat
```

Expected: `nothing to commit, working tree clean`, and the log entry shows exactly two files changed — `PrintPickingListResult.cs` (-1 line) and `LogisticsExpeditionPickingAdapterTests.cs` (-1 line).

---

## Done criteria

- `PrintPickingListResult` no longer declares `OrderIds`. (FR-1)
- `CreatePickingListAsync_TranslatesResultFields` no longer initializes `OrderIds`; its assertions on `ExportedFiles` and `TotalCount` are intact. (FR-2, NFR-3)
- Repository grep finds zero references to `OrderIds` inside `backend/src/Anela.Heblo.Application/Features/Logistics/` and `backend/test/Anela.Heblo.Tests/Features/Logistics/`. (FR-3)
- `frontend/src/api/` is unchanged — DTO was not on the API surface. (FR-3)
- `dotnet build` + `dotnet format --verify-no-changes` + `dotnet test` all pass. (CLAUDE.md validation gates)
- Exactly one commit, two files changed, two lines removed. (NFR-2)
