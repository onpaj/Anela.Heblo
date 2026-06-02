# Relocate PurchaseOrdersInTransitTile to Purchase Module Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `PurchaseOrdersInTransitTile` from `Features/Dashboard/Tiles/` to `Features/Purchase/DashboardTiles/` and shift its DI registration from `DashboardModule` to `PurchaseModule`, eliminating the cross-module compile-time edge from Dashboard onto `Anela.Heblo.Domain.Features.Purchase`.

**Architecture:** Pure source-tree refactor. No behavior, API, payload, tile id, frontend, or database changes. The tile keeps its `[TileId("purchaseordersintransit")]`, constructor signature `(IPurchaseOrderRepository)`, and `LoadDataAsync` body verbatim. Only the C# namespace and the module that calls `RegisterTile<T>()` change. The contract test `TileIdContractTests.cs:15` uses the old fully-qualified type as an assembly-scanning marker and must be repointed to `LowStockEfficiencyTile` (already a canonical tile in the same assembly) so the test compiles after the move.

**Tech Stack:** .NET 8, C#, MediatR (not used by this tile), xUnit + FluentAssertions for tests, existing `RegisterTile<T>()` extension from `Anela.Heblo.Xcc.Services.Dashboard`, `dotnet build` / `dotnet format` / `dotnet test` for validation.

---

## File Structure

This refactor touches exactly six paths and produces a single logical commit.

| Action | Path | Responsibility |
|---|---|---|
| **Create** | `backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/PurchaseOrdersInTransitTile.cs` | The relocated tile. New namespace, otherwise byte-identical body to the old file. |
| **Delete** | `backend/src/Anela.Heblo.Application/Features/Dashboard/Tiles/PurchaseOrdersInTransitTile.cs` | Old location — must be removed so two copies of the same class do not coexist. |
| **Delete (empty folder)** | `backend/src/Anela.Heblo.Application/Features/Dashboard/Tiles/` | The tile is the only file in this folder; an empty `Tiles/` directory misleadingly suggests Dashboard-owned tiles still exist. |
| **Edit** | `backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs` | Remove the `using` for `Features.Dashboard.Tiles`, remove the `RegisterTile<PurchaseOrdersInTransitTile>()` call, and remove the now-dead `// Register dashboard tiles` comment. |
| **Edit** | `backend/src/Anela.Heblo.Application/Features/Purchase/PurchaseModule.cs` | Add `services.RegisterTile<PurchaseOrdersInTransitTile>();` immediately after the existing `LowStockEfficiencyTile` registration. No new `using` needed — the import is already present. |
| **Edit** | `backend/test/Anela.Heblo.Tests/Features/Dashboard/TileIdContractTests.cs` | Swap the assembly-marker `typeof()` from the relocated tile to `LowStockEfficiencyTile` so the test still compiles and continues to scan the same `Anela.Heblo.Application` assembly. |

All six changes ship in one commit; the build does not pass at intermediate steps because both modules must be consistent simultaneously.

---

## Task 1: Establish Baseline (build, format, test all green)

**Files:** None modified.

**Why:** This is a pure relocation refactor. The safety net is the existing test suite — in particular `TileIdContractTests`, which enforces `[TileId]` invariants on every concrete `ITile` in the application assembly. Before changing anything, prove the baseline is green so any later regression is attributable to this task.

- [ ] **Step 1: Verify backend builds cleanly from a fresh state**

Run: `dotnet build backend/Anela.Heblo.sln`

Expected: `Build succeeded.` with `0 Error(s)`. Warnings are acceptable as long as the build succeeds. If the build fails on `main`-equivalent code, **stop and investigate** — do not proceed with the relocation until the baseline is green.

- [ ] **Step 2: Verify formatter is clean**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`

Expected: Exit code 0 (no formatting changes needed). If it reports violations, **stop and resolve them on a separate commit first** — do not let pre-existing formatting noise contaminate the relocation diff.

- [ ] **Step 3: Verify the tile-contract tests pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TileIdContractTests"`

