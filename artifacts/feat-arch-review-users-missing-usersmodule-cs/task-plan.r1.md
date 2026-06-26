# UsersModule.cs DI Registration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a dedicated `UsersModule.cs` for the `Users` feature module, register `ICurrentUserService → CurrentUserService` inside it, aggregate it from `ApplicationModule.AddApplicationServices`, and remove the now-duplicated binding from the API-layer composition root.

**Architecture:** This is a structural refactor with zero behavior change. The single DI binding currently sitting in `Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:130` moves into a new sibling-shaped module file at `Anela.Heblo.Application/Features/Users/UsersModule.cs`, which is invoked from `ApplicationModule.AddApplicationServices`. `IHttpContextAccessor` registration stays in the API layer (its conceptual home). Lifetime (`Singleton`) is preserved verbatim because `CurrentUserService` is stateless and the captive `IHttpContextAccessor` uses ambient `AsyncLocal<T>` context — safe across requests.

**Tech Stack:** .NET 8, C#, `Microsoft.Extensions.DependencyInjection`, xUnit + FluentAssertions (existing test stack — no test additions required since this is a pure refactor with behavior parity).

---

## File Structure

Three files in play. No new abstractions, no folder moves, no test additions.

| Path | Action | Responsibility |
|------|--------|----------------|
| `backend/src/Anela.Heblo.Application/Features/Users/UsersModule.cs` | **CREATE** | Owns the `Users` feature module's DI registrations. Exposes `AddUsersModule(this IServiceCollection)`. Today registers a single binding (`ICurrentUserService → CurrentUserService`); becomes the natural home for any future Users-feature service. |
| `backend/src/Anela.Heblo.Application/ApplicationModule.cs` | **MODIFY** | Add 1 `using` and 1 `services.AddUsersModule();` line so the new module participates in standard Application-layer aggregation. |
| `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` | **MODIFY** | Delete the now-relocated binding + comment (line 130 and the comment above it). Delete two `using` directives (lines 9 and 11) that the deletion strands. `services.AddHttpContextAccessor()` (line 124) stays. |

---

## Task 1: Create `UsersModule.cs`

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Users/UsersModule.cs`

**Pre-flight context (read before editing):**

The conventions to mimic are visible in two sibling files. Read these first so you copy the shape exactly:

- `backend/src/Anela.Heblo.Application/Features/GridLayouts/GridLayoutsModule.cs` — file-scoped namespace, single static class, returns `services`. This is the preferred modern style; the new file follows this layout.
- `backend/src/Anela.Heblo.Application/Features/Journal/JournalModule.cs` — same pattern but block-scoped namespace; use it to confirm method-name convention (`AddJournalModule` ↔ `AddUsersModule`).

The binding to register lives currently at `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:130`. Copy it verbatim — same interface, same implementation type, same lifetime (`AddSingleton`). Lifetime parity is an explicit acceptance criterion (spec FR-5, NFR-1).

`ICurrentUserService` lives in namespace `Anela.Heblo.Domain.Features.Users` (verified at `backend/src/Anela.Heblo.Domain/Features/Users/ICurrentUserService.cs`). `CurrentUserService` lives in `Anela.Heblo.Application.Features.Users` (the same namespace as the new file) — so only the Domain `using` is required.

- [ ] **Step 1: Create the file with the exact content below**

Write the file at `backend/src/Anela.Heblo.Application/Features/Users/UsersModule.cs` with this content:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Anela.Heblo.Domain.Features.Users;

namespace Anela.Heblo.Application.Features.Users;

public static class UsersModule
{
    public static IServiceCollection AddUsersModule(this IServiceCollection services)
    {
        services.AddSingleton<ICurrentUserService, CurrentUserService>();
        return services;
    }
}
```

Notes:
- File-scoped namespace matches the two most-recently authored sibling modules (`GridLayoutsModule`, `UserManagementModule`) and modern `dotnet format` defaults.
- No XML doc comment — siblings don't have one and the spec mandates surgical changes.
- Returns `services` for fluent chaining (matches `JournalModule.cs:18`).
- `using Microsoft.Extensions.DependencyInjection;` is required for the `AddSingleton<,>` and `IServiceCollection` extension surfaces.
- `using Anela.Heblo.Domain.Features.Users;` is required so the unqualified `ICurrentUserService` symbol resolves.

