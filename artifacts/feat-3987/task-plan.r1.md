# Deduplicate Shoptet Order-Status ID Constants Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate the duplicate declaration of three Shoptet order-status ID constants (`DefaultSourceStateId = -2`, `DefaultDesiredStateId = 26`, `DefaultNoteStateId = 35`) that currently exist byte-for-byte identically on both `ExpeditionPickingRequest` (ExpeditionList module) and `PrintPickingListRequest` (Logistics module), by making `ExpeditionPickingRequest` the sole owner and having `PrintPickingListRequest` reference it.

**Architecture:** `ExpeditionPickingRequest` (`Application/Features/ExpeditionList/Contracts/`) becomes the single declaration site for all three constants, gaining the two Shoptet-status-name comments (`Vyrizuje se`, `Bali se`) that today only exist on `PrintPickingListRequest`'s copies. `PrintPickingListRequest` (`Application/Features/Logistics/Picking/`) drops its own `const` declarations and instead references `ExpeditionPickingRequest`'s constants for its own property-default initializers, via a `using Anela.Heblo.Application.Features.ExpeditionList.Contracts;` import — reusing the `Logistics → ExpeditionList.Contracts` dependency direction that `LogisticsExpeditionPickingAdapter.cs` already established and that `ModuleBoundariesTests.cs` already permits (it only guards the reverse `ExpeditionList → Logistics` direction). The one test file that references the removed constants by their old declaring type (`PickingListIntegrationTests.cs`) is retargeted to `ExpeditionPickingRequest` **before** the constants are removed from `PrintPickingListRequest`, so the build never goes red at an intermediate commit. No behavior, DTO shape, or public contract changes anywhere.

**Tech Stack:** .NET 8, C# 12, xUnit, FluentAssertions. Backend-only (`Anela.Heblo.Application`, `Anela.Heblo.Tests`, `Anela.Heblo.Adapters.Shoptet.Tests` projects). No migrations, no API surface change, no OpenAPI regen, no frontend impact.

---

## File Structure

This change touches exactly three existing files. No new files, no renames, no project reference changes.

```
backend/
├── src/Anela.Heblo.Application/Features/
│   ├── ExpeditionList/Contracts/
│   │   └── ExpeditionPickingRequest.cs      ← ADD two inline comments (no structural change) — becomes sole owner
│   └── Logistics/Picking/
│       └── PrintPickingListRequest.cs       ← REMOVE 3 const lines + 1 stray commented-out line;
│                                                ADD `using ExpeditionList.Contracts` (with breadcrumb comment);
│                                                CHANGE 3 property-default expressions
└── test/
    └── Anela.Heblo.Adapters.Shoptet.Tests/Integration/
        └── PickingListIntegrationTests.cs   ← RETARGET 2 constant references + 1 comment to ExpeditionPickingRequest
```

**Unchanged (verify only, do not touch):**
- `backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapter.cs` — already imports `ExpeditionList.Contracts`; builds `PrintPickingListRequest` by copying `ExpeditionPickingRequest`'s **runtime property values**, not the default constants. No line in this file references `DefaultSourceStateId`, `DefaultDesiredStateId`, or `DefaultNoteStateId` by name.
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapterTests.cs` — never references the removed constants by name; used only as a regression safety net (must keep passing).
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/PrintExpeditionOrderHandlerTests.cs` — contains a test **method name** (`Handle_NonDefaultDesiredStateId_ChecksConfiguredValueNotHardcoded26`) that superficially string-matches `DefaultDesiredStateId`; it is not an actual reference to the constant and needs no change (confirmed by direct inspection — see Task 3 Step 1).
- `docs/superpowers/plans/2026-06-14-expedition-address-validation.md`, `docs/superpowers/plans/2026-06-11-remove-print-picking-list-default-carriers.md`, `docs/superpowers/plans/2026-06-02-relocate-picking-dtos-to-application.md` — historical planning documents that happen to mention these constants; out of scope per spec (point-in-time records, not living documentation).

