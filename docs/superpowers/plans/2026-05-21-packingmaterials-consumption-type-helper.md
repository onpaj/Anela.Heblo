# Extract Duplicated `GetConsumptionTypeText` Helper in PackingMaterials Module — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace five copy-pasted `private static string GetConsumptionTypeText(ConsumptionType)` methods in `Application/Features/PackingMaterials/UseCases/*` with a single shared `internal static` helper, so future enum additions only need to change one place.

**Architecture:** Add `PackingMaterialsTextHelper` in `Application/Features/PackingMaterials/Contracts/`, co-located with `PackingMaterialDto` (whose `ConsumptionTypeText` property it populates). All five handlers already import the helper's namespace via the existing `Contracts` `using`, so the only changes per handler are: (a) replace the call-site identifier and (b) delete the private method. No DI, no public API, no behavior change.

**Tech Stack:** .NET 8, C#, MediatR (already in place — handler shape unchanged).

---

## File Structure

| Action | Path | Responsibility |
|--------|------|----------------|
| Create | `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/PackingMaterialsTextHelper.cs` | New `internal static` helper; single switch expression mapping `ConsumptionType` → Czech UI string. |
| Modify | `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/CreatePackingMaterial/CreatePackingMaterialHandler.cs` | Replace call site (line 36); delete private method (lines 50–56). |
| Modify | `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterial/UpdatePackingMaterialHandler.cs` | Replace call site (line 37); delete private method (lines 50–56). |
| Modify | `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityHandler.cs` | Replace call site (line 50); delete private method (lines 63–69). |
| Modify | `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialsList/GetPackingMaterialsListHandler.cs` | Replace call site (line 39); delete private method (lines 53–59). |
| Modify | `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialLogs/GetPackingMaterialLogsHandler.cs` | Replace call site (line 36); delete `GetConsumptionTypeText` (lines 63–69). **Keep** `GetLogTypeText` and the `Anela.Heblo.Domain.Features.PackingMaterials.Enums` using — both still needed for `LogEntryType`. |

No other files are touched. No `using` directives are added or removed (the namespace `Anela.Heblo.Application.Features.PackingMaterials.Contracts` is already imported by every handler; `Anela.Heblo.Domain.Features.PackingMaterials.Enums` stays because it currently brings `ConsumptionType` and, in one handler, `LogEntryType` into source-level scope and any cleanup belongs to a separate change).

**Why surgical:** Spec FR-2 requires "No other code in these handlers is touched." After removing the private method, the `Enums` `using` in four handlers becomes source-level unused, but the project has no `.editorconfig` / analyzer rule enforcing IDE0005, so `dotnet format` will not strip it. NFR-4 (`dotnet format` produces no diff) is satisfied by leaving them in place.

---

## Pre-flight

- [ ] **Step 1: Confirm the worktree is clean and on the feature branch**

Run:
```bash
git status
git branch --show-current
```

Expected: clean working tree on branch `feat-arch-review-packingmaterials-getconsumpt`.

- [ ] **Step 2: Confirm baseline build and test status before any change**

Run:
```bash
dotnet build Anela.Heblo.sln
```

Expected: `Build succeeded.` with 0 errors.

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PackingMaterials" --no-build
```

Expected: All tests pass (current PackingMaterials test files are `GetDailyConsumptionBreakdownHandlerTests`, `ConsumptionCalculationServiceTests`, `AllocationHandlerTests`; none exercise the text mapping, but they must pass to establish baseline).

- [ ] **Step 3: Capture pre-refactor occurrence count of `GetConsumptionTypeText` for verification at the end**

Run:
```bash
grep -rn "GetConsumptionTypeText" backend/src | wc -l
```

Expected: `10` (one method definition + one call site, in each of 5 handler files).

---

### Task 1: Add `PackingMaterialsTextHelper`

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/PackingMaterialsTextHelper.cs`

- [ ] **Step 1: Create the helper file**

Write exactly:

```csharp
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;

namespace Anela.Heblo.Application.Features.PackingMaterials.Contracts;

internal static class PackingMaterialsTextHelper
{
    public static string ConsumptionTypeText(ConsumptionType type) => type switch
    {
        ConsumptionType.PerOrder => "za zakázku",
        ConsumptionType.PerProduct => "za produkt",
        ConsumptionType.PerDay => "za den",
        _ => type.ToString()
    };
}
```

**Encoding note:** Save as UTF-8 (without BOM) to match the existing sibling `PackingMaterialDto.cs` in the same folder. The Czech characters `á`, `ž` must round-trip byte-identical to the existing handler strings.

- [ ] **Step 2: Verify the new file compiles (the helper is unreferenced at this point but the project must still build)**

Run:
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: `Build succeeded.` 0 errors, 0 new warnings.

- [ ] **Step 3: Confirm the Czech strings round-tripped to disk correctly**

Run:
```bash
grep -c "za zakázku" backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/PackingMaterialsTextHelper.cs
grep -c "za produkt" backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/PackingMaterialsTextHelper.cs
grep -c "za den" backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/PackingMaterialsTextHelper.cs
```

Expected: `1`, `1`, `1` (one match each). If any returns `0`, the file was saved with a wrong encoding — re-save as UTF-8 without BOM and re-check.

---

### Task 2: Rewire `CreatePackingMaterialHandler`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/CreatePackingMaterial/CreatePackingMaterialHandler.cs`

- [ ] **Step 1: Replace the call site on line 36**

Find this line in `CreatePackingMaterialHandler.cs`:

```csharp
            ConsumptionTypeText = GetConsumptionTypeText(createdMaterial.ConsumptionType),
```

Replace with:

```csharp
            ConsumptionTypeText = PackingMaterialsTextHelper.ConsumptionTypeText(createdMaterial.ConsumptionType),
```

- [ ] **Step 2: Delete the private method (lines 50–56)**

Remove this entire block (including the blank line that precedes it on line 49) from `CreatePackingMaterialHandler.cs`:

```csharp

    private static string GetConsumptionTypeText(ConsumptionType type) => type switch
    {
        ConsumptionType.PerOrder => "za zakázku",
        ConsumptionType.PerProduct => "za produkt",
        ConsumptionType.PerDay => "za den",
        _ => type.ToString()
    };
```

After removal, the closing `}` of the class must be the last non-empty line of the file. The `using Anela.Heblo.Domain.Features.PackingMaterials.Enums;` directive **stays** — do not delete it (surgical-change rule; `dotnet format` will not strip it).

- [ ] **Step 3: Verify the file compiles**

Run:
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: `Build succeeded.` 0 errors, 0 new warnings.

---

### Task 3: Rewire `UpdatePackingMaterialHandler`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterial/UpdatePackingMaterialHandler.cs`

- [ ] **Step 1: Replace the call site on line 37**

Find this line in `UpdatePackingMaterialHandler.cs`:

```csharp
            ConsumptionTypeText = GetConsumptionTypeText(material.ConsumptionType),
```

Replace with:

```csharp
            ConsumptionTypeText = PackingMaterialsTextHelper.ConsumptionTypeText(material.ConsumptionType),
```

- [ ] **Step 2: Delete the private method (lines 50–56)**

Remove this entire block (including the preceding blank line) from `UpdatePackingMaterialHandler.cs`:

```csharp

    private static string GetConsumptionTypeText(ConsumptionType type) => type switch
    {
        ConsumptionType.PerOrder => "za zakázku",
        ConsumptionType.PerProduct => "za produkt",
        ConsumptionType.PerDay => "za den",
        _ => type.ToString()
    };
```

Keep the `using Anela.Heblo.Domain.Features.PackingMaterials.Enums;` directive in place.

- [ ] **Step 3: Verify the file compiles**

Run:
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: `Build succeeded.` 0 errors, 0 new warnings.

---

### Task 4: Rewire `UpdatePackingMaterialQuantityHandler`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityHandler.cs`

- [ ] **Step 1: Replace the call site on line 50**

Find this line in `UpdatePackingMaterialQuantityHandler.cs`:

```csharp
            ConsumptionTypeText = GetConsumptionTypeText(material.ConsumptionType),
```

