# Remove Dead Mutable Static `PrintPickingListRequest.DefaultCarriers` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate the public-setter static `DefaultCarriers` property on `PrintPickingListRequest` (a process-wide mutation hazard and dead code in production) by deleting it and pointing the lone integration-test consumer at the canonical `ExpeditionPickingRequest.DefaultCarriers` constant.

**Architecture:** Two byte-identical static carrier lists currently exist: `ExpeditionPickingRequest.DefaultCarriers` (read-only, used by all production callers) and `PrintPickingListRequest.DefaultCarriers` (mutable, used only by one integration test). Remove the duplicate, retarget the test, leave the rest of `PrintPickingListRequest` untouched. Net effect: one source of truth, zero behavior change.

**Tech Stack:** .NET 8, C# 12, xUnit, FluentAssertions, MediatR. Backend-only — no migrations, no API surface change, no OpenAPI regen, no frontend impact.

---

## File Structure

This change touches exactly two existing files. No new files, no renames, no project reference changes.

```
backend/
├── src/Anela.Heblo.Application/Features/Logistics/Picking/
│   └── PrintPickingListRequest.cs            ← DELETE the static DefaultCarriers property (lines 16-22)
└── test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/
    └── PickingListIntegrationTests.cs        ← Add using directive + retarget line 88
```

**Unchanged (do not touch):**
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingRequest.cs` — remains the single source of truth for the default carrier set.
- All other members of `PrintPickingListRequest` (the `DefaultSourceStateId`, `DefaultDesiredStateId` consts and instance properties).
- `PrintPickingListJob.cs`, `RunExpeditionListPrintFixHandler.cs`, and other ExpeditionList handlers — they already read `ExpeditionPickingRequest.DefaultCarriers`.

**Task ordering matters:** the test must be retargeted **before** the property is deleted, otherwise the build breaks between the two edits. Plan reflects this order.

---

### Task 1: Retarget the integration test to `ExpeditionPickingRequest.DefaultCarriers`

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs:7` (add a `using` directive)
- Modify: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs:88` (retarget the constant)

**Why first:** Removing `PrintPickingListRequest.DefaultCarriers` before this edit causes `CS0117 'PrintPickingListRequest' does not contain a definition for 'DefaultCarriers'` at build time. Retarget first, then delete.

- [ ] **Step 1: Confirm the current consumer site**

Open `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs` and verify:
- Line 7 currently reads: `using Anela.Heblo.Application.Features.Logistics.Picking;`
- Line 88 currently reads: `                Carriers = PrintPickingListRequest.DefaultCarriers,`

This is the only file that needs updating (verified by repo-wide grep — see Task 3 Step 3).

- [ ] **Step 2: Add the `using` directive for `ExpeditionPickingRequest`**

`ExpeditionPickingRequest` lives in the `Anela.Heblo.Application.Features.ExpeditionList.Contracts` sub-namespace. The test file already has `using Anela.Heblo.Application.Features.ExpeditionList;` (line 2) and `using Anela.Heblo.Application.Features.ExpeditionList.Services;` (line 3), but **not** the `.Contracts` sub-namespace. Add it.

Use the Edit tool to insert the new `using` after the existing `ExpeditionList` usings. The block at the top of the file changes from:

```csharp
using Anela.Heblo.Adapters.Shoptet.Tests.Integration.Infrastructure;
using Anela.Heblo.Application.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Application.Features.Logistics.Picking;
```

to:

```csharp
using Anela.Heblo.Adapters.Shoptet.Tests.Integration.Infrastructure;
using Anela.Heblo.Application.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Application.Features.Logistics.Picking;
```

Do **not** remove `using Anela.Heblo.Application.Features.Logistics.Picking;` — it is still required because the test continues to reference `PrintPickingListRequest` itself (lines 22, 84, 87) and `PrintPickingListOptions` (line 40).

- [ ] **Step 3: Retarget the carrier-list reference**

Change line 88 of the same file from:

```csharp
                Carriers = PrintPickingListRequest.DefaultCarriers,
```

to:

```csharp
                Carriers = ExpeditionPickingRequest.DefaultCarriers,