**Task ordering matters:** the test must be retargeted (Task 2) **before** the constants are deleted from `PrintPickingListRequest` (Task 3), otherwise the build breaks between the two edits (`CS0117 'PrintPickingListRequest' does not contain a definition for 'DefaultSourceStateId'`). Task 1 (adding comments to `ExpeditionPickingRequest.cs`) has no ordering dependency and can safely go first.

---

### task: add-shoptet-status-comments-to-expedition-picking-request

## Goal

Add the two missing Shoptet-status-name comments to `ExpeditionPickingRequest.cs` so that, once it becomes the sole declaration site (Task 3), none of the domain-meaning documentation carried today only by `PrintPickingListRequest`'s copies is lost (spec FR-3).

## Files to change

**Edit:**
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingRequest.cs`

**Verify only, no change expected:**
- None for this task.

**Do not touch:**
- `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs` — its own copies of these comments are removed in Task 3, not this task; touching it now would leave the comments duplicated for one commit, which is harmless but out of scope for this task's single responsibility.

## Steps

- [ ] **Step 1: Confirm current file content (baseline)**

Run:
```bash
cd /home/user/worktrees/feature-3987-Arch-Review-Expeditionlist-Shoptet-State-Id-Consta && grep -n "DefaultSourceStateId\|DefaultDesiredStateId\|DefaultNoteStateId" backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingRequest.cs
```

Expected output (no comments yet on the first two lines):
```
7:    public const int DefaultSourceStateId = -2;
8:    public const int DefaultDesiredStateId = 26;
9:    public const int DefaultNoteStateId = 35; // Poznámka — orders with incomplete address
12:    public int SourceStateId { get; set; } = DefaultSourceStateId;
13:    public int DesiredStateId { get; set; } = DefaultDesiredStateId;
14:    public int NoteStateId { get; set; } = DefaultNoteStateId;
```

This confirms the two constants that need comments (lines 7-8) and that `DefaultNoteStateId`'s existing comment (line 9) must be left exactly as-is.

- [ ] **Step 2: Add the two Shoptet-status-name comments**

Use the Edit tool on `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingRequest.cs`.

Change:
```csharp
    public const int DefaultSourceStateId = -2;
    public const int DefaultDesiredStateId = 26;
    public const int DefaultNoteStateId = 35; // Poznámka — orders with incomplete address
```

to:
```csharp
    public const int DefaultSourceStateId = -2; // Vyrizuje se
    public const int DefaultDesiredStateId = 26; // Bali se
    public const int DefaultNoteStateId = 35; // Poznámka — orders with incomplete address
```

The comment text (`Vyrizuje se`, `Bali se`) is copied verbatim from `PrintPickingListRequest.cs`'s existing comments (line 7 and line 9 there) — this is the domain documentation being preserved, not new wording. Do not change the `DefaultNoteStateId` line.

- [ ] **Step 3: Build to confirm the comment-only change compiles**

Run:
```bash
cd /home/user/worktrees/feature-3987-Arch-Review-Expeditionlist-Shoptet-State-Id-Consta && dotnet build Anela.Heblo.sln
```

Expected: build succeeds with no new errors or warnings. Comments have zero effect on compiled output, so this should be unconditionally green.

- [ ] **Step 4: Verify the comments landed correctly**

Run:
```bash
cd /home/user/worktrees/feature-3987-Arch-Review-Expeditionlist-Shoptet-State-Id-Consta && grep -n "DefaultSourceStateId\|DefaultDesiredStateId\|DefaultNoteStateId" backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingRequest.cs
```

Expected output:
```
7:    public const int DefaultSourceStateId = -2; // Vyrizuje se
8:    public const int DefaultDesiredStateId = 26; // Bali se
9:    public const int DefaultNoteStateId = 35; // Poznámka — orders with incomplete address
12:    public int SourceStateId { get; set; } = DefaultSourceStateId;
13:    public int DesiredStateId { get; set; } = DefaultDesiredStateId;
14:    public int NoteStateId { get; set; } = DefaultNoteStateId;
```

- [ ] **Step 5: Commit**

```bash
cd /home/user/worktrees/feature-3987-Arch-Review-Expeditionlist-Shoptet-State-Id-Consta && git add backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingRequest.cs && git commit -m "$(cat <<'EOF'
docs(expedition-list): add Shoptet status-name comments to ExpeditionPickingRequest