Expected: 4 tests pass (`AllConcreteTiles_HaveTileIdAttribute`, `AllConcreteTiles_HaveLowercaseTileId`, `AllConcreteTiles_HaveUniqueTileIds`, `AllConcreteTiles_WhoseNameEndsInTile_HaveBackwardCompatibleTileId`). If any of these fail before the change, **stop** — the suite is not a usable safety net.

- [ ] **Step 4: Confirm the source file count assumption**

Run: `ls backend/src/Anela.Heblo.Application/Features/Dashboard/Tiles/`

Expected: Exactly one file — `PurchaseOrdersInTransitTile.cs`. If anything else is present (e.g. a new tile added since the spec was written), **stop and re-scope** — deleting the folder in Task 6 would also delete a sibling tile.

- [ ] **Step 5: No commit**

Baseline verification produces no diff. Nothing to commit.

---

## Task 2: Create the relocated tile in the Purchase module

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/PurchaseOrdersInTransitTile.cs`

**Why:** Establish the new location first. After this step the codebase will have two `PurchaseOrdersInTransitTile` types (one in each namespace). The build will fail at this intermediate point because `DashboardModule.RegisterTile<PurchaseOrdersInTransitTile>()` becomes ambiguous — that is expected and resolved in Tasks 3–6. Do not run `dotnet build` between Tasks 2 and 6.

- [ ] **Step 1: Create the destination directory if it does not already exist**

Run: `mkdir -p backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles`

Expected: Silent success. The directory already exists today (it contains `LowStockEfficiencyTile.cs`), so this is a safety no-op.

- [ ] **Step 2: Write the new tile file**

Create `backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/PurchaseOrdersInTransitTile.cs` with the following contents. This is the exact body of the old file with the **namespace line** changed from `Anela.Heblo.Application.Features.Dashboard.Tiles` to `Anela.Heblo.Application.Features.Purchase.DashboardTiles`. Every other line — `using`s, `[TileId]` attribute, class declaration, properties, constructor, `LoadDataAsync`, `FormatAmountInThousands` — is byte-identical to the original.

```csharp
using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Xcc.Services.Dashboard;

namespace Anela.Heblo.Application.Features.Purchase.DashboardTiles;

[TileId("purchaseordersintransit")]
public class PurchaseOrdersInTransitTile : ITile
{
    private readonly IPurchaseOrderRepository _purchaseOrderRepository;

    // Self-describing metadata
    public string Title => "Suma nákupních objednávek";
    public string Description => "Celková částka nákupních objednávek ve stavu 'v přepravě'";
    public TileSize Size => TileSize.Small;
    public TileCategory Category => TileCategory.Purchase;
    public bool DefaultEnabled => true;
    public bool AutoShow => false; // Manual show
    public Type ComponentType => typeof(object); // Frontend component type not needed for backend
    public string[] RequiredPermissions => Array.Empty<string>();

    public PurchaseOrdersInTransitTile(IPurchaseOrderRepository purchaseOrderRepository)
    {
        _purchaseOrderRepository = purchaseOrderRepository;
    }

    public async Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
    {
        // Get all purchase orders in transit status
        var inTransitOrders = await _purchaseOrderRepository.GetByStatusAsync(PurchaseOrderStatus.InTransit, cancellationToken);

        // Calculate total amount
        var totalAmount = inTransitOrders.Sum(order => order.TotalAmount);

        // Format amount in thousands with 'k' suffix
        var formattedAmount = FormatAmountInThousands(totalAmount);

        var result = new
        {
            status = "success",
            data = new
            {
                count = inTransitOrders.Count(),
                totalAmount = totalAmount,
                formattedAmount = formattedAmount
            },
            metadata = new
            {
                lastUpdated = DateTime.UtcNow,
                source = "PurchaseOrderRepository"
            },
            drillDown = new
            {
                filters = new { state = "InTransit" },
                enabled = true,
                tooltip = "Zobrazit všechny objednávky v přepravě"
            }
        };

        return result;
    }

