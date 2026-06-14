# Verify and Document ProductExportOptions Module Ownership Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close a stale arch-review finding by verifying that `ProductExportOptions` is already owned by `CatalogModule` (not the API layer), add a single-module xUnit regression guard so the binding cannot silently drift, and leave durable trail markers (decision memory + resolution artifact) so the daily arch-review routine and any future reviewer can find the resolution.

**Architecture:** Three new files. Zero changes to production code. The xUnit test instantiates `CatalogModule.AddCatalogModule(services, configuration)` in isolation (no `WebApplicationFactory`), seeds an in-memory `IConfiguration`, resolves `IOptions<ProductExportOptions>`, and asserts both `Url` and `ContainerName` round-trip from the `"ProductExportOptions"` configuration section. Mirroring the `FileStorageModuleTests` pattern keeps the test cheap, local, and fails closed if either the `Configure<>` line is removed or the section name is changed.

**Tech Stack:** .NET 8, xUnit, Moq, Microsoft.Extensions.Configuration (in-memory provider), Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Options.

---

## File Structure

| File | Status | Responsibility |
|------|--------|---------------|
| `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogModuleProductExportOptionsTests.cs` | **Create** | xUnit regression guard. Verifies `CatalogModule.AddCatalogModule` binds `IOptions<ProductExportOptions>` from the `"ProductExportOptions"` configuration section. Fails closed if either the `Configure<>` line is deleted or the section name is repointed. |
| `memory/decisions/product-export-options-ownership.md` | **Create** | Decision record stating Catalog owns both `ProductExportOptions` and `ProductExportDownloadJob`; FileStorage exposes only generic primitives. Companion to `repository-di-in-feature-module.md` (same ADR-004 principle applied to options bindings). |
| `artifacts/feat-arch-review-filestorage-productexportopt/resolution.md` | **Create** | Per-branch routine close-out note. Records the original finding, current binding location, conclusion ("not applicable — already resolved"), and links to the guard test + decision memory. |
| `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` | **Read-only (verify)** | Already binds `ProductExportOptions` at line 114. No edits. |
| `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` | **Read-only (verify)** | Already contains no reference to `ProductExportOptions`. No edits. |

**Important signature note before writing the test:** `AddCatalogModule` takes **only** `(IServiceCollection services, IConfiguration configuration)` — it does **not** take `IHostEnvironment`. The arch-review's code sketch mentioned `env`, but the actual signature is two-parameter. Pass only `services` and `configuration`.

---

## Task 1: Verify the Current Binding Satisfies ADR-004 (FR-1)

This task records confirmation evidence in the worktree's session before changing anything. It produces no commits; it establishes the baseline the regression guard will defend.

**Files:**
- Read-only inspection: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs:114`
- Read-only inspection: `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`

- [ ] **Step 1: Confirm there is exactly one `Configure<ProductExportOptions>` call in production source**

Run from worktree root:

```bash
rg -n "Configure<ProductExportOptions>" backend/src
```

Expected output (exactly one line):

```
backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs:114:        services.Configure<ProductExportOptions>(configuration.GetSection("ProductExportOptions"));
```

If you see two or more lines, or any path under `backend/src/Anela.Heblo.API/`, **stop** — the spec's baseline assumption is broken and the plan needs amendment before proceeding.

- [ ] **Step 2: Confirm `ServiceCollectionExtensions.cs` has no reference to `ProductExportOptions`**

Run:

```bash
rg -n "ProductExportOptions" backend/src/Anela.Heblo.API/
```

Expected output: zero matches (the command exits with status 1; that is fine and expected).

If anything matches, **stop** and reconcile with the spec — the API layer must not bind or reference this options type.

- [ ] **Step 3: Confirm `ProductExportOptions` lives only in the Catalog feature folder**

Run:

```bash
rg -l "ProductExportOptions" backend/src
```

Expected output (exactly these two paths):

```
backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs
backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/ProductExportOptions.cs
```

Plus possibly `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/Jobs/ProductExportDownloadJob.cs` (the consumer). If anything is listed outside `Features/Catalog/`, **stop** — investigate before proceeding.

- [ ] **Step 4: No commit for this task**

This task is verification only. Proceed to Task 2.

---

## Task 2: Write the Regression Guard Test (FR-2)

A single-module xUnit test that builds a minimal `ServiceCollection`, registers `CatalogModule` with an in-memory `IConfiguration`, resolves `IOptions<ProductExportOptions>`, and asserts both fields round-trip. Mirrors `FileStorageModuleTests` exactly. xUnit assertions only — no FluentAssertions (consistent with the file we are mirroring).

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogModuleProductExportOptionsTests.cs`

