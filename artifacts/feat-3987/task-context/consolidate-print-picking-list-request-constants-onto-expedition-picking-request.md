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