    private string FormatAmountInThousands(decimal amount)
    {
        if (amount == 0)
            return "0";

        var amountInThousands = amount / 1000m;

        // Round to 1 decimal place if needed, otherwise show as integer
        if (amountInThousands % 1 == 0)
            return $"{(int)amountInThousands}k";
        else
            return $"{amountInThousands:F1}k";
    }
}
```

- [ ] **Step 3: No build, no commit yet**

The codebase is intentionally broken at this moment — two types share the simple name `PurchaseOrdersInTransitTile`, and `DashboardModule.cs` still references the old one. Tasks 3–6 restore consistency; the commit happens in Task 8.

---

## Task 3: Register the relocated tile in `PurchaseModule`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/PurchaseModule.cs:32`

**Why:** The Purchase module must own the registration. `PurchaseModule.cs:2` already imports `Anela.Heblo.Application.Features.Purchase.DashboardTiles`, so no `using` change is needed — only an additional `RegisterTile<T>()` call.

- [ ] **Step 1: Add the registration call immediately after the `LowStockEfficiencyTile` line**

In `backend/src/Anela.Heblo.Application/Features/Purchase/PurchaseModule.cs`, find this block:

```csharp
        // Register dashboard tiles
        services.RegisterTile<LowStockEfficiencyTile>();

        return services;
```

Replace it with:

```csharp
        // Register dashboard tiles
        services.RegisterTile<LowStockEfficiencyTile>();
        services.RegisterTile<PurchaseOrdersInTransitTile>();

        return services;
```

Do not touch any other line. Do not reorder the existing registrations. Do not add a `using` directive — the file already has `using Anela.Heblo.Application.Features.Purchase.DashboardTiles;` on line 2.

- [ ] **Step 2: No build, no commit yet**

`DashboardModule.cs` still registers the old type. Task 4 fixes that.

---

## Task 4: Remove the tile from `DashboardModule`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs:2,21,22`

**Why:** Drop the obsolete `using`, the obsolete `RegisterTile<T>()` call, and the now-orphan section comment. After this change `DashboardModule` no longer references `Features.Dashboard.Tiles` (which will not exist after Task 6) and no longer transitively pulls in `Anela.Heblo.Domain.Features.Purchase` via this tile — fulfilling FR-5.

- [ ] **Step 1: Open the current file**

The full current contents of `backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs` are:

```csharp
using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Application.Features.Dashboard.Tiles;
using Anela.Heblo.Xcc.Services.Dashboard;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Dashboard;

public static class DashboardModule
{
    public static IServiceCollection AddDashboardModule(this IServiceCollection services)
    {
        // MediatR handlers are automatically registered by the ApplicationModule

        // Hangfire storage singleton — resolved lazily after Hangfire is configured
        services.AddSingleton(_ => JobStorage.Current);

        // Per-user async lock for serializing concurrent UserDashboardSettings mutations
        services.AddSingleton<IUserDashboardSettingsLock, UserDashboardSettingsLock>();

        // Register dashboard tiles
        services.RegisterTile<PurchaseOrdersInTransitTile>();

        return services;
    }
}
```

- [ ] **Step 2: Rewrite the file to the new contents**

Replace the entire file with:

```csharp
using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Xcc.Services.Dashboard;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Dashboard;

public static class DashboardModule
{
    public static IServiceCollection AddDashboardModule(this IServiceCollection services)
    {
        // MediatR handlers are automatically registered by the ApplicationModule

        // Hangfire storage singleton — resolved lazily after Hangfire is configured
        services.AddSingleton(_ => JobStorage.Current);

        // Per-user async lock for serializing concurrent UserDashboardSettings mutations
        services.AddSingleton<IUserDashboardSettingsLock, UserDashboardSettingsLock>();

        return services;
    }
}
```

Changes summary:
- Removed line 2 `using Anela.Heblo.Application.Features.Dashboard.Tiles;` — the namespace will not exist after Task 6, and nothing else in the file uses it.
- Removed lines 21–22 (the `// Register dashboard tiles` comment and `services.RegisterTile<PurchaseOrdersInTransitTile>();`). The comment was dead once the only Dashboard-owned tile left the module; removing the comment keeps the file honest about what it does.
- The `using Anela.Heblo.Xcc.Services.Dashboard;` remains because `IUserDashboardSettingsLock` lives in `Application.Features.Dashboard.Infrastructure` and `RegisterTile<T>()` is no longer called from this file, but the `IUserDashboardSettingsLock` registration uses types from `Infrastructure`, not `Xcc.Services.Dashboard`. **Verify**: if `dotnet format` later reports `using Anela.Heblo.Xcc.Services.Dashboard;` as unused, allow the formatter to remove it; otherwise leave it as-is (it may be used by extension methods on `IServiceCollection` from that namespace).

