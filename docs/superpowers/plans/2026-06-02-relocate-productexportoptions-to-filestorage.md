# Relocate ProductExportOptions to FileStorage Module — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the `ProductExportOptions` configuration class from the Configuration module's Domain layer (`Anela.Heblo.Domain/Features/Configuration/`) into the FileStorage module's Application layer (`Anela.Heblo.Application/Features/FileStorage/`), where its only consumers live. Pure refactor — no behaviour change.

**Architecture:** Single-file relocation. The class body, defaults, property names, XML docs, and the `"ProductExportOptions"` configuration section key are preserved verbatim. The new namespace `Anela.Heblo.Application.Features.FileStorage` matches the convention used by every other single-file options class in the Application project (`LeafletOptions`, `ArticleOptions`, `OrgChartOptions`, `MeetingTasksOptions`, etc.). Three production consumers (all already under `Anela.Heblo.Application.Features.FileStorage.*`) and five test files have their stale `using Anela.Heblo.Domain.Features.Configuration;` directive removed; the test files additionally pick up `using Anela.Heblo.Application.Features.FileStorage;`. The DI registration in `Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:363` keeps its existing `Configure<ProductExportOptions>(configuration.GetSection("ProductExportOptions"))` call and its file-level `using Anela.Heblo.Domain.Features.Configuration;` (still required by `ConfigurationConstants` references on multiple lines in the same file).

**Tech Stack:** .NET 8, C#, xUnit + FluentAssertions for tests, ASP.NET Core Options pattern.

---

## File Inventory

### Files created
- `backend/src/Anela.Heblo.Application/Features/FileStorage/ProductExportOptions.cs` — new home of the class.

### Files deleted
- `backend/src/Anela.Heblo.Domain/Features/Configuration/ProductExportOptions.cs` — old location.

### Files modified — production code
1. `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/DownloadResilienceService.cs` — drop one `using`.
2. `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJob.cs` — drop one `using`.
3. `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs` — drop one `using`.

### Files modified — test code
4. `backend/test/Anela.Heblo.Tests/Features/FileStorage/Configuration/ProductExportOptionsTests.cs` — swap `using`.
5. `backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageModuleTests.cs` — swap `using`.
6. `backend/test/Anela.Heblo.Tests/Features/FileStorage/Infrastructure/DownloadResilienceServiceTests.cs` — swap `using`.
7. `backend/test/Anela.Heblo.Tests/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJobTests.cs` — swap `using`.
8. `backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs` — swap `using`.

### Files NOT modified (verified)
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — file-level `using Anela.Heblo.Domain.Features.Configuration;` on line 10 must stay (used by `ConfigurationConstants` on lines 37–39, 67, 76, 111, 116–117). Line 363 binds the section; identifier `ProductExportOptions` resolves cleanly from the new Application namespace because `using Anela.Heblo.Application.Features.FileStorage;` is **already** present in the file from the existing `FileStorageModule` registration block (verify in Task 8 — if not present, add it).
- `backend/src/Anela.Heblo.API/appsettings.json` — section key `"ProductExportOptions"` unchanged.
- `backend/src/Anela.Heblo.Domain/Features/Configuration/ApplicationConfiguration.cs` — unchanged.
- `backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs` — unchanged.

---

## Task 1: Create the new ProductExportOptions file under FileStorage

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/FileStorage/ProductExportOptions.cs`

- [ ] **Step 1: Create the new file with the class body byte-identical to the original (only namespace differs)**

Write file `backend/src/Anela.Heblo.Application/Features/FileStorage/ProductExportOptions.cs` with exactly this content (no trailing whitespace, single trailing newline):

```csharp
namespace Anela.Heblo.Application.Features.FileStorage;

/// <summary>
/// Configuration options for product export functionality
/// </summary>
public class ProductExportOptions
{
    /// <summary>
    /// The URL from which product export files will be downloaded
    /// </summary>
    public string Url { get; set; } = null!;

    public string ContainerName { get; set; }