Prepares ExpeditionPickingRequest to become the sole declaration site for
the three Shoptet order-status ID constants (next commits) without losing
the "Vyrizuje se" / "Bali se" documentation currently only present on
PrintPickingListRequest's soon-to-be-removed duplicate constants.

No behavior change.
EOF
)"
```

---

### task: retarget-picking-list-integration-test-to-expedition-picking-request

## Goal

Repoint `PickingListIntegrationTests.cs`'s two references to `PrintPickingListRequest.DefaultSourceStateId` / `.DefaultDesiredStateId` at `ExpeditionPickingRequest`'s constants, and update the adjacent explanatory comment — **before** those constants are removed from `PrintPickingListRequest` in Task 3, so the build stays green at every commit (spec FR-4).

## Files to change

**Edit:**
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs`

**Verify only, no change expected:**
- `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs` — still declares its own `DefaultSourceStateId` / `DefaultDesiredStateId` / `DefaultNoteStateId` at this point in the plan (removed in Task 3); this task must not touch it, since both the old and new constant references need to coexist and both compile identically until Task 3 removes the old ones.

**Do not touch:**
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapterTests.cs` — never references the removed constants by name.
- `backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapter.cs` — unaffected (copies runtime values, not the constants).

## Steps

- [ ] **Step 1: Confirm the current consumer sites (baseline)**

Run:
```bash
cd /home/user/worktrees/feature-3987-Arch-Review-Expeditionlist-Shoptet-State-Id-Consta && grep -n "PrintPickingListRequest\.Default\|^using" backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs
```

Expected output:
```
1:using Anela.Heblo.Adapters.Shoptet.Tests.Integration.Infrastructure;
2:using Anela.Heblo.Application.Features.ExpeditionList;
3:using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
4:using Anela.Heblo.Application.Features.ExpeditionList.Services;
5:using Anela.Heblo.Application.Features.ShoptetOrders;
6:using Anela.Heblo.Application.Shared.Printing;
7:using Anela.Heblo.Domain.Features.Logistics;
8:using Anela.Heblo.Application.Features.Logistics.Picking;
23:    private const int SourceStateId = PrintPickingListRequest.DefaultSourceStateId;
88:                DesiredStateId = PrintPickingListRequest.DefaultDesiredStateId,
```

Note `using Anela.Heblo.Application.Features.ExpeditionList.Contracts;` is already present (line 3) — the file already references `ExpeditionPickingRequest.DefaultCarriers` on line 89, so no new `using` is needed for this task.

- [ ] **Step 2: Retarget the comment and the `SourceStateId` constant reference**

Use the Edit tool on `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs`.

Change:
```csharp
    // Must match PrintPickingListRequest.DefaultSourceStateId (-2 = "Vyřizuje se").
    // Use statusId= query parameter (not status=) — the correct param name supports negative system IDs.
    private const int SourceStateId = PrintPickingListRequest.DefaultSourceStateId;
```

to:
```csharp
    // Sourced from ExpeditionPickingRequest.DefaultSourceStateId (-2 = "Vyřizuje se").
    // Use statusId= query parameter (not status=) — the correct param name supports negative system IDs.
    private const int SourceStateId = ExpeditionPickingRequest.DefaultSourceStateId;
```

The comment no longer describes a match between two separate declarations (there will only be one after Task 3), so it is reworded to describe the single source instead of a cross-class equivalence.

- [ ] **Step 3: Retarget the `DesiredStateId` constant reference**

In the same file, change:
```csharp
                DesiredStateId = PrintPickingListRequest.DefaultDesiredStateId,
```

to:
```csharp
                DesiredStateId = ExpeditionPickingRequest.DefaultDesiredStateId,
