# Remove Unused `Platform` Field from `MarketingTransaction` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the dead `Platform` property from the `MarketingTransaction` domain DTO and every write/read site, eliminating a misleading affordance from the MarketingInvoices module while keeping import behavior byte-identical.

**Architecture:** Compiler-driven dead-code removal in the Domain layer's in-memory DTO. The persisted entity `ImportedMarketingTransaction.Platform` is unchanged — it is populated from `source.Platform` (interface-level value), which remains authoritative. No migration, no API contract change, no frontend impact.

**Tech Stack:** C# / .NET 8, xUnit, FluentAssertions, EF Core (read-only impact verification).

**TDD note:** This is a refactor with **no behavior change**, so there is no new failing test to write. The existing test suite is the safety net — every task validates by running the suite and confirming it still passes (green → green). The change progresses from leaves to root (writes → property declaration) so the build stays green between every step.

---

## File Touch Map

| Path | Change |
|---|---|
| `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsTransactionSource.cs` | Remove `Platform = Platform,` initializer line. |
| `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsTransactionSource.cs` | Remove `Platform = Platform,` initializer line. |
| `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs` | Remove `Platform = "TestPlatform",` from 12 `MarketingTransaction` initializer sites. |
| `backend/test/Anela.Heblo.Tests/Adapters/MetaAds/MetaAdsTransactionSourceTests.cs` | Delete `tx.Platform.Should().Be("MetaAds");` assertion. |
| `backend/test/Anela.Heblo.Tests/Adapters/GoogleAds/GoogleAdsTransactionSourceTests.cs` | Delete `tx.Platform.Should().Be("GoogleAds");` assertion. |
| `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/MarketingTransaction.cs` | Delete `Platform` property declaration. |

**Do NOT touch:**
- `IMarketingTransactionSource` — interface-level `Platform` property stays (`source.Platform` is what the service actually reads).
- `ImportedMarketingTransaction` and `ImportedMarketingTransactionConfiguration` — DB column and index unchanged.
- `MarketingInvoiceImportService.cs` — already uses `source.Platform`; verify, don't edit.
- Adapter `Platform => PlatformName` properties on `MetaAdsTransactionSource` / `GoogleAdsTransactionSource` — these implement the interface and stay.
- `ImportMarketingInvoicesHandlerTests.cs`, `MetaAdsInvoiceImportJobTests.cs`, `GoogleAdsInvoiceImportJobTests.cs` — their `Platform =` references are on `ImportMarketingInvoicesRequest`/`Response`, not `MarketingTransaction`.

---

## Task 0: Verify Baseline

Establish that the suite is green on the current branch before changing anything.

**Files:** None.

- [ ] **Step 1: Build the backend solution**

Run from repo root:

```bash
dotnet build backend/Anela.Heblo.sln -warnaserror
```

Expected: build succeeds with zero errors/warnings. If warnings exist already on the branch, note them — they are pre-existing and not in scope.

- [ ] **Step 2: Run the targeted test slice**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~MarketingInvoices|FullyQualifiedName~MetaAds|FullyQualifiedName~GoogleAds" \
  --no-build
```

Expected: all tests pass. Record the count of passing tests (e.g., "N passed, 0 failed, 0 skipped") so you can confirm it does not regress after the refactor.

- [ ] **Step 3: Verify no pending EF model changes**

```bash
dotnet ef migrations has-pending-model-changes \
  --project backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj \
  --startup-project backend/src/Anela.Heblo/Anela.Heblo.csproj
```

Expected: "No model changes detected." (or equivalent). This sets a baseline so the post-change check is meaningful.

> If `dotnet ef` is not installed in the environment, fall back to `dotnet ef migrations list ...` and record the most-recent migration name. The relevant check is that this name does not change after the refactor.

---

## Task 1: Remove `Platform` Initializer from `MetaAdsTransactionSource`

Strip the write site in the Meta adapter. The domain property still exists, so the build stays green.

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsTransactionSource.cs:68`