Replace with:

```csharp
            ConsumptionTypeText = PackingMaterialsTextHelper.ConsumptionTypeText(material.ConsumptionType),
```

- [ ] **Step 2: Delete the private method (lines 63–69)**

Remove this entire block (including the preceding blank line) from `UpdatePackingMaterialQuantityHandler.cs`:

```csharp

    private static string GetConsumptionTypeText(ConsumptionType type) => type switch
    {
        ConsumptionType.PerOrder => "za zakázku",
        ConsumptionType.PerProduct => "za produkt",
        ConsumptionType.PerDay => "za den",
        _ => type.ToString()
    };
```

Keep the `using Anela.Heblo.Domain.Features.PackingMaterials.Enums;` directive (`LogEntryType` is referenced on line 33).

- [ ] **Step 3: Verify the file compiles**

Run:
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: `Build succeeded.` 0 errors, 0 new warnings.

---

### Task 5: Rewire `GetPackingMaterialsListHandler`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialsList/GetPackingMaterialsListHandler.cs`

- [ ] **Step 1: Replace the call site on line 39**

Find this line in `GetPackingMaterialsListHandler.cs`:

```csharp
                ConsumptionTypeText = GetConsumptionTypeText(material.ConsumptionType),
```

Replace with:

```csharp
                ConsumptionTypeText = PackingMaterialsTextHelper.ConsumptionTypeText(material.ConsumptionType),
```

- [ ] **Step 2: Delete the private method (lines 53–59)**

Remove this entire block (including the preceding blank line) from `GetPackingMaterialsListHandler.cs`:

```csharp

    private static string GetConsumptionTypeText(ConsumptionType type) => type switch
    {
        ConsumptionType.PerOrder => "za zakázku",
        ConsumptionType.PerProduct => "za produkt",
        ConsumptionType.PerDay => "za den",
        _ => type.ToString()
    };
```

Keep the `using Anela.Heblo.Domain.Features.PackingMaterials.Enums;` directive.

- [ ] **Step 3: Verify the file compiles**

Run:
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: `Build succeeded.` 0 errors, 0 new warnings.

---

### Task 6: Rewire `GetPackingMaterialLogsHandler`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialLogs/GetPackingMaterialLogsHandler.cs`

This handler has **two** helpers (`GetConsumptionTypeText` and `GetLogTypeText`). Delete only the first; keep `GetLogTypeText` intact.

- [ ] **Step 1: Replace the call site on line 36**

Find this line in `GetPackingMaterialLogsHandler.cs`:

```csharp
            ConsumptionTypeText = GetConsumptionTypeText(material.ConsumptionType),
```

Replace with:

```csharp
            ConsumptionTypeText = PackingMaterialsTextHelper.ConsumptionTypeText(material.ConsumptionType),
```

- [ ] **Step 2: Delete only the `GetConsumptionTypeText` private method (lines 63–69)**

Remove this entire block (including the preceding blank line) from `GetPackingMaterialLogsHandler.cs`:

```csharp

    private static string GetConsumptionTypeText(ConsumptionType type) => type switch
    {
        ConsumptionType.PerOrder => "za zakázku",
        ConsumptionType.PerProduct => "za produkt",
        ConsumptionType.PerDay => "za den",
        _ => type.ToString()
    };
```

**Do not touch** the `GetLogTypeText` method below it — it is unrelated. After this edit, the file should end with `GetLogTypeText` followed by the closing brace.

The `using Anela.Heblo.Domain.Features.PackingMaterials.Enums;` directive stays — `LogEntryType` is referenced on lines 50–51 and in the `GetLogTypeText` signature.

- [ ] **Step 3: Verify the file compiles**

Run:
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: `Build succeeded.` 0 errors, 0 new warnings.

---

### Task 7: Verify zero residuals and run full validation

- [ ] **Step 1: Confirm no `GetConsumptionTypeText` references remain anywhere in the repo**

Run:
```bash
grep -rn "GetConsumptionTypeText" backend/src backend/test
```

Expected: no output (zero matches). If any match is printed, return to the corresponding task and fix.