- [ ] **Step 1: Write the failing test file**

Create `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogModuleProductExportOptionsTests.cs` with this exact content:

```csharp
using System.Collections.Generic;
using Anela.Heblo.Application.Features.Catalog;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

/// <summary>
/// Regression guard for ADR-004: ProductExportOptions must be bound by CatalogModule
/// (the owner of both the options type and its sole consumer, ProductExportDownloadJob)
/// — never by the API layer. See memory/decisions/product-export-options-ownership.md.
/// </summary>
public class CatalogModuleProductExportOptionsTests
{
    private const string ExpectedUrl = "https://example.invalid/export";
    private const string ExpectedContainerName = "product-exports";

    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProductExportOptions:Url"] = ExpectedUrl,
                ["ProductExportOptions:ContainerName"] = ExpectedContainerName,
            })
            .Build();

    [Fact]
    public void AddCatalogModule_BindsProductExportOptions_FromConfigurationSection()
    {
        // Arrange — only CatalogModule wires DI; the API layer is intentionally NOT involved.
        // Pre-seed ILogger<> so any logger-injecting registration the module makes can resolve.
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddCatalogModule(BuildConfiguration());

        // Act
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ProductExportOptions>>().Value;

        // Assert — round-trip both fields. Fails closed if the Configure<T> call is deleted
        // from CatalogModule or repointed at the wrong configuration section name.
        Assert.Equal(ExpectedUrl, options.Url);
        Assert.Equal(ExpectedContainerName, options.ContainerName);
    }
}
```

- [ ] **Step 2: Run the test to confirm it passes against the current (correct) binding**

From worktree root:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CatalogModuleProductExportOptionsTests" \
  --nologo
```

Expected: `Passed: 1, Failed: 0`. The test passes because `CatalogModule.cs:114` already binds the options correctly.

If the test **fails** at this point, troubleshoot before continuing:

- `OptionsValidationException` → not expected; the options type has no validators. Re-read `ProductExportOptions.cs` and confirm.
- `InvalidOperationException` at `services.AddCatalogModule(...)` → some registration needs a dependency at registration time. Re-read the failure stack; the most likely missing dependency is an `IConfiguration` section other modules read. Add the minimum extra in-memory keys to satisfy that registration — **do not** weaken the assertion or skip the test.
- Assertion failure (`options.Url == null` or empty) → the bound section name in `CatalogModule.cs:114` does not match the test's seeded keys. Confirm both sides use the literal string `"ProductExportOptions"`. If `CatalogModule.cs` uses a different section, **stop** — Task 1 should have caught this.

- [ ] **Step 3: Prove the test fails closed — temporarily delete the binding line and re-run**

This is a one-shot manual sanity check. **Do not commit the deletion.**

Open `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` and comment out line 114:

```csharp
// services.Configure<ProductExportOptions>(configuration.GetSection("ProductExportOptions"));
```

Re-run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CatalogModuleProductExportOptionsTests" \
  --nologo
```

Expected: `Failed: 1`. The assertion `Assert.Equal(ExpectedUrl, options.Url)` should fail because the un-bound `ProductExportOptions.Url` defaults to its initializer (`= null!`), which is `null`.

**Restore the line** before continuing:

```csharp
services.Configure<ProductExportOptions>(configuration.GetSection("ProductExportOptions"));
```

Re-run the test to confirm `Passed: 1` again. Verify with `git status` that no production file is left modified:

```bash
git status backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs
```