- [ ] **Step 1: Edit the file**

Remove the single line at position 68 inside the `new MarketingTransaction { ... }` initializer.

**Before** (lines 65–74):

```csharp
results.Add(new MarketingTransaction
{
    TransactionId = item.Id,
    Platform = Platform,
    Amount = item.Amount / 100m,
    TransactionDate = txDate,
    Currency = item.Currency,
    Description = item.PaymentType,
    RawData = JsonSerializer.Serialize(item, JsonOptions),
});
```

**After**:

```csharp
results.Add(new MarketingTransaction
{
    TransactionId = item.Id,
    Amount = item.Amount / 100m,
    TransactionDate = txDate,
    Currency = item.Currency,
    Description = item.PaymentType,
    RawData = JsonSerializer.Serialize(item, JsonOptions),
});
```

> The adapter-level `Platform => PlatformName` property (member of the class, implementing `IMarketingTransactionSource.Platform`) is untouched — keep it.

- [ ] **Step 2: Verify file compiles**

```bash
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Anela.Heblo.Adapters.MetaAds.csproj
```

Expected: build succeeds.

- [ ] **Step 3: Run adapter tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~MetaAds" --no-build
```

Expected: all tests pass. (The `tx.Platform.Should().Be("MetaAds")` assertion still passes because the property is still on the class with its default value of `string.Empty`... wait — it asserts `"MetaAds"`, not the default. The assertion will now FAIL after this step because the initializer no longer sets it.)

> **Expected failure at this step:** the test `GetTransactionsAsync_Amount_ConvertedFromCentsToDecimal` (or whichever test contains the line 72 assertion) will fail with FluentAssertions reporting `Expected tx.Platform to be "MetaAds", but found ""`. **This is intentional and will be fixed in Task 4.** Do not commit yet.

---

## Task 2: Remove `Platform` Initializer from `GoogleAdsTransactionSource`

Mirror Task 1 for the Google adapter.

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsTransactionSource.cs:39`

- [ ] **Step 1: Edit the file**

Remove the single line at position 39 inside the `new MarketingTransaction { ... }` initializer.

**Before** (lines 36–45):

```csharp
var transactions = rows.Select(r => new MarketingTransaction
{
    TransactionId = r.Id,
    Platform = Platform,
    Amount = r.AmountServedMicros / 1_000_000m,
    TransactionDate = r.StartDate,
    Currency = r.CurrencyCode,
    Description = r.Name ?? "Google Ads billing period",
    RawData = JsonSerializer.Serialize(r),
}).ToList();
```

**After**:

```csharp
var transactions = rows.Select(r => new MarketingTransaction
{
    TransactionId = r.Id,
    Amount = r.AmountServedMicros / 1_000_000m,
    TransactionDate = r.StartDate,
    Currency = r.CurrencyCode,
    Description = r.Name ?? "Google Ads billing period",
    RawData = JsonSerializer.Serialize(r),
}).ToList();
```

> The class-level `Platform => PlatformName` property is untouched.

- [ ] **Step 2: Verify file compiles**

```bash
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Anela.Heblo.Adapters.GoogleAds.csproj
```

Expected: build succeeds.

- [ ] **Step 3: Note expected adapter test failure**

The Google adapter test on line 28 (`tx.Platform.Should().Be("GoogleAds");`) will now fail for the same reason as the Meta one. Do not run tests separately here — they will be fixed in Task 4 and validated then.

---

## Task 3: Clean Up `MarketingInvoiceImportServiceTests` Initializers