- [ ] **Step 3: No build, no commit yet**

The old tile file still exists at `Features/Dashboard/Tiles/PurchaseOrdersInTransitTile.cs` and the test still references it. Tasks 5 and 6 finish the job.

---

## Task 5: Update the contract-test assembly marker

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Dashboard/TileIdContractTests.cs:15`

**Why:** The test uses `typeof(Anela.Heblo.Application.Features.Dashboard.Tiles.PurchaseOrdersInTransitTile).Assembly` purely as a stable handle on the `Anela.Heblo.Application` assembly so reflection can enumerate every concrete `ITile`. After Task 6 that fully-qualified name no longer exists and the test file will not compile. Swap the marker to `LowStockEfficiencyTile` — same assembly, conventional tile, expected to remain stable.

- [ ] **Step 1: Locate the current marker line**

In `backend/test/Anela.Heblo.Tests/Features/Dashboard/TileIdContractTests.cs`, find:

```csharp
    // Scan only these two production assemblies (not the test assembly)
    private static readonly Assembly[] ProductionAssemblies =
    [
        typeof(BackgroundTaskStatusTile).Assembly,  // Anela.Heblo.Xcc
        typeof(Anela.Heblo.Application.Features.Dashboard.Tiles.PurchaseOrdersInTransitTile).Assembly  // Anela.Heblo.Application
    ];
```

- [ ] **Step 2: Replace the marker**

Replace the block with:

```csharp
    // Scan only these two production assemblies (not the test assembly)
    private static readonly Assembly[] ProductionAssemblies =
    [
        typeof(BackgroundTaskStatusTile).Assembly,  // Anela.Heblo.Xcc
        typeof(Anela.Heblo.Application.Features.Purchase.DashboardTiles.LowStockEfficiencyTile).Assembly  // Anela.Heblo.Application
    ];
```

Only the second `typeof` changes. Do not modify the `BackgroundTaskStatusTile` line, the trailing comment, any of the test methods, or the file's `using` directives — the new fully-qualified name does not require an additional `using` since it is spelled out in full.

- [ ] **Step 3: No build, no commit yet**

The old file still exists on disk. Task 6 removes it.

---

## Task 6: Delete the old file and the now-empty folder

**Files:**
- Delete: `backend/src/Anela.Heblo.Application/Features/Dashboard/Tiles/PurchaseOrdersInTransitTile.cs`
- Delete: `backend/src/Anela.Heblo.Application/Features/Dashboard/Tiles/` (directory)

**Why:** The new file at `Features/Purchase/DashboardTiles/PurchaseOrdersInTransitTile.cs` is now the only copy that should exist. Until the old file is removed there are two types with the same simple name and the build is ambiguous. Once removed, the parent `Tiles/` folder is empty and must be deleted so the source tree honestly reflects that no Dashboard-owned tiles remain.

- [ ] **Step 1: Remove the obsolete source file**

Run: `git rm backend/src/Anela.Heblo.Application/Features/Dashboard/Tiles/PurchaseOrdersInTransitTile.cs`

Expected: `rm 'backend/src/Anela.Heblo.Application/Features/Dashboard/Tiles/PurchaseOrdersInTransitTile.cs'`. Using `git rm` (rather than `rm`) records the deletion in the index so Task 8's commit captures it cleanly.

- [ ] **Step 2: Remove the now-empty directory**

Run: `rmdir backend/src/Anela.Heblo.Application/Features/Dashboard/Tiles`

Expected: Silent success. `rmdir` fails loudly if the directory contains any file — that would mean Task 1 Step 4's invariant was violated (a sibling tile snuck in). In that case, **stop** and re-scope manually: the spec forbids touching unrelated tiles.

Git does not track empty directories, so this `rmdir` is a working-tree-only cleanup; nothing additional needs staging.

- [ ] **Step 3: No commit yet**

Validation runs in Task 7. Commit in Task 8.

---

## Task 7: Validate (build, format, test)

**Files:** None modified.

**Why:** All six file changes are now in place. The build, formatter, and full test suite must come back green together before committing — that is the contract that proves FR-1 through FR-7 are satisfied.

- [ ] **Step 1: Verify the solution still builds**

Run: `dotnet build backend/Anela.Heblo.sln`

Expected: `Build succeeded.` with `0 Error(s)`. If the build fails:
- An `ambiguous reference` or `'PurchaseOrdersInTransitTile' does not exist in the namespace` error means a `using Anela.Heblo.Application.Features.Dashboard.Tiles;` reference was missed somewhere in the solution. Run `grep -rn "Features.Dashboard.Tiles" backend/` and update every offender to the new namespace or remove it.
- A `'TileId' is duplicated` test failure from `AllConcreteTiles_HaveUniqueTileIds` means two copies of the file still coexist — confirm Task 6 Step 1 actually deleted the old file.

- [ ] **Step 2: Verify formatting**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`