Expected: clean (the file is unchanged from `HEAD`).

- [ ] **Step 4: Run the wider Catalog test slice to confirm no collateral damage**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Catalog" \
  --nologo
```

Expected: all Catalog tests pass. If anything that was green before this task is now red, the new test file is interfering — most likely a missing `using` or a name collision. Resolve before committing.

- [ ] **Step 5: Run `dotnet format` on the new file**

```bash
dotnet format backend/Anela.Heblo.sln --include backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogModuleProductExportOptionsTests.cs --verify-no-changes
```

If `--verify-no-changes` reports diffs, drop the flag and re-run to apply them:

```bash
dotnet format backend/Anela.Heblo.sln --include backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogModuleProductExportOptionsTests.cs
```

Then re-run the verify command to confirm clean.

- [ ] **Step 6: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogModuleProductExportOptionsTests.cs
git commit -m "test: add CatalogModule ProductExportOptions binding regression guard"
```

---

## Task 3: Write the Decision Memory (FR-3)

A `memory/decisions/*.md` record that states Catalog owns `ProductExportOptions` and explains why. Follows the same shape as `repository-di-in-feature-module.md` (the existing ADR-004 sister record).

**Files:**
- Create: `memory/decisions/product-export-options-ownership.md`

- [ ] **Step 1: Write the decision file**

Create `memory/decisions/product-export-options-ownership.md` with this exact content:

```markdown
# Decision: ProductExportOptions Is Owned by the Catalog Module

**Decision:** `ProductExportOptions` and its sole consumer `ProductExportDownloadJob`
both live in the Catalog vertical slice (`Anela.Heblo.Application.Features.Catalog.Infrastructure`).
The DI binding `services.Configure<ProductExportOptions>(configuration.GetSection("ProductExportOptions"))`
lives in `CatalogModule.AddCatalogModule` (currently `CatalogModule.cs:114`). FileStorage exposes
only generic download/upload primitives (`IBlobStorageService`, `FileDownloadOptions`) and
does **not** own anything specific to product exports. (ADR-004 in
`docs/architecture/development_guidelines.md`.)

**Why:** Two earlier plans considered this question and converged on the current placement:
`docs/superpowers/plans/2026-06-02-relocate-productexportoptions-to-filestorage.md` proposed
moving the options into FileStorage. That decision was superseded by
`docs/superpowers/plans/2026-06-12-relocate-productexportdownloadjob-to-catalog.md`, which
moved both the options type **and** the consuming job into Catalog. Reason: the consumer is a
catalog-data refresh job, not a generic file operation; under ADR-004 each vertical slice
owns the options its module reads. Co-locating the binding with the consumer prevents a
recurring cross-module wiring split — exactly the same principle as the repository-binding
ruling in `[[repository-di-in-feature-module]]`.

**How to apply:**
- The binding line stays at `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`
  (line 114 at time of writing — line number may drift, but the file is stable).
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` must **never** bind
  `ProductExportOptions`. If a future arch-review or audit recommends moving it there, reject
  the recommendation and link back to this memo.
- Future arch-review iterations that re-file a "FileStorage owns ProductExportOptions"
  finding should be closed by pointing at this memo and at
  `artifacts/feat-arch-review-filestorage-productexportopt/resolution.md`.
- Regression guard:
  `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogModuleProductExportOptionsTests.cs`
  fails closed if the binding is deleted from `CatalogModule` or repointed at the wrong
  configuration section.
- Companion to `[[repository-di-in-feature-module]]` — the same ADR-004 principle applied to
  options bindings rather than repository bindings.
- Follow-up (out of scope here): a cross-module convention test that scans every `*Module.cs`
  and asserts each `Configure<T>` call sits in the owning module is a worthwhile future
  investment but should be tracked as its own task.
```

- [ ] **Step 2: Verify the file exists and reads correctly**

```bash
ls -la memory/decisions/product-export-options-ownership.md
head -5 memory/decisions/product-export-options-ownership.md
```

Expected: file exists, first line is `# Decision: ProductExportOptions Is Owned by the Catalog Module`.