Remove every `Platform = "TestPlatform",` site in the service unit tests. There are 12 sites: 9 inline initializers and 3 multi-line block initializers (verified by grep).

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs`

Lines affected (per grep `Platform\s*=` against the file):
- Lines 38, 39, 74, 103, 104, 139, 140, 197, 198 — inline `new() { ..., Platform = "TestPlatform", ... }` literals (delete the substring `Platform = "TestPlatform", ` from each).
- Lines 237, 289, 342 — multi-line `new() { ... Platform = "TestPlatform", ... }` (delete the entire line plus its trailing newline).

- [ ] **Step 1: Edit inline sites (lines 38, 39, 74, 103, 104, 139, 140, 197, 198)**

For each of these lines the substring `Platform = "TestPlatform", ` (including the trailing comma + space) appears exactly once between `TransactionId = ...,` and `Amount = ...,`. Use Edit with `replace_all` against the exact substring to delete it in one operation.

**Exact substring to remove** (appears in 9 sites):

```
Platform = "TestPlatform", 
```

(Note the single trailing space before `Amount`.)

After the edit, each affected line should read:

```csharp
new() { TransactionId = "TX-001", Amount = 100m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
```

…and similarly for the other transaction IDs (`"TX-002"`, `"TX-DUP"`).

- [ ] **Step 2: Edit multi-line initializers (lines 237, 289, 342)**

Each of these sites is a property line of its own within a `new() { ... }` initializer. Delete the full line, including the leading whitespace and trailing comma+newline.

For line 237 — before:

```csharp
            {
                TransactionId = "TX-EUR-001",
                Platform = "TestPlatform",
                Amount = 123.45m,
                ...
            },
```

After:

```csharp
            {
                TransactionId = "TX-EUR-001",
                Amount = 123.45m,
                ...
            },
```

Apply the same deletion for the multi-line initializers around lines 289 (`TransactionId = "TX-BAD-001"`) and 342 (`TransactionId = "TX-WS-001"`).

- [ ] **Step 3: Verify the file is clean**

Run from repo root:

```bash
grep -n "Platform" backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs || echo "no matches"
```

Expected matches are limited to references on **other types**:
- `_mockRepository.Setup(x => x.ExistsAsync("TestPlatform", ...))` — argument string, not a property assignment. **Keep.**
- `v.ToString()!.Contains("TestPlatform")` inside a logger Verify — keep.
- `Mock<IMarketingTransactionSource>` setup where `source.Platform` is mocked (e.g., `_mockSource.Setup(x => x.Platform).Returns("TestPlatform")` if present) — keep.

There must be **zero** remaining occurrences of `Platform = "TestPlatform"` (the property-assignment form). If grep still shows any, repeat Step 1/Step 2 against the missed line.

- [ ] **Step 4: Build the test project**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: build succeeds.

---

## Task 4: Remove Adapter-Test Assertions on `tx.Platform`

Delete the two assertions that read `MarketingTransaction.Platform`. They assert a constant (`"MetaAds"` / `"GoogleAds"`) that is already exercised indirectly via `IMarketingTransactionSource.Platform` and adds no coverage.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Adapters/MetaAds/MetaAdsTransactionSourceTests.cs:72`
- Modify: `backend/test/Anela.Heblo.Tests/Adapters/GoogleAds/GoogleAdsTransactionSourceTests.cs:28`

- [ ] **Step 1: Delete the Meta assertion**

In `MetaAdsTransactionSourceTests.cs`, remove the entire line 72:

```csharp
        tx.Platform.Should().Be("MetaAds");
```

The surrounding assertions (`tx.TransactionId`, `tx.Amount`, `tx.Currency`, `tx.Description`, `tx.TransactionDate`) stay.

- [ ] **Step 2: Delete the Google assertion**

In `GoogleAdsTransactionSourceTests.cs`, remove the entire line 28:

```csharp
        tx.Platform.Should().Be("GoogleAds");
```

The surrounding assertions (`tx.TransactionId`, `tx.Amount`, `tx.Currency`, `tx.Description`, `tx.TransactionDate`) stay.

- [ ] **Step 3: Build the test project**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: build succeeds.

- [ ] **Step 4: Run the targeted test slice**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~MarketingInvoices|FullyQualifiedName~MetaAds|FullyQualifiedName~GoogleAds" \
  --no-build
```

Expected: all tests pass. **The count must match the Task 0 baseline minus zero** — no tests should be missing; the only changes were deletions of assertion lines, not whole tests.

> If a test fails here with `Platform` in the message, you missed a reference. Re-grep with `grep -n "tx\.Platform\|\.Platform\.Should" backend/test/Anela.Heblo.Tests/...` and clean up.

---

## Task 5: Delete the `Platform` Property from `MarketingTransaction`

With all writes and reads removed, deleting the property declaration is safe. The compiler will fail-fast on any stragglers.

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/MarketingTransaction.cs`

- [ ] **Step 1: Edit the file**

**Before:**

```csharp
namespace Anela.Heblo.Domain.Features.MarketingInvoices;

public class MarketingTransaction
{
    public string TransactionId { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? RawData { get; set; }
}
```

**After:**

```csharp
namespace Anela.Heblo.Domain.Features.MarketingInvoices;

public class MarketingTransaction
{
    public string TransactionId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? RawData { get; set; }
}
```

(Single-line deletion: `    public string Platform { get; set; } = string.Empty;`.)

- [ ] **Step 2: Build the whole solution**

```bash
dotnet build backend/Anela.Heblo.sln -warnaserror
```

Expected: build succeeds with zero errors. If you see any `CS0117: 'MarketingTransaction' does not contain a definition for 'Platform'`, the failing site was missed in Task 1–4 — fix it in the file pointed to by the compiler and rerun.

- [ ] **Step 3: Confirm `MarketingInvoiceImportService` still uses `source.Platform` (no edit required)**

Open `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs` and read around line 70. Verify that the assignment to `ImportedMarketingTransaction.Platform` reads `source.Platform`, not `tx.Platform` (it should already, per the spec and arch review). No edit; this is a sanity check.

If somehow `tx.Platform` is referenced anywhere in this file, the build in Step 2 would have failed — so a passing build is the actual proof. Step 3 just documents the invariant for the reviewer.

---

## Task 6: Final Validation, Format, Commit

Comprehensive validation against the spec's acceptance criteria.

**Files:** All changes from Tasks 1–5.

- [ ] **Step 1: Format touched files**

```bash
dotnet format backend/Anela.Heblo.sln \
  --include backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/MarketingTransaction.cs \
            backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsTransactionSource.cs \
            backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsTransactionSource.cs \
            backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs \
            backend/test/Anela.Heblo.Tests/Adapters/MetaAds/MetaAdsTransactionSourceTests.cs \
            backend/test/Anela.Heblo.Tests/Adapters/GoogleAds/GoogleAdsTransactionSourceTests.cs
```

Expected: no output (or "Format complete"). If the formatter rewrites any of the touched files, `git diff` should still show only the property/initializer/assertion deletions plus, at worst, whitespace normalization.

- [ ] **Step 2: Full backend build with warnings as errors**

```bash
dotnet build backend/Anela.Heblo.sln -warnaserror
```

Expected: build succeeds, zero warnings introduced by this change.

- [ ] **Step 3: Full test run (all backend tests)**

```bash
dotnet test backend/Anela.Heblo.Tests --no-build
```

Or, more targeted but still comprehensive:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build
```

Expected: all tests pass. Count must equal the Task 0 baseline (we removed two adapter-test assertions but no `[Fact]`/`[Theory]` declarations, so the test count is unchanged).

- [ ] **Step 4: Verify no EF migration is pending**

```bash
dotnet ef migrations has-pending-model-changes \
  --project backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj \
  --startup-project backend/src/Anela.Heblo/Anela.Heblo.csproj
```

Expected: "No model changes detected." — matches Task 0, confirms the persisted `ImportedMarketingTransaction` schema is untouched.

- [ ] **Step 5: Grep for leftover references to the removed property**

```bash
grep -rn "MarketingTransaction.*Platform\|tx\.Platform\|transaction\.Platform" \
  backend/src backend/test --include="*.cs"
```

Expected: no matches in production code or tests for `tx.Platform` / `transaction.Platform`. The `MarketingTransaction.*Platform` pattern may still match incidentally on unrelated whitespace (e.g. comments mentioning the change in commit history aren't grepped from source — only `.cs` files); inspect any hits and confirm they're false positives (e.g. references to `ImportMarketingInvoicesRequest.Platform` or `ImportMarketingInvoicesResponse.Platform`, which are different types).

- [ ] **Step 6: Inspect the staged diff**

```bash
git diff --stat
git diff
```

Expected diff scope (6 files, ~17 line deletions, 0 additions outside whitespace):

| File | Approx. line delta |
|---|---|
| `MarketingTransaction.cs` | −1 |
| `MetaAdsTransactionSource.cs` | −1 |
| `GoogleAdsTransactionSource.cs` | −1 |
| `MarketingInvoiceImportServiceTests.cs` | −12 (9 inline edits + 3 line deletions) |
| `MetaAdsTransactionSourceTests.cs` | −1 |
| `GoogleAdsTransactionSourceTests.cs` | −1 |

If any other file is modified, investigate: was `dotnet format` overly aggressive, or did an edit slip into the wrong file?

- [ ] **Step 7: Commit**

Single atomic commit — the changes form one logical refactor.

```bash
git add backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/MarketingTransaction.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsTransactionSource.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsTransactionSource.cs \
        backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs \
        backend/test/Anela.Heblo.Tests/Adapters/MetaAds/MetaAdsTransactionSourceTests.cs \
        backend/test/Anela.Heblo.Tests/Adapters/GoogleAds/GoogleAdsTransactionSourceTests.cs

git commit -m "$(cat <<'EOF'
refactor: remove unused Platform field from MarketingTransaction

The per-transaction Platform property on MarketingTransaction was written
by both adapters (MetaAds, GoogleAds) but never read by any consumer.
MarketingInvoiceImportService.ImportAsync uses source.Platform
(IMarketingTransactionSource.Platform) when persisting
ImportedMarketingTransaction — that path is unchanged.

- Drop the Platform property from the domain DTO.
- Drop Platform = Platform initializers in MetaAds and GoogleAds adapters.
- Drop Platform = "TestPlatform" initializers in MarketingInvoiceImportServiceTests.
- Drop tx.Platform assertions in MetaAds/GoogleAds adapter tests (they
  asserted a constant already covered by source.Platform).

No public API, persisted schema, or OpenAPI client is affected. The
ImportedMarketingTransaction column and its Platform/TransactionId index
remain unchanged.
EOF
)"
```

- [ ] **Step 8: Confirm commit and clean tree**

```bash
git status
git log -1 --stat
```

Expected: clean working tree, the new commit lists the six files above.

---

## Acceptance Criteria Cross-Check

Mapping each spec requirement to the task that implements it:

| Spec | Task |
|---|---|
| FR-1: Remove `Platform` property from `MarketingTransaction` | Task 5 |
| FR-2: Remove `Platform` initializer from `MetaAdsTransactionSource` | Task 1 |
| FR-3: Remove `Platform` initializer from `GoogleAdsTransactionSource` | Task 2 |
| FR-4: Clean up test data that sets the removed property | Tasks 3, 4 |
| FR-5: Preserve import behavior end-to-end | Task 6, Steps 3–6 (passing test suite + diff inspection) |
| NFR-4: Backwards compatibility (no migration, no API change) | Task 6, Steps 4–5 |

## Risks (from arch review) — Mitigation Status

- *A consumer outside the explored set reads `MarketingTransaction.Platform`.* → Task 5 Step 2 fails the build fast on any missed read; the grep in Task 6 Step 5 confirms zero stragglers.
- *Adapter unit-tests assert `tx.Platform` and would fail.* → Known; addressed in Task 4.
- *Accidental EF model change* → Task 6 Step 4 explicitly checks for pending model changes.
- *Hidden serialization of `MarketingTransaction`* → Arch review's grep showed none; passing tests in Task 6 are the runtime check.