Expected: Exit code 0. If the formatter reports violations, run `dotnet format backend/Anela.Heblo.sln` to fix them (typically removal of an unused `using`), then re-run `--verify-no-changes` to confirm clean. Any auto-fixed changes ship in the same commit (Task 8) — they are part of the relocation, not a separate refactor.

- [ ] **Step 3: Run the tile-contract tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TileIdContractTests"`

Expected: All 4 tests pass. In particular:
- `AllConcreteTiles_HaveTileIdAttribute` proves the relocated tile still carries `[TileId("purchaseordersintransit")]`.
- `AllConcreteTiles_HaveUniqueTileIds` proves there is only one tile registered under `"purchaseordersintransit"` (i.e. the old file is genuinely gone).
- `AllConcreteTiles_WhoseNameEndsInTile_HaveBackwardCompatibleTileId` proves the persisted-id contract (NFR-4) is still honoured — the tile id `"purchaseordersintransit"` still equals `"PurchaseOrdersInTransitTile".ToLower().Replace("tile","")`.

- [ ] **Step 4: Run the full backend test suite**

Run: `dotnet test backend/Anela.Heblo.sln`

Expected: All tests pass. This catches any test elsewhere in the suite that happened to reference the old fully-qualified type (none was found during planning, but the full suite is the canonical check).

- [ ] **Step 5: Confirm Dashboard no longer touches Purchase domain (FR-5 acceptance)**

Run: `grep -rn "Anela.Heblo.Domain.Features.Purchase" backend/src/Anela.Heblo.Application/Features/Dashboard/`

Expected: No matches. If any line is returned, the move did not fully sever the cross-module edge — investigate the matching file and either relocate it or open a follow-up issue (the spec marks other Dashboard-tile coupling as out of scope; only the `PurchaseOrdersInTransitTile`-introduced edge is in scope here).

- [ ] **Step 6: No commit yet**

Commit happens once in Task 8 so the diff is a single atomic relocation.

---

## Task 8: Commit

**Files:** All six paths from the File Structure table.

**Why:** A single commit keeps the relocation atomic and bisectable. The branch already exists (`feat-arch-review-dashboard-purchaseordersintr`) and is pushed; this commit lands on top of it.

- [ ] **Step 1: Inspect the staged and unstaged diff**

Run: `git status`

Expected output (paths may be in any order):

```
On branch feat-arch-review-dashboard-purchaseordersintr
Changes to be committed:
        deleted:    backend/src/Anela.Heblo.Application/Features/Dashboard/Tiles/PurchaseOrdersInTransitTile.cs

Changes not staged for commit:
        modified:   backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs
        modified:   backend/src/Anela.Heblo.Application/Features/Purchase/PurchaseModule.cs
        modified:   backend/test/Anela.Heblo.Tests/Features/Dashboard/TileIdContractTests.cs

Untracked files:
        backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/PurchaseOrdersInTransitTile.cs
```

If anything else appears (e.g. unrelated formatter changes from Task 7 Step 2 on files this plan did not touch), **stop and investigate** before committing — surgical-changes rule applies.