```

This is the only line that needs changing on the consumer side. Surrounding lines (`SourceStateId`, `DesiredStateId`, `ChangeOrderState`, `SendToPrinter`) stay exactly as they are.

- [ ] **Step 4: Build to confirm the retarget compiles**

Run:

```bash
cd backend && dotnet build Anela.Heblo.sln
```

Expected: build succeeds with no new errors or warnings. Both `PrintPickingListRequest.DefaultCarriers` and `ExpeditionPickingRequest.DefaultCarriers` still exist at this point (both compile), so the test file uses the new one and the old property is still defined but now has zero consumers. This is the intermediate green state before the deletion in Task 2.

- [ ] **Step 5: Verify the test class still loads correctly**

Run the integration-test project's build target specifically:

```bash
cd backend && dotnet build test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj
```

Expected: build succeeds. We do **not** run the integration test itself here — `PickingListIntegrationTests` requires `Shoptet:IsTestEnvironment=true` user secrets and hits a live test store (see the `ShoptetTestGuard.Assert` call at line 55). It is excluded from CI via `[Trait("Category", "Integration")]` and is run manually per the file's docstring. Build success is the meaningful signal at this step.

---

### Task 2: Delete `PrintPickingListRequest.DefaultCarriers`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs:16-22` (delete the static property)

**Why this is safe now:** After Task 1, no source file references `PrintPickingListRequest.DefaultCarriers`. Deleting it cannot break the build.

- [ ] **Step 1: Confirm zero remaining references before deletion**

Run the repo-wide search:

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feat-arch-review-expeditionlist-logistics-pri && grep -rn 'PrintPickingListRequest\.DefaultCarriers' --include='*.cs'
```

Expected output: empty (no matches). If anything other than the test file (which was retargeted in Task 1) appears, stop and investigate before deleting.

- [ ] **Step 2: Delete the property**

Open `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs`. The current full file is:

```csharp
using Anela.Heblo.Domain.Features.Logistics;

namespace Anela.Heblo.Application.Features.Logistics.Picking;

public class PrintPickingListRequest
{
    public const int DefaultSourceStateId = -2; // Vyrizuje se
    //private const string DesiredStateId = "26"; // Bali se
    public const int DefaultDesiredStateId = 26; // Bali se

    public IList<Carriers> Carriers { get; set; } = new List<Carriers>();
    public int SourceStateId { get; set; } = DefaultSourceStateId;
    public int DesiredStateId { get; set; } = DefaultDesiredStateId;
    public bool ChangeOrderState { get; set; }

    public static IList<Carriers> DefaultCarriers { get; set; } = new List<Carriers>()
    {
        Anela.Heblo.Domain.Features.Logistics.Carriers.Zasilkovna,
        Anela.Heblo.Domain.Features.Logistics.Carriers.GLS,
        Anela.Heblo.Domain.Features.Logistics.Carriers.PPL,
        Anela.Heblo.Domain.Features.Logistics.Carriers.Osobak
    };

    public bool SendToPrinter { get; set; }
}
```

Delete lines 16-22 inclusive — the `public static IList<Carriers> DefaultCarriers { get; set; } = new List<Carriers>() { ... };` block — **and** the blank line that separates it from `public bool SendToPrinter { get; set; }`. Use the Edit tool with the exact `old_string` (the seven-line property block plus its trailing blank line) and an empty `new_string`.

Resulting file must be exactly:

```csharp
using Anela.Heblo.Domain.Features.Logistics;

namespace Anela.Heblo.Application.Features.Logistics.Picking;

public class PrintPickingListRequest
{
    public const int DefaultSourceStateId = -2; // Vyrizuje se
    //private const string DesiredStateId = "26"; // Bali se
    public const int DefaultDesiredStateId = 26; // Bali se

    public IList<Carriers> Carriers { get; set; } = new List<Carriers>();
    public int SourceStateId { get; set; } = DefaultSourceStateId;
    public int DesiredStateId { get; set; } = DefaultDesiredStateId;
    public bool ChangeOrderState { get; set; }
    public bool SendToPrinter { get; set; }
}
```

Do **not** delete or modify:
- The `using Anela.Heblo.Domain.Features.Logistics;` directive (still needed by the `Carriers` property type).
- The `DefaultSourceStateId` and `DefaultDesiredStateId` consts (used by the integration test at lines 22 and 87).
- Any instance property.
- The commented-out line 8 (`//private const string DesiredStateId = "26"; // Bali se`) — not in scope, surgical change only.

- [ ] **Step 3: Build the full solution**

Run:

```bash
cd backend && dotnet build Anela.Heblo.sln
```

Expected: build succeeds with no errors and no new warnings (the existing baseline warning count is unchanged). If you see `CS0117 'PrintPickingListRequest' does not contain a definition for 'DefaultCarriers'`, a consumer was missed in Task 1 — go back, find it with grep, and retarget it before re-attempting the delete.

- [ ] **Step 4: Verify the canonical source is untouched**

Run:

```bash
grep -n 'DefaultCarriers' backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingRequest.cs
```

Expected output:

```
16:    public static IList<Carriers> DefaultCarriers { get; } = new List<Carriers>
```

(Line 16, read-only — the canonical declaration unchanged.) This confirms the single source of truth is intact.

---

### Task 3: Full verification and commit