- [ ] **Step 2: Verify the file compiles in isolation**

Run from the repo root:

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: `Build succeeded.` with `0 Error(s)`.

If you see a warning about an unused `using`, you've forgotten to reference `CurrentUserService` — re-check Step 1; the second `AddSingleton<ICurrentUserService, CurrentUserService>` argument is the concrete class living in the file-scoped namespace and pulls in the Domain namespace via the interface.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Users/UsersModule.cs
git commit -m "feat: add UsersModule.cs for Users feature DI registration"
```

---

## Task 2: Wire `AddUsersModule()` into `ApplicationModule`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs`

**Pre-flight context (read before editing):**

Open `backend/src/Anela.Heblo.Application/ApplicationModule.cs` and confirm two things:
1. The `using` block runs from roughly line 1 through line 46 in alphabetical-ish grouping. The line ordering is not strictly alphabetical (it groups by concept) so place the new `using` adjacent to the related `UserManagement` import (line 42) for readability.
2. The aggregation block inside `AddApplicationServices` runs from line 72 to line 113. Each line follows the pattern `services.AddXxxModule([configuration]);`. The new call sits adjacent to `services.AddUserManagement(configuration);` (line 86) — both touch identity-adjacent surfaces and live next to each other in the diff.

The new module's extension takes no `IConfiguration` parameter — it has no configuration-driven behavior today.

- [ ] **Step 1: Add the `using` directive**

Add the following line directly after the existing `using Anela.Heblo.Application.Features.UserManagement;` (line 42):

```csharp
using Anela.Heblo.Application.Features.Users;
```

Resulting block (around lines 42-43 after the edit):

```csharp
using Anela.Heblo.Application.Features.UserManagement;
using Anela.Heblo.Application.Features.Users;
using Anela.Heblo.Xcc.Services.Dashboard;
```

- [ ] **Step 2: Add the `services.AddUsersModule();` invocation**

Add the following line directly after `services.AddUserManagement(configuration);` (line 86) inside `AddApplicationServices`:

```csharp
        services.AddUsersModule();
```

Resulting block (around lines 86-88 after the edit):

```csharp
        services.AddUserManagement(configuration);
        services.AddUsersModule();
        services.AddOrgChartServices(configuration);
```

Indentation: 8 spaces (matches the surrounding lines — verify by inspection that the file uses 4-space indents and the method body sits at depth 2).

- [ ] **Step 3: Verify the Application project still builds**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: `Build succeeded.` with `0 Error(s)`. The Application assembly now contains both the new module file and the wiring call.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/ApplicationModule.cs
git commit -m "feat: wire UsersModule into ApplicationModule aggregation"
```

---

## Task 3: Remove the duplicate binding from the API composition root

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`

**Pre-flight context (read before editing):**

Open `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` and locate three regions:

1. **Line 9:** `using Anela.Heblo.Application.Features.Users;` — referenced only by `CurrentUserService` on line 130. Becomes unused after deletion.
2. **Line 11:** `using Anela.Heblo.Domain.Features.Users;` — referenced only by `ICurrentUserService` on line 130. Becomes unused after deletion.
3. **Lines 129-130:** the comment `// Register Current User Service` followed by `services.AddSingleton<ICurrentUserService, CurrentUserService>();`.

Before deleting either `using`, confirm with a grep that no other line in this file uses symbols from those namespaces. The two `using`s currently appear among an 18-line import block (lines 1-26). The verified call graph (per architecture review) is: both directives are referenced *only* by line 130. Run the verification grep below before deleting them — if either grep returns a match outside line 130, do **not** delete that `using`; report the unexpected reference instead and stop.

- [ ] **Step 1: Verify both `using` directives are dead after the planned deletion**

Run from the repo root:

```bash
grep -n "ICurrentUserService\|CurrentUserService" backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs
```

Expected: exactly one matching line — line 130, the binding to be deleted. If the grep returns any other line numbers, **STOP** and report the unexpected reference before continuing — the spec FR-3 acceptance criterion ("verify by grep before deletion") has been violated.

Also verify nothing else in the file references the `Anela.Heblo.Application.Features.Users` or `Anela.Heblo.Domain.Features.Users` namespaces:

```bash
grep -n "Application.Features.Users\|Domain.Features.Users" backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs
```

Expected: exactly two matches — line 9 and line 11 (the `using` directives themselves). No other matches.

- [ ] **Step 2: Delete the binding and its comment (lines 129-130)**

Remove these two consecutive lines from the file:

```csharp
        // Register Current User Service
        services.AddSingleton<ICurrentUserService, CurrentUserService>();
```

After the edit, the surrounding context (originally lines 126-133) should read:

```csharp
        // Register TimeProvider
        services.AddSingleton(TimeProvider.System);

        // Register HttpClient for E2E testing middleware
        services.AddHttpClient();
```

The blank line that previously separated the `// Register Current User Service` block from `// Register HttpClient for E2E testing middleware` collapses naturally — keep exactly one blank line between `services.AddSingleton(TimeProvider.System);` and `// Register HttpClient for E2E testing middleware`.

Do **not** touch `services.AddHttpContextAccessor();` on line 124 — that is an API-layer concern (per spec FR-4 and architecture review Decision 3).

- [ ] **Step 3: Delete the two stranded `using` directives (lines 9 and 11)**

Remove these two lines from the top of the file:

```csharp
using Anela.Heblo.Application.Features.Users;
```

```csharp
using Anela.Heblo.Domain.Features.Users;
```

After the edit, lines 8-12 (originally lines 8-12) should read:

```csharp
using Anela.Heblo.API.Infrastructure.Telemetry;
using Anela.Heblo.Domain.Features.Configuration;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.OpenApi.Models;
using Hangfire;
```

Keep `using Anela.Heblo.Domain.Features.Configuration;` (was line 10 — referenced elsewhere in the file for `ConfigurationConstants`) and `using Anela.Heblo.Domain.Features.BackgroundJobs;` (was line 12 — referenced for `IRecurringJob`).

- [ ] **Step 4: Verify the API project still builds**

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

Expected: `Build succeeded.` with `0 Error(s)` and `0 Warning(s)` introduced by the change. Pre-existing warnings elsewhere are fine, but **no new warnings** must originate from `ServiceCollectionExtensions.cs`.

If the build fails with `CS0246` (type not found), one of the `using` deletions removed a symbol still in use elsewhere — restore that specific `using` and re-run Step 1's grep to identify the additional reference.

If `dotnet format --verify-no-changes` flags an `IDE0005` (unused using) warning later, it means a `using` survived the deletion — recheck Step 3.

- [ ] **Step 5: Verify exactly one `AddSingleton<ICurrentUserService, ...>` registration exists in the entire backend**

This is the architecture-review-recommended sanity check (Risks table, row 1):

```bash
grep -rn "AddSingleton<ICurrentUserService" backend/src
```

Expected: exactly one hit — `backend/src/Anela.Heblo.Application/Features/Users/UsersModule.cs`, the new file. Multiple hits indicate the API-layer deletion was incomplete and "last-registration-wins" semantics will mask a divergence.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs
git commit -m "refactor: remove duplicate ICurrentUserService binding from API composition root"
```

---

## Task 4: Full-solution build and format verification

**Files:** No edits in this task — verification only.

**Pre-flight context:**

The refactor is structural and Application-layer, so a full-solution build is required to catch any consumer that, against expectations, references the moved binding through the API layer. The architecture review confirmed no such consumer exists, but this is the verification gate that makes that claim load-bearing.

- [ ] **Step 1: Build the full backend solution**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: `Build succeeded.` with `0 Error(s)`. Warnings count must be unchanged vs. `main` for the three affected files — pre-existing warnings elsewhere are tolerable.

- [ ] **Step 2: Run `dotnet format` and confirm clean**

```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: exit code `0`. If `dotnet format` reports `IDE0005` (unused using) or whitespace drift on any of the three changed files, fix the reported violations and re-run before continuing.

