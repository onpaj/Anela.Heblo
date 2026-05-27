# Remove Forced GC.Collect() From AnalyticsRepository Streaming Loop — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete the forced `GC.Collect()` call (and its misleading comment) from `AnalyticsRepository.StreamProductsWithSalesAsync` without altering the yielded sequence or any consumer behavior, and add a characterization unit test that pins the method's identity/order/count contract so the upcoming `IAnalyticsProductSource` refactor cannot silently regress it.

**Architecture:** Single-file surgical edit in the Analytics vertical slice (`backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/AnalyticsRepository.cs`). The `IAnalyticsRepository` abstraction, all five MediatR margin-report handlers, and the `AnalyticsProduct` shape are untouched. A new direct xUnit test against the concrete repository (constructed with a `Mock<ICatalogRepository>` and a `null!` `ApplicationDbContext` because `StreamProductsWithSalesAsync` never dereferences the context) characterizes the yielded sequence across the batch boundary (≥250 inputs, `batchSize = 100`).

**Tech Stack:** .NET 8, C#, xUnit, FluentAssertions, Moq. Existing `Anela.Heblo.Tests` project. No new packages, no migrations, no DI changes, no config changes.

---

## File Structure

**Modify:**
- `backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/AnalyticsRepository.cs` — delete the `// Allow garbage collection between batches` comment and the `GC.Collect();` call inside the batch loop. Everything else stays byte-identical.

**Create:**
- `backend/test/Anela.Heblo.Tests/Features/Analytics/AnalyticsRepositoryTests.cs` — single xUnit test class that exercises the concrete `AnalyticsRepository.StreamProductsWithSalesAsync` against a mocked `ICatalogRepository`. One test method asserting count/order/identity round-trip across more than two batches. AAA pattern, FluentAssertions, Moq.

Placement rationale: the existing `backend/test/Anela.Heblo.Tests/Features/Analytics/` folder is the natural sibling of the production `Features/Analytics/Infrastructure/` slice and matches where the other Analytics unit tests (e.g., `GetMarginReportHandlerTests.cs`) already live. No `Infrastructure/` subfolder exists under the test tree today — adding the file at the same level as the existing handler tests keeps the layout consistent.

---

## Task 1: Add direct characterization test for `StreamProductsWithSalesAsync`

**Why this task is first (and not strictly RED-first):** This is a *characterization test*, not a new-feature TDD cycle. The yielded sequence does not change between "before GC removal" and "after GC removal" — that's the whole point of the fix. So the test passes against the current code. We add it *first* so that:

1. We prove the test compiles and passes against the pre-change implementation (baseline green).
2. After Task 2 deletes the `GC.Collect()` line, we re-run it to confirm the contract still holds (post-change green).
3. It survives in the repo as the guard rail the arch review (FR-3) requires — the existing handler tests all mock `IAnalyticsRepository` and never exercise the concrete class, so without this test the next refactor of `StreamProductsWithSalesAsync` is unprotected.

The arch review explicitly states: *"Treat it as required, not optional."*

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Analytics/AnalyticsRepositoryTests.cs`

### Step 1.1: Confirm the test file does not already exist

- [ ] **Run:**

```bash
ls backend/test/Anela.Heblo.Tests/Features/Analytics/AnalyticsRepositoryTests.cs 2>/dev/null || echo "absent — proceed"
```

Expected output: `absent — proceed`

If the file already exists, stop and read it before continuing — the plan assumes a fresh file.

### Step 1.2: Create the test file with the characterization test

- [ ] **Create `backend/test/Anela.Heblo.Tests/Features/Analytics/AnalyticsRepositoryTests.cs` with the following exact content:**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Analytics.Infrastructure;
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Domain.Features.Catalog;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Analytics;

/// <summary>
/// Direct characterization tests for the concrete <see cref="AnalyticsRepository"/>.
/// The five margin-report handlers all mock <see cref="IAnalyticsRepository"/> and
/// therefore cannot catch regressions in the concrete streaming method. This class
/// pins the identity/order/count contract of <see cref="AnalyticsRepository.StreamProductsWithSalesAsync"/>
/// across the internal batch boundary (batchSize = 100).
/// </summary>
public class AnalyticsRepositoryTests
{
    [Fact]
    public async Task StreamProductsWithSalesAsync_YieldsAllProductsInInputOrder_AcrossMultipleBatches()
    {
        // Arrange
        const int productCount = 250; // > 2 * batchSize (100) to cross the boundary twice
        var fromDate = new DateTime(2026, 1, 1);
        var toDate = new DateTime(2026, 12, 31);
        var productTypes = new[] { ProductType.Product };

        var input = Enumerable.Range(0, productCount)
            .Select(i => new CatalogAggregate
            {
                ProductCode = $"P{i:D4}",
                ProductName = $"Product {i}",
                Type = ProductType.Product
            })
            .ToList();

        var catalogRepositoryMock = new Mock<ICatalogRepository>();
        catalogRepositoryMock
            .Setup(r => r.GetProductsWithSalesInPeriod(
                fromDate,
                toDate,
                productTypes,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(input);

        // ApplicationDbContext is required by the ctor but never dereferenced by
        // StreamProductsWithSalesAsync — passing null! is safe for this test.
        var sut = new AnalyticsRepository(catalogRepositoryMock.Object, null!);

        // Act
        var yielded = new List<AnalyticsProduct>();
        await foreach (var product in sut.StreamProductsWithSalesAsync(fromDate, toDate, productTypes))
        {
            yielded.Add(product);
        }

        // Assert
        yielded.Should().HaveCount(productCount);
        yielded.Select(p => p.ProductCode).Should().Equal(input.Select(p => p.ProductCode));
        yielded.First().ProductCode.Should().Be("P0000");
        yielded.Last().ProductCode.Should().Be($"P{productCount - 1:D4}");

        // Spot-check identities at batch boundaries (indexes 99/100 and 199/200).
        yielded[99].ProductCode.Should().Be("P0099");
        yielded[100].ProductCode.Should().Be("P0100");
        yielded[199].ProductCode.Should().Be("P0199");
        yielded[200].ProductCode.Should().Be("P0200");
    }
}
```