**Files:** None modified — verification only, then a single commit covering Tasks 1 and 2.

- [ ] **Step 1: Re-run repo-wide search to confirm zero `PrintPickingListRequest.DefaultCarriers` references**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feat-arch-review-expeditionlist-logistics-pri && grep -rn 'PrintPickingListRequest\.DefaultCarriers' --include='*.cs'
```

Expected output: empty (no matches). This is FR-1 acceptance criterion #2 from the spec.

- [ ] **Step 2: Run `dotnet format` on the two modified files**

```bash
cd backend && dotnet format Anela.Heblo.sln --include src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs
```

Expected: completes silently or reports formatting changes applied. If it makes changes, those are part of the same commit.

- [ ] **Step 3: Run the unit-test suites that exercise the touched code paths**

The integration test itself cannot run in CI (it requires Shoptet user secrets), but the unit-test projects that touch `PrintPickingListJob`, `RunExpeditionListPrintFixHandler`, and ExpeditionList contracts must pass. Run:

```bash
cd backend && dotnet test Anela.Heblo.sln --no-build --filter "Category!=Integration"
```

Expected: all tests pass. The `Category!=Integration` filter skips `PickingListIntegrationTests` (it has `[Trait("Category", "Integration")]` and would fail without the live test store) while still running every unit test in the solution. If anything in the ExpeditionList or Picking namespaces fails, investigate — but per the architecture review there should be no behavior change.

- [ ] **Step 4: Confirm `ExpeditionPickingRequest.DefaultCarriers` still contains the same four carriers**

```bash
grep -A 5 'public static IList<Carriers> DefaultCarriers' backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingRequest.cs
```

Expected output contains the four carrier enum values in this order: `Zasilkovna, GLS, PPL, Osobak`. This is FR-3 acceptance criterion #3 — manual review that runtime carrier set is unchanged.

- [ ] **Step 5: Stage and commit**

Stage only the two intended files (no `-A` / no `.`) to avoid accidentally committing unrelated changes:

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feat-arch-review-expeditionlist-logistics-pri && git add backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs
```

Then commit with a conventional-commit message:

```bash
git commit -m "$(cat <<'EOF'
refactor(logistics): remove dead mutable PrintPickingListRequest.DefaultCarriers

The static property had a public setter (process-wide mutation hazard) and
duplicated the read-only ExpeditionPickingRequest.DefaultCarriers that all
production callers (PrintPickingListJob, RunExpeditionListPrintFixHandler)
already use. The lone consumer — PickingListIntegrationTests — now reads
the canonical constant directly, so the test exercises the same default
carrier set the production path uses.

No behavior change. Carrier set { Zasilkovna, GLS, PPL, Osobak } unchanged.
EOF
)"
```

- [ ] **Step 6: Verify the commit shows exactly two files changed**

```bash
git show --stat HEAD
```

Expected: two files in the diffstat — `PrintPickingListRequest.cs` (lines removed) and `PickingListIntegrationTests.cs` (small +/- around the `using` block and line 88). Any other file appearing means staging picked up unintended changes — investigate before pushing.

---

## Self-Review

Checked the plan against the spec and architecture review:

- **FR-1 (remove the property)** — covered by Task 2 Step 2 (delete) and Task 3 Step 1 (repo-wide grep).
- **FR-2 (retarget the integration test)** — covered by Task 1 Steps 2-3, including the explicit `using Anela.Heblo.Application.Features.ExpeditionList.Contracts;` directive the arch review called out.
- **FR-3 (no behavioral change in production)** — covered by Task 2 Step 3 (full solution build), Task 3 Step 3 (unit test run), and Task 3 Step 4 (manual verification of the canonical carrier set).
- **NFR-3 (single source of truth)** — guaranteed by the deletion plus Task 2 Step 4 confirming `ExpeditionPickingRequest.DefaultCarriers` is untouched.
- **Out of scope items** (broader `PrintPickingListRequest` refactor, contract consolidation, default carrier value changes) — none introduced.
- **Risk #2 from arch review** (forgotten `using` directive breaking the build) — addressed by Task 1 Step 4 building immediately after the retarget.
- **Risk #3** (someone re-adds the property later) — outside this plan's scope; relies on PR review per the arch review's mitigation.
- **Ordering trap** (deleting before retargeting breaks the build) — explicitly addressed by Task 1 → Task 2 order and called out in both task preambles.
- **Placeholders** — none. Every code block is the actual code; every command is the exact command to run; every "expected" line is the concrete expected result.
- **Type/name consistency** — `DefaultCarriers`, `ExpeditionPickingRequest`, `PrintPickingListRequest`, namespace strings, file paths, and the `Carriers` enum all match between tasks and match the actual files inspected.

Plan is complete.
