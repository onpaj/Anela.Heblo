# Reuse `JsonSerializerOptions` in `OrgChartService` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the per-call `JsonSerializerOptions` allocation in `OrgChartService.GetOrganizationStructureAsync` with a `private static readonly` field so the instance (and its internal reflection-metadata cache) is built once at type-load time and reused on every request.

**Architecture:** Single-file refactor in `backend/src/Anela.Heblo.Application/Features/OrgChart/Services/OrgChartService.cs`. No DI registration, MediatR handler, interface, or DTO contracts are touched. The new field is `private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };` — matching the dominant convention used by 12+ existing call sites in this codebase (notably `backend/src/Adapters/Anela.Heblo.Adapters.OpenMeteo/OpenMeteoWeatherForecastClient.cs`, which is structurally identical).

**Tech Stack:** C# / .NET 8, `System.Text.Json`.

---

## Background context for the engineer

- The codebase convention is `private static readonly JsonSerializerOptions JsonOptions = new() { ... };` — PascalCase, no underscore, inline initializer. Confirmed across 12+ sites; do not invent a new naming or initialization style.
- `JsonSerializerOptions` becomes effectively read-only after first use; mutating it later throws at runtime. `private static readonly` is the correct scope.
- There are **zero existing tests** for `OrgChartService` (verified by the architecture review via `find backend/test -iname "*orgchart*"`). Do not invent tests for this refactor — the spec is explicit that none are required. The success bar is "existing test suite passes + build is clean + format is clean."
- The actual file path is `backend/src/Anela.Heblo.Application/Features/OrgChart/Services/OrgChartService.cs` (lowercase `backend`, `Services/` subfolder). The spec's path with `Backend/...` and no `Services/` segment is wrong — use the path stated here.
- This is a surgical refactor. Do not touch any other line, comment, formatting, or `using` directive in the file beyond what the steps below specify.

---

## File Structure

**Files modified (1):**
- `backend/src/Anela.Heblo.Application/Features/OrgChart/Services/OrgChartService.cs` — adds the `JsonOptions` static field and switches the `Deserialize` call to use it.

**Files created:** none.
**Files deleted:** none.
**Tests added:** none (per spec; no existing tests for this class).

---

## Tasks

### Task 1: Replace per-call `JsonSerializerOptions` with a `private static readonly` field

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/OrgChart/Services/OrgChartService.cs` (lines 13 and 39–44)

- [ ] **Step 1: Add the `JsonOptions` static field above the existing instance fields**

Open `backend/src/Anela.Heblo.Application/Features/OrgChart/Services/OrgChartService.cs`. Locate the field declaration block at the top of the class (currently line 13):

```csharp
public class OrgChartService : IOrgChartService
{
    private readonly HttpClient _httpClient;
    private readonly OrgChartOptions _options;
    private readonly ILogger<OrgChartService> _logger;
```

Insert the new static field as the first member of the class body, before the existing instance fields, so it reads:

```csharp
public class OrgChartService : IOrgChartService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly OrgChartOptions _options;
    private readonly ILogger<OrgChartService> _logger;
```

Rationale for placement: statics-first then instance fields is the prevailing convention at sister sites (e.g., `OpenMeteoWeatherForecastClient.cs:20`). Keeping a single blank line between the static and the instance block matches the surrounding style.

- [ ] **Step 2: Remove the local `options` allocation in `GetOrganizationStructureAsync`**

In the same file, locate lines 39–44 inside `GetOrganizationStructureAsync`:

```csharp
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var orgChart = JsonSerializer.Deserialize<OrgChartResponse>(content, options);
```

Replace that block with:

```csharp
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            var orgChart = JsonSerializer.Deserialize<OrgChartResponse>(content, JsonOptions);
```

That is: delete the four-line `var options = new JsonSerializerOptions { ... };` block (including its trailing blank line) and change the third argument of `Deserialize<OrgChartResponse>` from `options` to `JsonOptions`. Leave every other line in the method — `try`/`catch` structure, logging calls, null check, throw sites — untouched.

- [ ] **Step 3: Verify the `using System.Text.Json;` directive is still present**

The file already has `using System.Text.Json;` at line 1. Do not add, remove, or reorder `using` directives. Confirm by reading the top of the file; if line 1 is no longer `using System.Text.Json;`, stop and investigate — the file must not have been touched anywhere else.

- [ ] **Step 4: Run `dotnet build` and confirm it succeeds with no new warnings**

Run from the worktree root:

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: `Build succeeded.` with `0 Warning(s)` and `0 Error(s)`. If any new warning appears that was not present before this change, stop and fix it before continuing.

- [ ] **Step 5: Run `dotnet format` and confirm it reports no changes required**

Run from the worktree root:

```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes --include backend/src/Anela.Heblo.Application/Features/OrgChart/Services/OrgChartService.cs
```

Expected: exits with code `0` and no formatting differences. If the command reports formatting changes, run `dotnet format` without `--verify-no-changes` on the file, then re-run with the flag to confirm clean.

- [ ] **Step 6: Run the existing backend test suite and confirm all tests pass**

Run from the worktree root:

```bash
dotnet test backend/Anela.Heblo.sln --no-restore
```

Expected: all tests pass. There are no tests targeting `OrgChartService` directly, so this is a regression-safety check on the broader suite. If any test fails, the failure is almost certainly pre-existing or unrelated — read the failure carefully before concluding the refactor caused it. Do not modify any test as part of this task.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/OrgChart/Services/OrgChartService.cs
git commit -m "refactor(orgchart): reuse static JsonSerializerOptions in OrgChartService"
```

---

## Verification (end-to-end sanity check before declaring complete)

After Task 1 is committed, do one final read-through of the modified file to confirm:

- [ ] The class begins with `private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };`.
- [ ] No `var options = new JsonSerializerOptions { ... };` block remains anywhere in the file.
- [ ] The single `JsonSerializer.Deserialize<OrgChartResponse>(content, JsonOptions)` call is the only reference to `JsonOptions` in the file.
- [ ] No other lines in the file changed (compare with `git diff HEAD~1` — diff should be exactly: one block added at the top of the class, one block deleted in the method, one argument renamed on the `Deserialize` call).
- [ ] `dotnet build` clean, `dotnet format --verify-no-changes` clean, `dotnet test` clean.

If all four boxes check, the feature is done.

---

## Out of scope (do not do these, even if tempted)

- Do not audit or refactor other `JsonSerializerOptions` call sites elsewhere in the codebase. The spec explicitly excludes this (Out of Scope §1).
- Do not introduce a centralized `JsonDefaults`/`JsonOptions` registry in `Anela.Heblo.Application.Common`. Spec Out of Scope §3.
- Do not add converters, naming policies, or source-generated context. Spec Out of Scope §4.
- Do not add new tests for `OrgChartService`. Spec FR-2 acceptance criteria and arch review §3 both confirm none are required.
- Do not change the frontend `staleTime` or any caching behavior. Spec Out of Scope §2.
- Do not benchmark or instrument. Spec Out of Scope §5.
- Do not touch `OrgChartResponse` or any other DTO. Spec Out of Scope §6.