```

This is the line inside the `new PrintPickingListRequest { ... }` object initializer (directly above `Carriers = ExpeditionPickingRequest.DefaultCarriers,`, which is already correctly targeted and untouched).

- [ ] **Step 4: Build the test project to confirm it still compiles**

Run:
```bash
cd /home/user/worktrees/feature-3987-Arch-Review-Expeditionlist-Shoptet-State-Id-Consta && dotnet build backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj
```

Expected: build succeeds with no errors. At this point both `PrintPickingListRequest.DefaultSourceStateId`/`.DefaultDesiredStateId` (still declared) and `ExpeditionPickingRequest.DefaultSourceStateId`/`.DefaultDesiredStateId` exist and are equal (`-2`, `26`), so this is a safe intermediate state — the test now points at the soon-to-be-canonical source while the old declarations still exist as an (about-to-be-removed) safety net.

`PickingListIntegrationTests` itself is `[Trait("Category", "Integration")]` and requires `Shoptet:IsTestEnvironment=true` user secrets plus a live Shoptet store connection (see `ShoptetTestGuard.Assert` in the file) — it cannot and must not be executed as part of this validation. A successful build is the correct and sufficient signal here.

- [ ] **Step 5: Verify no stray references remain**

Run:
```bash
cd /home/user/worktrees/feature-3987-Arch-Review-Expeditionlist-Shoptet-State-Id-Consta && grep -n "PrintPickingListRequest\.Default" backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs
```

Expected output: empty (no matches) — both references now point at `ExpeditionPickingRequest`.

- [ ] **Step 6: Commit**

```bash
cd /home/user/worktrees/feature-3987-Arch-Review-Expeditionlist-Shoptet-State-Id-Consta && git add backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs && git commit -m "$(cat <<'EOF'
test(shoptet): retarget PickingListIntegrationTests to ExpeditionPickingRequest constants

Repoints the two remaining PrintPickingListRequest.DefaultSourceStateId /
.DefaultDesiredStateId references at ExpeditionPickingRequest's constants,
ahead of removing PrintPickingListRequest's own duplicate declarations in
the next commit. Keeps the build green at every commit in this sequence.

No behavior change — both constants currently hold identical values.
EOF
)"
```

---

### task: consolidate-print-picking-list-request-constants-onto-expedition-picking-request

## Goal

Remove `PrintPickingListRequest`'s duplicate `DefaultSourceStateId` / `DefaultDesiredStateId` / `DefaultNoteStateId` constant declarations (and the adjacent stray commented-out dead-code line), and have its `SourceStateId` / `DesiredStateId` / `NoteStateId` property defaults reference `ExpeditionPickingRequest`'s constants instead — completing the deduplication (spec FR-1, FR-2, FR-5).

## Files to change

**Edit:**
- `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs`

**Verify only, no change expected:**
- `backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapter.cs` — confirm it still contains no reference to `DefaultSourceStateId`/`DefaultDesiredStateId`/`DefaultNoteStateId` by name (it copies `ExpeditionPickingRequest`'s runtime property values, not the class-level constants) and needs no edit.
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapterTests.cs` — must pass unmodified after this change (regression safety net for FR-5).
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs` — already retargeted in the previous task; confirm it now compiles cleanly against the reduced `PrintPickingListRequest`.

**Do not touch:**
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingRequest.cs` — already updated in Task 1; its constants and their values are unchanged by this task.
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/PrintExpeditionOrderHandlerTests.cs` — its test method name superficially matches `DefaultDesiredStateId` as a substring but does not reference the constant; out of scope.

## Steps

- [ ] **Step 1: Confirm zero remaining references to the constants under their old declaring type, and confirm the `PrintExpeditionOrderHandlerTests.cs` false positive**

Run:
```bash
cd /home/user/worktrees/feature-3987-Arch-Review-Expeditionlist-Shoptet-State-Id-Consta && grep -rn "PrintPickingListRequest\.Default\(Source\|Desired\|Note\)StateId" --include='*.cs' backend/
```

Expected output: empty (no matches) — Task 2 already retargeted the one file that had them.

Run:
```bash
cd /home/user/worktrees/feature-3987-Arch-Review-Expeditionlist-Shoptet-State-Id-Consta && grep -n "DefaultDesiredStateId" backend/test/Anela.Heblo.Tests/Features/ExpeditionList/PrintExpeditionOrderHandlerTests.cs
```

Expected output:
```
106:    public async Task Handle_NonDefaultDesiredStateId_ChecksConfiguredValueNotHardcoded26()
```

Confirms this is a test **method name**, not a code reference to the constant — no change needed here.

- [ ] **Step 2: Establish the regression-test baseline before touching production code**

Run:
```bash
cd /home/user/worktrees/feature-3987-Arch-Review-Expeditionlist-Shoptet-State-Id-Consta && dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~LogisticsExpeditionPickingAdapterTests"
```

Expected: all tests in `LogisticsExpeditionPickingAdapterTests` PASS. This is the baseline green state the change must not break (spec FR-5's acceptance criterion: "all existing tests in `LogisticsExpeditionPickingAdapterTests.cs` pass unmodified").

- [ ] **Step 3: Remove the duplicate constants and stray commented-out line, add the `using` directive, retarget the property defaults**

Use the Edit tool on `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs`.

Current full file content:
```csharp
using Anela.Heblo.Domain.Features.Logistics;