**Notes for the implementer:**
- `CatalogAggregate` exposes `ProductCode` as a settable property (it proxies the `Id` of the `Entity<string>` base). `ProductName` is settable. `Type` defaults to `ProductType.UNDEFINED`; we set it to `ProductType.Product` explicitly.
- `Margins` defaults to a new `MonthlyMarginHistory()` with an empty `MonthlyData` dictionary — that's fine; the method under test handles the empty case via `Averages` fallback and produces `MarginAmount = 0` (which satisfies the required `decimal` init-only property on `AnalyticsProduct`).
- `SalesHistory` defaults to an empty `List<CatalogSaleRecord>`; `PurchaseHistory` defaults to an empty `List<CatalogPurchaseRecord>`. Both are non-null, so the LINQ filtering inside the method does not NRE.
- **Do not assert anything about GC behavior, margin amounts, sales math, or the `latestMarginEntry` fallback chain.** The arch review (Risks table, row 4) warns that asserting on internal margin math couples this test to internals that the upcoming refactor will rewrite. Identity + order + count is the entire contract this test pins.
- Use `ProductType.Product` for the enum value. If that exact name is not present on the enum in this branch, the build will fail at this step — list the enum values with `grep -n "^\s*[A-Z]" backend/src/Anela.Heblo.Domain/Features/Catalog/ProductType.cs` and substitute any defined value (e.g., `Material`, `Goods`). The specific enum value is not load-bearing; the test only needs a valid one.

### Step 1.3: Build the test project to verify the test compiles

- [ ] **Run:**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: `Build succeeded` with 0 errors. Warnings about unused usings or analyzer suggestions are acceptable as long as they do not block the build.

If you get `CS0246` for `ProductType.Product`, fix the enum value per the note in Step 1.2 and rebuild.

If you get `CS0103` or `CS0117` on `CatalogAggregate` members, re-read `backend/src/Anela.Heblo.Domain/Features/Catalog/CatalogAggregate.cs` and adjust property names. Do not work around by adding extra `using` statements you do not understand — fix the symbol reference.

### Step 1.4: Run the new test against the *pre-change* code to establish baseline green

- [ ] **Run:**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~AnalyticsRepositoryTests" \
  --no-build
```

Expected: `Passed: 1, Failed: 0`. This proves the test characterizes the *current* behavior, which is the behavior we want to preserve.

**If the test fails before Task 2 runs**, stop. The failure means the test is wrong about today's behavior, not that the fix is wrong. Read the failure carefully and adjust the test (most likely the expected `ProductCode` ordering — verify by reading `AnalyticsRepository.cs` lines 42–117 once more). Do not proceed to Task 2 with a failing characterization test.

### Step 1.5: Commit the test on its own

- [ ] **Run:**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Analytics/AnalyticsRepositoryTests.cs
git commit -m "test(analytics): characterize StreamProductsWithSalesAsync yielded sequence"
```

Separating the test commit from the production-code commit keeps the diff for the actual fix (Task 2) to a single line plus a single comment line in a single file, which is what the spec (FR-1, FR-2) and arch review both explicitly require.

---

## Task 2: Remove the forced `GC.Collect()` call and its misleading comment

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/AnalyticsRepository.cs:119-120`

### Step 2.1: Delete the comment and the `GC.Collect()` call

- [ ] **In `backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/AnalyticsRepository.cs`, locate the two-line block at lines 119–120:**

```csharp
            // Allow garbage collection between batches
            GC.Collect();