- [ ] **Step 2: Stage the four remaining paths explicitly**

Run:
```
git add \
  backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/PurchaseOrdersInTransitTile.cs \
  backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs \
  backend/src/Anela.Heblo.Application/Features/Purchase/PurchaseModule.cs \
  backend/test/Anela.Heblo.Tests/Features/Dashboard/TileIdContractTests.cs
```

Add specific paths rather than `git add -A` so unrelated stray files cannot ride along.

- [ ] **Step 3: Verify the final staged set**

Run: `git status`

Expected: All six changes (one deletion, one addition, three modifications, plus the implicit empty-folder deletion which git does not display) under "Changes to be committed". Nothing under "Changes not staged" or "Untracked files".

- [ ] **Step 4: Commit with a conventional-commits message**

Run:
```
git commit -m "$(cat <<'EOF'
refactor(purchase): relocate PurchaseOrdersInTransitTile from Dashboard to Purchase module

Move the tile to Features/Purchase/DashboardTiles/ so it lives with the
domain it consumes, and register it from PurchaseModule. Removes the
Dashboard -> Domain.Features.Purchase compile-time edge introduced by this
tile, restoring the module-ownership convention demonstrated by
LowStockEfficiencyTile.

- Tile id, payload, constructor, and LoadDataAsync body unchanged
  (NFR-4 backward compatibility preserved).
- TileIdContractTests assembly marker repointed to LowStockEfficiencyTile
  (the relocated type's old fully-qualified name no longer exists).
- Empty Features/Dashboard/Tiles/ folder removed.
EOF
)"
```

Expected: Commit succeeds and reports the six changed paths. If a pre-commit hook fails (e.g. dotnet format runs again and finds something), **do not amend** — fix the underlying issue, re-stage, and create a new commit.

- [ ] **Step 5: Verify the commit**

Run: `git log -1 --stat`

Expected: The commit summary lists the four modified/created/deleted code files plus the deletion of the old tile. The diff stat shows the new file as additions and the old file as deletions.

---

## Acceptance Criteria Coverage

Mapping each spec requirement (including arch-review amendments) to its implementing task:

| Requirement | Source | Task |
|---|---|---|
| FR-1: File at new path with new namespace, body unchanged, tile id unchanged | spec | Task 2 |
| FR-1: Old file removed | spec | Task 6 Step 1 |
| FR-2: `PurchaseModule.cs` registers the tile | spec | Task 3 |
| FR-2: App starts and DI resolves the tile | spec | Task 7 Step 1 (build) + Step 4 (tests exercise composition) |
| FR-3: `DashboardModule.cs` no longer contains the registration | spec | Task 4 Step 2 |
| FR-3: Unused `using` removed from `DashboardModule.cs` | spec | Task 4 Step 2 |
| FR-4: Runtime behavior preserved (same id, payload, repository call) | spec | Task 2 (body verbatim) + Task 7 Step 3 (contract tests) |
| FR-5: No `Anela.Heblo.Domain.Features.Purchase` reference left in `Features/Dashboard/` | spec | Task 7 Step 5 |
| FR-5: `dotnet build` succeeds | spec | Task 7 Step 1 |
| FR-5: `dotnet format` reports no violations | spec | Task 7 Step 2 |
| FR-6 (arch-review amendment): `TileIdContractTests` marker updated to compile | arch-review | Task 5 + Task 7 Step 3 |
| FR-7 (arch-review amendment): empty `Features/Dashboard/Tiles/` folder removed | arch-review | Task 6 Step 2 |
| NFR-1 Performance: unchanged execution path | spec | Task 2 body is byte-identical |
| NFR-2 Security: unchanged | spec | No auth/data-access change touched |
| NFR-3 Maintainability: tile co-located with its domain | spec | Tasks 2–4 collectively |
| NFR-4 Backward compatibility: `[TileId("purchaseordersintransit")]` preserved | spec | Task 2 (attribute carried verbatim) + Task 7 Step 3 (`AllConcreteTiles_WhoseNameEndsInTile_HaveBackwardCompatibleTileId`) |

No spec requirement is unaddressed.