namespace Anela.Heblo.Application.Features.Logistics.Picking;

public class PrintPickingListRequest
{
    public const int DefaultSourceStateId = -2; // Vyrizuje se
    //private const string DesiredStateId = "26"; // Bali se
    public const int DefaultDesiredStateId = 26; // Bali se
    public const int DefaultNoteStateId = 35; // Poznámka — orders with incomplete address

    public IList<Carriers> Carriers { get; set; } = new List<Carriers>();
    public int SourceStateId { get; set; } = DefaultSourceStateId;
    public int DesiredStateId { get; set; } = DefaultDesiredStateId;
    public int NoteStateId { get; set; } = DefaultNoteStateId;
    public bool ChangeOrderState { get; set; }
    public bool SendToPrinter { get; set; }
    public string? OrderCode { get; set; }
}
```

Replace it in full with:
```csharp
using Anela.Heblo.Domain.Features.Logistics;
// Sources its state-ID defaults from ExpeditionList's contract (ExpeditionPickingRequest) —
// see LogisticsExpeditionPickingAdapter.cs / LogisticsModule.cs for the established
// provider-depends-on-consumer-contract pattern this follows.
using Anela.Heblo.Application.Features.ExpeditionList.Contracts;

namespace Anela.Heblo.Application.Features.Logistics.Picking;

public class PrintPickingListRequest
{
    public IList<Carriers> Carriers { get; set; } = new List<Carriers>();
    public int SourceStateId { get; set; } = ExpeditionPickingRequest.DefaultSourceStateId;
    public int DesiredStateId { get; set; } = ExpeditionPickingRequest.DefaultDesiredStateId;
    public int NoteStateId { get; set; } = ExpeditionPickingRequest.DefaultNoteStateId;
    public bool ChangeOrderState { get; set; }
    public bool SendToPrinter { get; set; }
    public string? OrderCode { get; set; }
}
```

This removes all three `public const int Default*StateId` declarations, the stray `//private const string DesiredStateId = "26"; // Bali se` dead-code line, and retargets the three property defaults to `ExpeditionPickingRequest`'s constants. The `using Anela.Heblo.Domain.Features.Logistics;` directive is kept (still needed for the `Carriers` property type).

- [ ] **Step 4: Build the full solution**

Run:
```bash
cd /home/user/worktrees/feature-3987-Arch-Review-Expeditionlist-Shoptet-State-Id-Consta && dotnet build Anela.Heblo.sln
```

Expected: build succeeds with no errors and no new warnings. If you see `CS0117 'PrintPickingListRequest' does not contain a definition for 'DefaultSourceStateId'` (or `DefaultDesiredStateId`/`DefaultNoteStateId`), a consumer of the old declaration was missed — re-run the grep from Step 1 across the whole repo (not just `backend/`) and retarget it before re-attempting.

- [ ] **Step 5: Re-run the regression-test baseline to confirm no behavior change**

Run:
```bash
cd /home/user/worktrees/feature-3987-Arch-Review-Expeditionlist-Shoptet-State-Id-Consta && dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~LogisticsExpeditionPickingAdapterTests"
```