```

…inside the `for (int i = 0; i < allProducts.Count; i += batchSize)` loop at lines 42–121.

- [ ] **Delete both lines.** Leave no replacement comment behind (per NFR-3 of the spec: *"the absence of GC.Collect() is self-documenting"*).

Surrounding context after the edit should look like:

```csharp
            foreach (var product in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // ... existing projection code unchanged ...

                yield return new AnalyticsProduct
                {
                    // ... existing initializer unchanged ...
                };
            }
        }
    }
```

The closing `}` of the `for` loop directly follows the closing `}` of the inner `foreach`. No blank line between them is required (or forbidden — whatever `dotnet format` produces is fine).

**Do not** modify:
- The class-level XML doc comment on lines 11–14 (`🔒 PERFORMANCE FIX: ... Prevents memory overload`). It is misleading but out of scope per the arch review (Decision 3 and Specification Amendments item 2). It will be rewritten during the follow-up `IAnalyticsProductSource` refactor.
- The method-level XML doc comment on lines 26–29.
- The `for`/`Skip`/`Take`/`yield return` structure of the loop.
- The `batchSize` constant.
- Any other line in the file.

### Step 2.2: Repo-wide grep to confirm no other `GC.Collect` exists (FR-1 acceptance)

- [ ] **Run:**

```bash
grep -rn "GC\.Collect" backend/src backend/test || echo "no matches — good"
```

Expected output: `no matches — good`.

If any match comes back, stop and investigate. The spec (FR-1) requires that the removed call is the only occurrence post-change. The arch review confirmed (at spec-time) that no other call site exists in this codebase, so any match is either: (a) a new one that snuck in during this branch's lifetime, or (b) a string literal/comment that is acceptable but worth reading. Either way, do not proceed until you understand it.

### Step 2.3: Run `dotnet format` over the touched file

- [ ] **Run:**

```bash
dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj \
  --include backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/AnalyticsRepository.cs
```

Expected: command exits 0 with no errors. It is acceptable for the formatter to make whitespace adjustments inside the `for` loop where the two deleted lines used to be — that is the *only* additional change expected from this step. If the formatter changes anything *outside* the immediate vicinity of the deletion, revert that part and re-run the formatter targeting just the file (it should be idempotent).

### Step 2.4: Build the solution

- [ ] **Run:**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: `Build succeeded` with 0 errors. Warnings unrelated to this change are acceptable.

If the build fails, the most likely cause is a stray brace/semicolon left over from the deletion. Re-open `AnalyticsRepository.cs` and verify that the inner `foreach` block and the outer `for` block both close cleanly with no orphaned braces.

### Step 2.5: Re-run the characterization test added in Task 1

- [ ] **Run:**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~AnalyticsRepositoryTests" \
  --no-build
```

Expected: `Passed: 1, Failed: 0`.

A pass here is the primary acceptance signal for FR-3: the yielded sequence (count, order, identity) is byte-identical to the pre-change behavior captured in Task 1.4.

### Step 2.6: Run the full Analytics test slice

- [ ] **Run:**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Analytics" \
  --no-build
```

Expected: all tests pass, including `GetMarginReportHandlerTests`, `GetProductMarginAnalysisHandlerTests`, `GetProductMarginSummaryHandlerTests`, `GetInvoiceImportStatisticsHandlerTests`, the `DashboardTiles/InvoiceImportStatisticsTileTests`, and the new `AnalyticsRepositoryTests`.

These handler tests mock `IAnalyticsRepository` and therefore cannot detect a concrete-class behavior change — but they will catch any accidental signature or compile-level regression introduced by an over-zealous edit, which is exactly what we want at this gate.

### Step 2.7: Commit the production-code change on its own

- [ ] **Run:**

```bash
git add backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/AnalyticsRepository.cs
git commit -m "perf(analytics): remove forced GC.Collect from streaming batch loop

The GC.Collect call at the end of every 100-product batch forced a
synchronous, blocking Gen2 collection on every iteration. It provided
no memory benefit because allProducts is fully materialized via
ICatalogRepository.GetProductsWithSalesInPeriod before the loop runs —
peak working set is already paid. Removing the call eliminates 5+
forced Gen2 pauses per margin-report request (one per use case) with
no change to the yielded AnalyticsProduct sequence.

The misleading // Allow garbage collection between batches comment is
removed as well; per spec NFR-3 the absence is self-documenting.