    /// <summary>
    /// Timeout for the HTTP HEAD probe used to verify export availability.
    /// </summary>
    public TimeSpan HeadTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Timeout for the full export file download.
    /// </summary>
    public TimeSpan DownloadTimeout { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Maximum number of retry attempts for transient HTTP failures.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Base delay for the exponential back-off retry policy.
    /// </summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);
}
```

Note: The class members, modifiers, defaults, and XML documentation comments are bit-for-bit identical to the source at `backend/src/Anela.Heblo.Domain/Features/Configuration/ProductExportOptions.cs`. Only the namespace declaration on line 1 changes from `Anela.Heblo.Domain.Features.Configuration` to `Anela.Heblo.Application.Features.FileStorage`.

- [ ] **Step 2: Verify the build succeeds with both old and new file in place (will compile because nothing references the new namespace yet, and old consumers still resolve the old class)**

Run from repo root:
```bash
cd backend && dotnet build src/Anela.Heblo.sln --nologo
```
Expected: `Build succeeded` with 0 errors. There MAY be ambiguous-reference errors at this stage because both files now define `ProductExportOptions` and several consumers still have `using Anela.Heblo.Domain.Features.Configuration;`. If that happens, that is expected and will be cleaned up in the next tasks. **If errors appear, do not panic — proceed to Task 2.** If the build succeeds, that's also fine.

- [ ] **Step 3: Do NOT commit yet** — the working tree still has a duplicate class. Continue to Task 2.

---

## Task 2: Delete the old ProductExportOptions file from Domain

**Files:**
- Delete: `backend/src/Anela.Heblo.Domain/Features/Configuration/ProductExportOptions.cs`

- [ ] **Step 1: Delete the old file**

```bash
git rm backend/src/Anela.Heblo.Domain/Features/Configuration/ProductExportOptions.cs
```

Expected: file removed, staged for deletion.

- [ ] **Step 2: Verify the build now fails for all consumers**

Run:
```bash
cd backend && dotnet build src/Anela.Heblo.sln --nologo
```
Expected: build FAILS with `CS0246` (type or namespace `ProductExportOptions` could not be found) errors in:
- `Anela.Heblo.Application/Features/FileStorage/Infrastructure/DownloadResilienceService.cs`
- `Anela.Heblo.Application/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJob.cs`
- `Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs`
- `Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`
- All five test files listed in the file inventory.

This is the expected RED state. Each consumer's stale `using Anela.Heblo.Domain.Features.Configuration;` no longer resolves `ProductExportOptions`. Proceed to fix them in Tasks 3–8.

---

## Task 3: Fix DownloadResilienceService.cs

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/DownloadResilienceService.cs`

Context: This file's namespace is `Anela.Heblo.Application.Features.FileStorage.Infrastructure`, so `ProductExportOptions` in the new namespace `Anela.Heblo.Application.Features.FileStorage` resolves automatically (parent namespace is in scope). The stale `using Anela.Heblo.Domain.Features.Configuration;` on line 1 is the **only** reference to that namespace in this file (verified via grep — count = 1), so it can be removed entirely.

- [ ] **Step 1: Remove the stale using directive**

In `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/DownloadResilienceService.cs`, delete the entire line:
```csharp
using Anela.Heblo.Domain.Features.Configuration;
```
Do not add any replacement `using` — the type resolves via parent-namespace scope.

- [ ] **Step 2: Verify this file now compiles (the other files still fail; we'll fix them next)**

Run:
```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --nologo 2>&1 | grep -i "DownloadResilienceService.cs"
```
Expected: zero output (file no longer flagged). The whole solution build still fails at other files — that's fine. Continue.

---

## Task 4: Fix ProductExportDownloadJob.cs

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJob.cs`

Context: Namespace is `Anela.Heblo.Application.Features.FileStorage.Infrastructure.Jobs`. Same parent-scope resolution as Task 3. Stale `using Anela.Heblo.Domain.Features.Configuration;` on line 8 is the only reference (count = 1). Remove it entirely.

- [ ] **Step 1: Remove the stale using directive**

In `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJob.cs`, delete the line:
```csharp
using Anela.Heblo.Domain.Features.Configuration;
```

- [ ] **Step 2: Verify**

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --nologo 2>&1 | grep -i "ProductExportDownloadJob.cs"
```
Expected: zero output for this file.

---

## Task 5: Fix DownloadFromUrlHandler.cs

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs`

Context: Namespace is `Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl`. Parent-scope resolution. Stale `using Anela.Heblo.Domain.Features.Configuration;` on line 9 is the only reference (count = 1). Remove it.

- [ ] **Step 1: Remove the stale using directive**

In `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs`, delete the line:
```csharp
using Anela.Heblo.Domain.Features.Configuration;
```

- [ ] **Step 2: Verify the Application project now builds clean**

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --nologo
```
Expected: `Build succeeded` with 0 errors. The API project (`ServiceCollectionExtensions.cs`) and the Tests project still fail — fix them next.

---

## Task 6: Fix ProductExportOptionsTests.cs (the test for the class itself)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/FileStorage/Configuration/ProductExportOptionsTests.cs`

Context: Test namespace is `Anela.Heblo.Tests.Features.FileStorage.Configuration` — NOT a child of `Anela.Heblo.Application.Features.FileStorage`, so the type must be brought in via an explicit `using`. The stale `using Anela.Heblo.Domain.Features.Configuration;` on line 1 is the only reference to that namespace (count = 1). Replace it with the new one.

Note: The test folder is named `Configuration/` because the original class lived in the Configuration module. The folder name is now misleading but renaming it is out of scope per spec NFR-4 (minimal surgical change) and was flagged as a follow-up in the arch review. Leave the folder name as-is.

- [ ] **Step 1: Swap the using directive**

In `backend/test/Anela.Heblo.Tests/Features/FileStorage/Configuration/ProductExportOptionsTests.cs`, change line 1 from:
```csharp
using Anela.Heblo.Domain.Features.Configuration;
```
to:
```csharp
using Anela.Heblo.Application.Features.FileStorage;
```

- [ ] **Step 2: Verify this test file compiles individually**

```bash
cd backend && dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --nologo 2>&1 | grep -i "ProductExportOptionsTests.cs"
```
Expected: zero output for this file.

---

## Task 7: Fix the remaining four test files (FileStorageModuleTests, DownloadResilienceServiceTests, ProductExportDownloadJobTests, DownloadFromUrlHandlerTests)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageModuleTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/FileStorage/Infrastructure/DownloadResilienceServiceTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJobTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs`

Context: For each file, the test namespace lives under `Anela.Heblo.Tests.Features.FileStorage.*`, so an explicit `using` is required. Each file currently has exactly one reference to `Anela.Heblo.Domain.Features.Configuration` (verified via grep, count = 1 per file). For three of them (`FileStorageModuleTests`, `DownloadResilienceServiceTests`, `DownloadFromUrlHandlerTests`) the file already has `using Anela.Heblo.Application.Features.FileStorage;` or another sibling `using` — only the stale Domain one needs to go in those, **but to keep this safe and explicit, do the same swap everywhere**: change `Anela.Heblo.Domain.Features.Configuration` → `Anela.Heblo.Application.Features.FileStorage`. The C# compiler will deduplicate any duplicate `using` directives (no warning is raised for the duplicate in this analyzer config), but if `dotnet format` flags it as IDE0005, deduplicate by deleting the now-redundant line.

- [ ] **Step 1: Swap the using directive in FileStorageModuleTests.cs**

In `backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageModuleTests.cs`, change line 3 from:
```csharp
using Anela.Heblo.Domain.Features.Configuration;
```
to:
```csharp
using Anela.Heblo.Application.Features.FileStorage;
```
Note: line 1 of this file is already `using Anela.Heblo.Application.Features.FileStorage;`. After the swap, both directives will be identical. **Delete the now-duplicate line on (former) line 3** so the file has only one `using Anela.Heblo.Application.Features.FileStorage;` directive (the one already on line 1).

- [ ] **Step 2: Swap the using directive in DownloadResilienceServiceTests.cs**

In `backend/test/Anela.Heblo.Tests/Features/FileStorage/Infrastructure/DownloadResilienceServiceTests.cs`, change line 2 from:
```csharp
using Anela.Heblo.Domain.Features.Configuration;
```
to:
```csharp
using Anela.Heblo.Application.Features.FileStorage;
```
Note: line 1 of this file is `using Anela.Heblo.Application.Features.FileStorage.Infrastructure;` — a DIFFERENT namespace, so no duplicate to remove.

- [ ] **Step 3: Swap the using directive in ProductExportDownloadJobTests.cs**

In `backend/test/Anela.Heblo.Tests/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJobTests.cs`, change line 11 from:
```csharp
using Anela.Heblo.Domain.Features.Configuration;
```
to:
```csharp
using Anela.Heblo.Application.Features.FileStorage;
```
Note: lines 7 and 8 in this file are `using Anela.Heblo.Application.Features.FileStorage.Infrastructure.Jobs;` and `using Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;` — DIFFERENT namespaces, so no duplicate to remove.

- [ ] **Step 4: Swap the using directive in DownloadFromUrlHandlerTests.cs**

In `backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs`, change line 11 from:
```csharp
using Anela.Heblo.Domain.Features.Configuration;
```
to:
```csharp
using Anela.Heblo.Application.Features.FileStorage;
```
Note: line 7 of this file is already `using Anela.Heblo.Application.Features.FileStorage;`. After the swap there will be a duplicate. **Delete the now-duplicate line on (former) line 11** so the file has only one `using Anela.Heblo.Application.Features.FileStorage;` directive (the one already on line 7).

- [ ] **Step 5: Verify the tests project compiles**

```bash
cd backend && dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --nologo
```
Expected: `Build succeeded` with 0 errors.

---

## Task 8: Fix the DI registration in ServiceCollectionExtensions.cs

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`

Context: Line 363 is `services.Configure<ProductExportOptions>(configuration.GetSection("ProductExportOptions"));`. The file-level `using Anela.Heblo.Domain.Features.Configuration;` on line 10 **must stay** because other types from that namespace (`ConfigurationConstants`, `ApplicationConfiguration`) are referenced throughout the file (lines 37–39, 67, 76, 111, 116–117). What's missing is `using Anela.Heblo.Application.Features.FileStorage;` to resolve the moved `ProductExportOptions`.

Verify first whether the new using is already present (it may be there from existing `FileStorageModule.AddFileStorageModule(...)` registration code).

- [ ] **Step 1: Check if the new using directive is already present**

Run:
```bash
grep -n "using Anela.Heblo.Application.Features.FileStorage" backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs
```

- **If a match prints**, the using is already present. Skip Step 2 and go to Step 3.
- **If no match prints**, proceed to Step 2 to add it.

- [ ] **Step 2: Add the new using directive (only if Step 1 found no match)**

In `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`, find the block of `using` directives at the top of the file (lines 1–~30). Insert the new directive in alphabetical order among the existing `Anela.Heblo.Application.*` `using` lines:
```csharp
using Anela.Heblo.Application.Features.FileStorage;
```

Do **NOT** touch line 10 (`using Anela.Heblo.Domain.Features.Configuration;`) — it stays.
Do **NOT** modify line 363 — the `Configure<ProductExportOptions>(...)` call and the section key string `"ProductExportOptions"` are preserved verbatim per spec FR-5.

- [ ] **Step 3: Verify the API project compiles**

```bash
cd backend && dotnet build src/Anela.Heblo.API/Anela.Heblo.API.csproj --nologo
```
Expected: `Build succeeded` with 0 errors.

- [ ] **Step 4: Verify the full solution compiles**

```bash
cd backend && dotnet build src/Anela.Heblo.sln --nologo
```
Expected: `Build succeeded` with 0 errors and **no new warnings** introduced by this change (NFR-1). If new warnings appear (e.g., IDE0005 unused using), investigate and fix in this same task.

---

## Task 9: Run dotnet format to apply analyzer fixes

**Files:** any files touched in Tasks 1–8 may be normalised.

- [ ] **Step 1: Run dotnet format on the backend solution**

```bash
cd backend && dotnet format src/Anela.Heblo.sln --verify-no-changes
```
Expected: exit code 0, no diff. If exit code is non-zero, run without `--verify-no-changes` to apply fixes:

```bash
cd backend && dotnet format src/Anela.Heblo.sln
```

Then `git diff` and inspect: the diff should be limited to whitespace, `using` ordering, or IDE0005 removals on files this PR already touches. If `dotnet format` modifies files outside the inventory in Tasks 1–8, **revert those changes** (`git checkout -- <file>`) — they violate NFR-4 (minimal surgical change).

- [ ] **Step 2: Re-run verification**

```bash
cd backend && dotnet format src/Anela.Heblo.sln --verify-no-changes
```
Expected: exit code 0.

---

## Task 10: Final grep — confirm no orphan references

**Acceptance criterion FR-4**: no remaining reference to the old namespace path for `ProductExportOptions`.

- [ ] **Step 1: Confirm zero references to the old namespace for ProductExportOptions**

```bash
grep -rn "Anela.Heblo.Domain.Features.Configuration" backend/src backend/test | grep -v "//"
```
Expected: matches only in files that legitimately use `ConfigurationConstants` or `ApplicationConfiguration` (the remaining types in that namespace). No match should be on a line that mentions `ProductExportOptions`. To verify cross-line independence run:

```bash
grep -rln "Anela.Heblo.Domain.Features.Configuration" backend/src backend/test | xargs -I {} grep -l "ProductExportOptions" {} 2>/dev/null
```
Expected: zero output (no file both imports the old Configuration namespace AND references `ProductExportOptions`).

- [ ] **Step 2: Confirm all current references to ProductExportOptions are expected**

```bash
grep -rln "ProductExportOptions" backend/src backend/test
```
Expected: exactly these files (and ONLY these):
- `backend/src/Anela.Heblo.Application/Features/FileStorage/ProductExportOptions.cs` (the new class)
- `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/DownloadResilienceService.cs`
- `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJob.cs`
- `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs`
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`
- `backend/src/Anela.Heblo.API/appsettings.json` (config section — UNCHANGED)
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/Configuration/ProductExportOptionsTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageModuleTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/Infrastructure/DownloadResilienceServiceTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJobTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs`

If any extra file appears, investigate it before continuing.

- [ ] **Step 3: Confirm the old file is gone**

```bash
test ! -e backend/src/Anela.Heblo.Domain/Features/Configuration/ProductExportOptions.cs && echo "OK: old file removed" || echo "FAIL: old file still present"
```
Expected: `OK: old file removed`.

- [ ] **Step 4: Confirm the new file exists**

```bash
test -e backend/src/Anela.Heblo.Application/Features/FileStorage/ProductExportOptions.cs && echo "OK: new file present" || echo "FAIL: new file missing"
```
Expected: `OK: new file present`.

- [ ] **Step 5: Confirm appsettings.json section key is unchanged**

```bash
grep -n "ProductExportOptions" backend/src/Anela.Heblo.API/appsettings.json
```
Expected: at least one match showing the `"ProductExportOptions"` object key, identical to its previous form. The file should not appear in `git diff`:

```bash
git diff -- backend/src/Anela.Heblo.API/appsettings.json
```
Expected: empty diff.

---

## Task 11: Run the FileStorage test suite

**Acceptance criterion NFR-2**: all existing tests touching FileStorage / Configuration modules continue to pass.

- [ ] **Step 1: Run the FileStorage tests**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.FileStorage"
```
Expected: all tests pass, zero failures, zero skipped (unless a specific test was already in a skipped state pre-refactor).

- [ ] **Step 2: Run the full backend test suite**

```bash
cd backend && dotnet test src/Anela.Heblo.sln --no-build
```
Expected: pre-existing pass/fail counts. This change must not cause any net new test failures. If a pre-existing failure unrelated to this refactor is present, note it and proceed — it is not introduced by this PR.

---

## Task 12: Commit

- [ ] **Step 1: Review the staged changes**

```bash
git status
git diff --stat HEAD
```
Expected: exactly these files touched:
- Added: `backend/src/Anela.Heblo.Application/Features/FileStorage/ProductExportOptions.cs`
- Deleted: `backend/src/Anela.Heblo.Domain/Features/Configuration/ProductExportOptions.cs`
- Modified: 3 production files + 5 test files + 1 DI registration file (= 9 modifications)

Total: 11 file changes (1 add + 1 delete + 9 modify).

If anything outside this list appears, revert it (`git checkout -- <file>`) — it violates NFR-4.

- [ ] **Step 2: Stage all intended changes**

```bash
git add \
  backend/src/Anela.Heblo.Application/Features/FileStorage/ProductExportOptions.cs \
  backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/DownloadResilienceService.cs \
  backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJob.cs \
  backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs \
  backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs \
  backend/test/Anela.Heblo.Tests/Features/FileStorage/Configuration/ProductExportOptionsTests.cs \
  backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageModuleTests.cs \
  backend/test/Anela.Heblo.Tests/Features/FileStorage/Infrastructure/DownloadResilienceServiceTests.cs \
  backend/test/Anela.Heblo.Tests/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJobTests.cs \
  backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs
```

The deletion of `backend/src/Anela.Heblo.Domain/Features/Configuration/ProductExportOptions.cs` was already staged in Task 2 via `git rm`.

- [ ] **Step 3: Commit with a Conventional Commit message**

```bash
git commit -m "$(cat <<'EOF'
refactor: move ProductExportOptions to FileStorage module

Relocates ProductExportOptions from Domain/Features/Configuration to
Application/Features/FileStorage where its only consumers live. Aligns
with the project's Vertical Slice convention — every other single-file
options class (Article, Leaflet, OrgChart, MeetingTasks, KnowledgeBase)
sits inside its own feature slice.

Pure refactor: class body, defaults, section key ("ProductExportOptions"),
and DI binding semantics are byte-for-byte unchanged. Only namespace
and using directives moved.
EOF
)"
```

- [ ] **Step 4: Confirm the commit landed**

```bash
git log --stat -1
```
Expected: one new commit on the current branch showing the 11 file changes.

---

## Self-Review (already performed during plan authoring)

**Spec coverage:**
- FR-1 (relocate class) → Tasks 1 + 2.
- FR-2 (update consumer usings) → Tasks 3, 4, 5 (production) + Tasks 6, 7 (tests, per arch-review amendment).
- FR-3 (DI registration) → Task 8 (correct line is `:363`, not `:356` as in spec; file-level `using` for Domain.Configuration is preserved per arch-review amendment 3).
- FR-4 (no orphan references) → Task 10.
- FR-5 (preserve runtime behaviour) → Task 1 step 1 keeps property defaults and names byte-identical; Task 8 step 2 keeps section key string `"ProductExportOptions"` unchanged; Task 11 runs the tests.
- NFR-1 (build + format) → Tasks 8 step 4 + Task 9.
- NFR-2 (test stability) → Task 11.
- NFR-3 (no config / deployment change) → Task 10 step 5 verifies appsettings.json untouched. No KV / Docker / migration files appear in the inventory.
- NFR-4 (minimal surgical change) → Tasks 9 step 1 and Task 12 step 1 both check that only the 11 inventoried files appear in the diff.

**Placeholder scan:** No "TBD", "TODO", "etc.", or "handle edge cases" in any step. Every step has the exact command or exact code change to make.

**Type / name consistency:** `ProductExportOptions` spelled identically everywhere. New namespace `Anela.Heblo.Application.Features.FileStorage` used consistently across Task 1 (class), Tasks 6–7 (test usings), and Task 8 (DI file). Configuration section key string `"ProductExportOptions"` referenced consistently. All file paths exact and verified against the working tree.

**Skipped from arch-review amendments by design:**
- Amendment 4 (move `Configure<ProductExportOptions>(...)` into `FileStorageModule.AddFileStorageModule`) — explicitly out of scope per spec NFR-4. Tracked as a follow-up after this PR lands.
- Constant `SectionName = "ProductExportOptions"` on the class — arch-review Decision 4, out of scope.
- Renaming test folder `Configuration/` → `Tests/Features/FileStorage/` — arch-review Risk row 3, follow-up.