- [ ] **Step 2: Confirm exactly 5 call sites now invoke `PackingMaterialsTextHelper.ConsumptionTypeText`**

Run:
```bash
grep -rn "PackingMaterialsTextHelper.ConsumptionTypeText" backend/src
```

Expected: 5 lines printed, one per handler file in `UseCases/`. (The helper definition itself does not call this fully-qualified path; it only declares the method.)

- [ ] **Step 3: Full solution build**

Run:
```bash
dotnet build Anela.Heblo.sln
```

Expected: `Build succeeded.` 0 errors. Warning count must equal the pre-refactor baseline (no new warnings).

- [ ] **Step 4: Verify `dotnet format` produces no additional changes (NFR-4)**

Run:
```bash
dotnet format Anela.Heblo.sln --verify-no-changes
```

Expected: exit code 0 with no diff. If the tool exits non-zero, inspect the proposed changes with `git diff` after running `dotnet format Anela.Heblo.sln` without `--verify-no-changes`. Accept the formatting fixes and re-run verification; do not commit unverified formatting changes.

- [ ] **Step 5: Run all backend tests**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build
```

Expected: all tests pass, with the same totals as the pre-refactor baseline.

- [ ] **Step 6: Spot-check the diff before committing**

Run:
```bash
git status
git diff --stat
```

Expected `git status`:
- `new file: backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/PackingMaterialsTextHelper.cs`
- `modified: 5 handler files` listed under `UseCases/…`

Expected `git diff --stat`: 6 files changed; the new helper file adds ~12 lines, each handler file loses ~7 lines net (call-site rename + 7-line private method removal).

If the diff shows any other modified files, revert those edits — only the listed paths must change.

- [ ] **Step 7: Commit**

Run:
```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/PackingMaterialsTextHelper.cs \
        backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/CreatePackingMaterial/CreatePackingMaterialHandler.cs \
        backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterial/UpdatePackingMaterialHandler.cs \
        backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityHandler.cs \
        backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialsList/GetPackingMaterialsListHandler.cs \
        backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialLogs/GetPackingMaterialLogsHandler.cs

git commit -m "refactor(packing-materials): extract shared ConsumptionType text helper

Consolidate five copy-pasted GetConsumptionTypeText methods in
PackingMaterials handlers into a single internal static
PackingMaterialsTextHelper.ConsumptionTypeText in Contracts/.
No behavior change; API output is byte-identical."
```

Expected: commit succeeds; the next `git log -1 --stat` shows exactly the six files listed above.

---

## Acceptance Cross-Check

| Spec requirement | Verified by |
|---|---|
| FR-1: helper exists, `internal static class`, single `public static` method, exact mappings, no other public members | Task 1 Step 1 + Step 3 (Czech-string round-trip) |
| FR-2: private method deleted from all five handlers; all call sites rewired; repo-wide grep returns zero | Tasks 2–6 Step 2; Task 7 Step 1 |
| FR-2: only the five listed handler files are modified | Task 7 Step 6 (`git diff --stat`) |
| FR-3: existing tests still pass; no enum changes; only the invoked method name changes | Task 7 Step 5 + Step 6 |
| NFR-1 / NFR-2: no perf or security impact | No change to call shape; static method on switch expression — verified inherently by FR-3 |
| NFR-3: single source of truth for labels | Task 7 Step 2 (single helper, 5 callers) |
| NFR-4: `dotnet build` succeeds with no new warnings; `dotnet format` produces no diff; tests pass | Task 7 Steps 3, 4, 5 |

## Out-of-Scope Reminders

Per spec, do **not** in this PR:
- Add unit tests for `PackingMaterialsTextHelper` (existing handler tests provide indirect coverage and the spec excludes this).
- Replace `_ => type.ToString()` with an exception.
- Introduce a localization framework.
- Touch other duplicated helpers elsewhere in the codebase.
- Remove unused `using` directives that become dead due to the refactor (the project has no analyzer rule enforcing this; leaving them satisfies the surgical-change requirement and NFR-4).
- Modify any frontend code — the API contract is unchanged.