Scope: single-line removal (plus its comment) in a single file. The
batching loop, public surface, and IAnalyticsRepository interface are
unchanged. The broader concern that the list is fully materialized
upfront is tracked separately under the IAnalyticsProductSource
ownership refactor."
```

---

## Task 3: Final validation gate

Per `CLAUDE.md`, every task must pass `dotnet build` + `dotnet format` + the touched tests before being declared done. Task 2 covered all three in narrow form against the Analytics slice. This task is the broader gate.

**Files:** none (verification only).

### Step 3.1: Full solution build

- [ ] **Run:**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: `Build succeeded`, 0 errors.

### Step 3.2: Full-solution `dotnet format` check

- [ ] **Run:**

```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: exits 0 with no diff. If it produces a diff, run it again without `--verify-no-changes` to apply the fixes, then `git status` to see what changed — only `AnalyticsRepository.cs` should be touched. Commit any whitespace fixup as `style(analytics): apply dotnet format` if needed.

### Step 3.3: Run the full backend test suite (or, at minimum, every test under Analytics)

- [ ] **Run:**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build
```

Expected: 100% pass rate. There should be zero test-count delta from the pre-change baseline aside from the +1 new test added in Task 1.

If anything outside Analytics fails, the failure is almost certainly pre-existing and unrelated to this change (the diff touches only the Analytics infrastructure file). Re-run `git log -1 -- <failing-test-file>` to confirm the failing test hasn't been touched by this branch. If it has, investigate before continuing.

### Step 3.4: Final diff sanity check

- [ ] **Run:**

```bash
git diff main...HEAD -- backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/AnalyticsRepository.cs
```

Expected: a diff of exactly two removed lines (the comment and the `GC.Collect();` call), with no additions and no other changes. The arch review's success criterion is *byte-identical apart from the deleted line* — this is the verification.

- [ ] **Run:**

```bash
git diff main...HEAD -- backend/test/Anela.Heblo.Tests/Features/Analytics/AnalyticsRepositoryTests.cs
```

Expected: the new test file in its entirety; no modifications to any other test file.

### Step 3.5: Confirm task completion

- [ ] All seven acceptance criteria from FR-1 / FR-2 / FR-3 of the spec are satisfied:
  - `GC.Collect()` no longer appears in `AnalyticsRepository.cs`. ✅ (Step 2.1)
  - The misleading comment is removed. ✅ (Step 2.1)
  - Method signature, return type, parameter list, yielded sequence unchanged. ✅ (Task 1 characterization test passes pre- and post-change.)
  - All five margin-report handler call sites continue to compile and behave identically. ✅ (Step 2.6 ran them.)
  - Repo-wide search confirms no other `GC.Collect()` calls exist. ✅ (Step 2.2)
  - `for` loop bounds, batch size, `Skip`/`Take`, `yield return` byte-identical. ✅ (Step 3.4)
  - `dotnet build` and `dotnet format` succeed. ✅ (Steps 3.1, 3.2)

---

## Out-of-Scope Reminders (do not do these, even if tempted)

The arch review and spec are both explicit on what *not* to touch in this PR:

1. **Do not** delete the `for`/`Skip`/`Take` batching wrapper, even though it provides no memory benefit once the list is materialized. (Arch review Decision 1, spec FR-2.) This is intentionally deferred — the `IAnalyticsProductSource` ownership refactor will replace the whole loop with EF-streamed enumeration.
2. **Do not** update the `🔒 PERFORMANCE FIX: ... Prevents memory overload` XML doc comments on `AnalyticsRepository.cs:11-14` or `IAnalyticsRepository.cs:9-12`. They are misleading but tracked for the follow-up. (Arch review Decision 3, Specification Amendments item 2.)
3. **Do not** add a Roslyn analyzer or EditorConfig rule banning `GC.Collect`. Worth doing, but explicitly deferred. (Arch review Risks row 2, Specification Amendments item 3.)
4. **Do not** add benchmarks, profiling harnesses, or GC telemetry. (Spec Out of Scope.)
5. **Do not** touch any of the five margin-report use cases. (Spec Out of Scope.)
6. **Do not** assert on margin-amount math, GC behavior, or the `latestMarginEntry` fallback chain in the new test. Identity/order/count only. (Arch review Risks row 4.)

If a reviewer asks for any of the above as part of this PR, point them to the `IAnalyticsProductSource` cross-module boundary issue and ask them to add it there instead. The whole point of this PR's tight scope is to ship the perf fix today without merge friction against that refactor branch.

---

## Risk Recap

- **Rollback:** `git revert` of the Task-2 commit restores `GC.Collect()`. The Task-1 test commit can stay independently — it does not depend on the fix and continues to characterize the method's contract.
- **Blast radius:** one production file, one new test file. No interfaces, DTOs, migrations, config, or DI registrations touched.
- **Risk level:** very low. The deleted line has no functional effect on yielded data; the only effect is the elimination of forced GC pauses, which is the intent.