If `--verify-no-changes` mode is unavailable in the local SDK, run `dotnet format backend/Anela.Heblo.sln` and confirm `git diff` shows no further changes to the three files in scope.

- [ ] **Step 3: Run the full backend test suite**

```bash
dotnet test backend/Anela.Heblo.sln --no-build
```

Expected: all existing tests pass with zero new failures. Spec FR-5 explicitly requires "no test modifications expected" — if a test fails, you have introduced a behavior delta. Roll back and investigate; do **not** modify tests to make them pass.

The ~32 consumer files (handlers across Journal, Packaging, MeetingTasks, Catalog.Inventory, etc.) inject `ICurrentUserService` through the interface — none reference `CurrentUserService` directly outside of tests, and the relocated binding produces the same singleton resolution. A passing suite confirms behavior parity (NFR-1).

- [ ] **Step 4: Smoke-test resolution at startup**

```bash
dotnet run --project backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

Wait for the host to reach `Now listening on:` log line (typically 5-10s) and confirm no exception of the form `Unable to resolve service for type 'Anela.Heblo.Domain.Features.Users.ICurrentUserService'` appears in the console. Then `Ctrl+C` to stop.

If the resolution fails at startup, the most likely cause is that `services.AddUsersModule();` is missing from `ApplicationModule.cs` — re-check Task 2 Step 2. The second-most likely cause is that `services.AddHttpContextAccessor();` has been accidentally removed from `AddCrossCuttingServices` — re-check Task 3 Step 2 (it must still be on line 124, untouched).

- [ ] **Step 5: Final commit (only if Step 2 made any formatter changes)**

If `dotnet format` (Step 2) modified any file, commit those changes:

```bash
git status   # confirm what changed
git add -p   # stage only formatter-driven changes to the three files in scope
git commit -m "chore: dotnet format pass on Users module refactor"
```

If Step 2 reported no changes, skip this step — there is nothing to commit.

---

## Self-Review Checklist

Reviewed against `spec.r1.md`:

- **FR-1** (Create `UsersModule.cs`): Task 1 creates the file with the exact namespace, class name, method signature, and binding the spec mandates. File-scoped namespace per architecture-review Decision §"Namespace style suggestion".
- **FR-2** (Aggregate from `ApplicationModule`): Task 2 adds the `using` and the unconditional `services.AddUsersModule();` call adjacent to `AddUserManagement` (line 86), matching the spec's placement guidance.
- **FR-3** (Remove inline binding + prune unused `using`s): Task 3 deletes line 130 and its comment, and deletes the two `using` directives confirmed dead by Step 1 grep. `AddHttpContextAccessor()` is explicitly preserved (Step 2 note).
- **FR-4** (Preserve DI registration order semantics): Task 4 Step 4 smoke-tests startup resolution; `Program.cs` ordering is not touched in any task; `IHttpContextAccessor` stays in the API layer per architecture-review Decision 3.
- **FR-5** (All existing tests pass): Task 4 Step 3 runs the full suite. No test modifications planned.
- **NFR-1** (Behavior parity): Lifetime is `Singleton` verbatim (Task 1 Step 1); claim-resolution code in `CurrentUserService.cs` is untouched.
- **NFR-2** (Convention conformance): Task 1's file shape matches `JournalModule` / `GridLayoutsModule` siblings exactly.
- **NFR-3** (Surgical change): Only the three files listed in the spec's "Affected Files" table are touched. No adjacent cleanup, no comment rewording outside the immediate change site.

Placeholder scan: no `TBD`, no "add appropriate error handling", no "similar to Task N", no untyped references. Every code-touching step includes the full code block.

Type consistency: `UsersModule` ↔ `AddUsersModule` ↔ `services.AddUsersModule();` aligned across Tasks 1, 2, and 4.