- [ ] **Step 3: Confirm no `memory/decisions/` index file requires updating**

```bash
ls memory/decisions/ | grep -i 'index\|readme'
```

Expected: no matches. The directory currently has no index/readme, so no further edit is needed.

If the command returns a hit (someone added an index between when this plan was written and when you execute it), open it, follow its existing style, and add a one-line entry for the new memo before committing.

- [ ] **Step 4: Commit**

```bash
git add memory/decisions/product-export-options-ownership.md
git commit -m "docs: record decision that Catalog owns ProductExportOptions"
```

---

## Task 4: Write the Resolution Artifact (FR-4)

A per-branch close-out note in the `artifacts/feat-arch-review-filestorage-productexportopt/` directory the daily arch-review routine already uses. Records that the brief's finding has been verified as stale and lists the durable trail markers (test + decision memo).

**Files:**
- Create: `artifacts/feat-arch-review-filestorage-productexportopt/resolution.md`

- [ ] **Step 1: Write the resolution file**

Create `artifacts/feat-arch-review-filestorage-productexportopt/resolution.md` with this exact content:

```markdown
# Resolution: ProductExportOptions ownership (FileStorage arch-review finding)

**Source:** daily arch-review routine, 2026-06-05, FileStorage module.
The brief claimed `ProductExportOptions` was being bound in
`Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:364` — outside the owning module —
and suggested moving the binding into `FileStorageModule`.

**Current state (verified 2026-06-14):**
- `ProductExportOptions` is bound exactly once, in
  `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs:114`.
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` contains
  zero references to `ProductExportOptions`. Line 364 currently binds `HangfireOptions`.
- `ProductExportOptions` lives in `Anela.Heblo.Application.Features.Catalog.Infrastructure`.
- The sole consumer, `ProductExportDownloadJob`, lives in
  `Anela.Heblo.Application.Features.Catalog.Infrastructure.Jobs`.

**Conclusion:** Not applicable — already resolved by the
`docs/superpowers/plans/2026-06-12-relocate-productexportdownloadjob-to-catalog.md` plan,
which moved both `ProductExportOptions` and `ProductExportDownloadJob` into Catalog. The
brief's suggested fix (move the binding into `FileStorageModule`) would **reintroduce** the
ADR-004 violation it claims to fix, because the option type and its consumer both belong to
Catalog — not to FileStorage.

**Durable trail markers added in this branch:**
- Regression guard:
  `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogModuleProductExportOptionsTests.cs`
- Decision record: `memory/decisions/product-export-options-ownership.md`

**For future arch-review runs:** any re-filing of this same finding should be closed by
linking to this resolution and to the decision memo. The guard test will fail closed if
someone moves the binding back to the API layer or to `FileStorageModule`.
```

- [ ] **Step 2: Verify the file**

```bash
ls -la artifacts/feat-arch-review-filestorage-productexportopt/resolution.md
head -3 artifacts/feat-arch-review-filestorage-productexportopt/resolution.md
```

Expected: file exists, first line is `# Resolution: ProductExportOptions ownership (FileStorage arch-review finding)`.

- [ ] **Step 3: Commit**

```bash
git add artifacts/feat-arch-review-filestorage-productexportopt/resolution.md
git commit -m "docs: record resolution for FileStorage ProductExportOptions arch-review finding"
```

---

## Task 5: Full Validation and PR Description Preparation

Final gate before declaring the work shippable. Runs the project's standard validation commands and produces the one-line PR description summary the spec requires (FR-4 acceptance criterion 2).

**Files:**
- No file changes in this task. Validates the previous three tasks together.

- [ ] **Step 1: Build the backend solution**

```bash
dotnet build backend/Anela.Heblo.sln --nologo
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (warning counts may differ, but no errors). If new warnings appear that trace to the new test file, fix them.

- [ ] **Step 2: Re-verify `dotnet format` on the touched project**

```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: clean exit (no diffs). If the command reports formatting differences in unrelated files, that is pre-existing drift — do **not** auto-fix unrelated files in this PR (per project rule: "Surgical changes. Touch only what the task requires."). Re-run with `--include` scoped to only the file we created:

```bash
dotnet format backend/Anela.Heblo.sln --include backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogModuleProductExportOptionsTests.cs --verify-no-changes
```

This must report no changes.

- [ ] **Step 3: Run the full Catalog test slice once more, end-to-end**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Catalog" \
  --nologo
```

Expected: all green. New test (`CatalogModuleProductExportOptionsTests.AddCatalogModule_BindsProductExportOptions_FromConfigurationSection`) appears in the output and passes.

- [ ] **Step 4: Final grep confirming the spec's acceptance criteria still hold**

```bash
rg -n "Configure<ProductExportOptions>" backend/src
rg -n "ProductExportOptions" backend/src/Anela.Heblo.API/
```

Expected:
- First command: exactly one hit at `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs:114`.
- Second command: zero matches (exit status 1, which is fine).

- [ ] **Step 5: Confirm all three new files exist and are tracked**

```bash
git ls-files \
  backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogModuleProductExportOptionsTests.cs \
  memory/decisions/product-export-options-ownership.md \
  artifacts/feat-arch-review-filestorage-productexportopt/resolution.md
```

Expected: all three paths listed. If any file is missing from the listing, it was never committed — go back to the relevant task and commit it.

- [ ] **Step 6: Prepare the PR description snippet**

When the PR is opened, the description body must include this one-line summary (per spec FR-4 acceptance criterion 2). Add it under the PR's `## Summary` heading:

```markdown
Closes the daily arch-review FileStorage `ProductExportOptions` finding as stale — the
binding already lives in `CatalogModule` per the 2026-06-12 ownership decision. See
[`artifacts/feat-arch-review-filestorage-productexportopt/resolution.md`](../blob/feat-arch-review-filestorage-productexportopt/artifacts/feat-arch-review-filestorage-productexportopt/resolution.md)
and [`memory/decisions/product-export-options-ownership.md`](../blob/feat-arch-review-filestorage-productexportopt/memory/decisions/product-export-options-ownership.md);
new test `CatalogModuleProductExportOptionsTests` defends the binding.
```

(If the link form above gives the wrong path once `gh pr create` resolves the branch — for example because the repository hosts the PR view differently — substitute relative paths like `artifacts/feat-arch-review-filestorage-productexportopt/resolution.md`. The important thing is the human reviewer can click through to both files.)

- [ ] **Step 7: No commit for this task**

This task is validation only. The PR description snippet is for use at PR-creation time and not stored as a file.

---

## Self-Review

**Spec coverage:**

| Spec requirement | Implemented in |
|------------------|----------------|
| FR-1 (confirm current binding satisfies ADR-004) | Task 1 (verification greps), Task 5 (re-confirmed at end) |
| FR-2 (regression guard test) | Task 2 (writes test, runs it, confirms it fails closed when binding is removed) |
| FR-3 (document module ownership decision) | Task 3 (writes `memory/decisions/product-export-options-ownership.md`, cross-references both prior plans, sister-records `repository-di-in-feature-module.md`, names affected file paths) |
| FR-4 (close arch-review finding) | Task 4 (writes resolution artifact), Task 5 Step 6 (PR description one-liner) |
| NFR-1 / NFR-2 | N/A — no runtime behavior changes |
| NFR-3 (backward compatibility) | Guaranteed — zero production code changes; configuration section name and shape untouched |
| NFR-4 (build & lint gates) | Task 5 Steps 1, 2 |

**Placeholder scan:** No `TBD`, no `add appropriate error handling`, no "implement later". Every code block is the actual file contents to write.

**Type / name consistency:** Test name `CatalogModuleProductExportOptionsTests`, fact method `AddCatalogModule_BindsProductExportOptions_FromConfigurationSection`, and the seeded section name `"ProductExportOptions"` are used identically across every place they appear in this plan and match the production code at `CatalogModule.cs:114`. The `AddCatalogModule` call uses the two-parameter signature `(IServiceCollection, IConfiguration)` — confirmed against the actual source file, not the arch-review's three-parameter sketch.