Expected: all tests PASS, identical result to Step 2. This confirms FR-5 (`LogisticsExpeditionPickingAdapter`'s field-by-field copy logic and all its tests are unaffected).

- [ ] **Step 6: Run the module-boundary architecture guard**

Run:
```bash
cd /home/user/worktrees/feature-3987-Arch-Review-Expeditionlist-Shoptet-State-Id-Consta && dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests"
```

Expected: all tests PASS. This confirms the new `using Anela.Heblo.Application.Features.ExpeditionList.Contracts;` in `PrintPickingListRequest.cs` does not trip the `ExpeditionList -> Logistics` boundary rule (that rule only inspects the reverse direction; per the architecture review, no allowlist change is required or expected).

- [ ] **Step 7: Run the full non-integration backend test suite**

Run:
```bash
cd /home/user/worktrees/feature-3987-Arch-Review-Expeditionlist-Shoptet-State-Id-Consta && dotnet test Anela.Heblo.sln --no-build --filter "Category!=Integration"
```

Expected: all tests PASS. This excludes `PickingListIntegrationTests` (requires live Shoptet test-store secrets, per its `[Trait("Category", "Integration")]`) while covering every other unit test in the solution, including the `ExpeditionList`, `Logistics`, and `ShoptetOrders` namespaces.

- [ ] **Step 8: Run `dotnet format` on the three changed files**

Run:
```bash
cd /home/user/worktrees/feature-3987-Arch-Review-Expeditionlist-Shoptet-State-Id-Consta && dotnet format Anela.Heblo.sln --include backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingRequest.cs backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs
```

Expected: completes with no formatting violations reported (or, if it reports fixes, re-run Step 4's build to confirm those fixes didn't change behavior, then fold the formatting fix into this commit).

- [ ] **Step 9: Confirm exactly one declaration of each constant remains, repo-wide**

Run:
```bash
cd /home/user/worktrees/feature-3987-Arch-Review-Expeditionlist-Shoptet-State-Id-Consta && grep -rn "public const int DefaultSourceStateId\|public const int DefaultDesiredStateId\|public const int DefaultNoteStateId" --include='*.cs' .
```

Expected output — exactly one match per constant, all three in `ExpeditionPickingRequest.cs`:
```
./backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingRequest.cs:    public const int DefaultSourceStateId = -2; // Vyrizuje se
./backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingRequest.cs:    public const int DefaultDesiredStateId = 26; // Bali se
./backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingRequest.cs:    public const int DefaultNoteStateId = 35; // Poznámka — orders with incomplete address
```

This is spec FR-1's acceptance criterion verified directly.

- [ ] **Step 10: Confirm `LogisticsExpeditionPickingAdapter.cs` required no change**

Run:
```bash
cd /home/user/worktrees/feature-3987-Arch-Review-Expeditionlist-Shoptet-State-Id-Consta && grep -n "DefaultSourceStateId\|DefaultDesiredStateId\|DefaultNoteStateId" backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapter.cs
```

Expected output: empty (no matches) — confirms it copies runtime property values, never the class-level constants, and genuinely needed zero changes.

- [ ] **Step 11: Commit**

Stage only the three intended files:
```bash
cd /home/user/worktrees/feature-3987-Arch-Review-Expeditionlist-Shoptet-State-Id-Consta && git add backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingRequest.cs backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs
```

(The latter two are no-ops if Steps 8's `dotnet format` made no changes to them — `git add` on an unchanged file is harmless.)

```bash
git commit -m "$(cat <<'EOF'
refactor(logistics): consolidate Shoptet order-status ID constants onto ExpeditionPickingRequest

PrintPickingListRequest duplicated ExpeditionPickingRequest's three Shoptet
order-status ID constants (DefaultSourceStateId=-2, DefaultDesiredStateId=26,
DefaultNoteStateId=35) byte-for-byte. ExpeditionPickingRequest is now the
sole declaration site; PrintPickingListRequest's property defaults reference
it instead, via the same Logistics -> ExpeditionList.Contracts dependency
direction LogisticsExpeditionPickingAdapter.cs already uses.

No behavior change. Effective defaults (-2, 26, 35) unchanged.
LogisticsExpeditionPickingAdapterTests and ModuleBoundariesTests pass
unmodified.
EOF
)"
```

---

## Self-Review

Checked the plan against the spec, architecture review, and design with fresh eyes:

- **FR-1 (single declaration of each state-ID constant)** — covered by Task 3 Step 3 (removal from `PrintPickingListRequest`) and Task 3 Step 9 (repo-wide grep confirming exactly one match per constant, all in `ExpeditionPickingRequest.cs`).
- **FR-2 (`PrintPickingListRequest` defaults reference the canonical constants, via the specified `using`)** — covered by Task 3 Step 3 (the `using Anela.Heblo.Application.Features.ExpeditionList.Contracts;` directive and the three retargeted property-default expressions). No new project/assembly reference needed — both types live in `Anela.Heblo.Application`, confirmed by the unchanged `<ProjectReference>` set (this task never touches any `.csproj`).
- **FR-3 (preserve domain-meaning comments, including removal of the stray dead-code line)** — covered by Task 1 (adds `// Vyrizuje se` / `// Bali se` to `ExpeditionPickingRequest.cs`, retains `DefaultNoteStateId`'s existing comment unchanged) and Task 3 Step 3 (the stray `//private const string DesiredStateId = "26"; // Bali se` line is removed as part of the same edit that removes its adjacent constant).
- **FR-4 (update dependent test code, including the explanatory comment)** — covered by Task 2 Steps 2-3 (both constant references retargeted) and the comment reworded from "Must match PrintPickingListRequest.DefaultSourceStateId..." to "Sourced from ExpeditionPickingRequest.DefaultSourceStateId...", reflecting there is now a single declaration rather than two that must match. Confirmed `LogisticsExpeditionPickingAdapterTests.cs` and `LogisticsExpeditionPickingAdapter.cs` need no changes (Task 3's "Verify only" file list and Step 10).
- **FR-5 (no behavior change)** — covered by Task 3 Steps 2 and 5 (before/after `LogisticsExpeditionPickingAdapterTests` run identically), Step 6 (`ModuleBoundariesTests` guard), and Step 7 (full non-integration suite).
- **Architecture review's one addition beyond the spec's literal text** (breadcrumb comment on the new `using` in `PrintPickingListRequest.cs`) — included verbatim in Task 3 Step 3.
- **Architecture review's validation gate** — `dotnet build` + `dotnet format` (Task 3 Steps 4 and 8), `LogisticsExpeditionPickingAdapterTests.cs` passing (Steps 2/5), `ModuleBoundariesTests.cs` passing with no allowlist change (Step 6), `PickingListIntegrationTests.cs` compile-only / not run against live Shoptet (Task 2 Step 4's explicit note).
- **Out of scope items honored:** no constant value changes, no renames, no changes to `LogisticsExpeditionPickingAdapter`'s mapping logic, no changes to `ShoptetOrdersSettings.cs` (different module, different mechanism — not referenced anywhere in this plan), no new shared constants location introduced, no edits to the historical `docs/superpowers/plans/*.md` files that mention these constants.
- **Placeholder scan** — no "TBD"/"implement later"/"add appropriate handling" language anywhere; every step shows the exact code, exact command, and exact expected output; no step says "similar to Task N" without repeating the actual content.
- **Type/name consistency across tasks** — `ExpeditionPickingRequest.DefaultSourceStateId`/`.DefaultDesiredStateId`/`.DefaultNoteStateId`, `PrintPickingListRequest.SourceStateId`/`.DesiredStateId`/`.NoteStateId`, namespaces (`Anela.Heblo.Application.Features.ExpeditionList.Contracts`, `Anela.Heblo.Application.Features.Logistics.Picking`), and file paths are identical across all three tasks and match the verified current source (read directly from the worktree, not assumed from the spec's line numbers — the spec's "line ~23" / "line ~88" estimates for `PickingListIntegrationTests.cs` matched the actual file exactly, and `PrintPickingListRequest.cs`'s current content matched the spec/arch-review's description exactly, including the stray commented-out line).

No gaps found. Plan is complete.
