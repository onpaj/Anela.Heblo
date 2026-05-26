# Relocate `CurrentUserService` Out of Application Layer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the `CurrentUserService` implementation out of the Application layer to the API layer, consolidate Users DI wiring into a `UsersModule.cs`, and remove the `Microsoft.AspNetCore.Http.Abstractions` package reference from the Application csproj so the Clean Architecture boundary is enforced at compile time.

**Architecture:** The interface `ICurrentUserService` already lives in `Anela.Heblo.Domain.Features.Users` (no change). The concrete implementation moves verbatim to `Anela.Heblo.API/Features/Users/CurrentUserService.cs` (namespace `Anela.Heblo.API.Features.Users`). A new `UsersModule.cs` in the same folder exposes `AddUsersModule()` that registers `IHttpContextAccessor` + `AddSingleton<ICurrentUserService, CurrentUserService>()`. Program.cs invokes `AddUsersModule()` immediately before `AddCrossCuttingServices()`. The `Microsoft.AspNetCore.Http.Abstractions` package reference is removed from `Anela.Heblo.Application.csproj`, and a new architecture test locks the rule in.

**Tech Stack:** .NET 8, ASP.NET Core, xUnit, FluentAssertions, Moq, MediatR (handlers consume `ICurrentUserService` unchanged).

---

## File Structure

**Create:**
- `backend/src/Anela.Heblo.API/Features/Users/CurrentUserService.cs` — moved verbatim from Application; new namespace `Anela.Heblo.API.Features.Users`.
- `backend/src/Anela.Heblo.API/Features/Users/UsersModule.cs` — composition root for the Users module; exposes `AddUsersModule(this IServiceCollection)`.

**Delete:**
- `backend/src/Anela.Heblo.Application/Features/Users/CurrentUserService.cs` — moved.
- `backend/src/Anela.Heblo.Application/Features/Users/` directory if it becomes empty.

**Modify:**
- `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — remove `Microsoft.AspNetCore.Http.Abstractions` package reference (line 21).
- `backend/src/Anela.Heblo.Application/Features/UserManagement/UserManagementModule.cs` — remove unused `using Microsoft.AspNetCore.Http;` (line 6).
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — remove inline `AddHttpContextAccessor()` and `AddSingleton<ICurrentUserService, CurrentUserService>()` from `AddCrossCuttingServices()`; remove now-unused usings.
- `backend/src/Anela.Heblo.API/Program.cs` — add `builder.Services.AddUsersModule();` before `AddCrossCuttingServices()`.
- `backend/test/Anela.Heblo.Tests/Features/Users/CurrentUserServiceTests.cs` — update `using Anela.Heblo.Application.Features.Users;` → `using Anela.Heblo.API.Features.Users;`.
- `backend/test/Anela.Heblo.Tests/Application/Users/CurrentUserServiceIsInRoleTests.cs` — same using update.
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — add an `Application_should_not_reference_AspNetCore` test (locks in NFR-3 as a CI gate).

---

## Task 1: Establish green baseline

**Files:** none modified — pure verification before changes.

- [ ] **Step 1: Build the backend solution**

Run:
```bash
cd backend && dotnet build
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (or pre-existing warnings only).

- [ ] **Step 2: Run the existing `CurrentUserService` tests to confirm they currently pass**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~CurrentUserService" --no-build
```
Expected: All tests in `CurrentUserServiceTests` (8 tests) and `CurrentUserServiceIsInRoleTests` (3 tests) PASS — 11 total green.

- [ ] **Step 3: Run the existing module-boundary architecture tests**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ModuleBoundariesTests" --no-build
```
Expected: All existing rules PASS.

- [ ] **Step 4: Confirm baseline grep state**

Run:
```bash
grep -rn "Microsoft.AspNetCore" backend/src/Anela.Heblo.Application
```
Expected output (exactly two lines): the `using Microsoft.AspNetCore.Http;` at `Features/UserManagement/UserManagementModule.cs:6` and the source file `Features/Users/CurrentUserService.cs:3`. The csproj `PackageReference` should also be visible if you grep the csproj — that's the package we will remove.

- [ ] **Step 5: No commit — this task is verification only.**

---

## Task 2: Move `CurrentUserService` from Application to API project

**Files:**
- Create: `backend/src/Anela.Heblo.API/Features/Users/CurrentUserService.cs`
- Delete: `backend/src/Anela.Heblo.Application/Features/Users/CurrentUserService.cs`
- Modify: `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` (update `using` for the relocated type)
- Modify: `backend/test/Anela.Heblo.Tests/Features/Users/CurrentUserServiceTests.cs` (`using` only)
- Modify: `backend/test/Anela.Heblo.Tests/Application/Users/CurrentUserServiceIsInRoleTests.cs` (`using` only)

- [ ] **Step 1: Create the new file with the implementation verbatim under the API namespace**

Create `backend/src/Anela.Heblo.API/Features/Users/CurrentUserService.cs` with this exact content:

```csharp
using System.Security.Claims;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.AspNetCore.Http;

namespace Anela.Heblo.API.Features.Users;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public CurrentUser GetCurrentUser()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var isAuthenticated = user?.Identity?.IsAuthenticated ?? false;

        var name = user?.Identity?.Name
                   ?? user?.FindFirst(ClaimTypes.Name)?.Value
                   ?? user?.FindFirst("name")?.Value
                   ?? (isAuthenticated ? "Unknown User" : "Anonymous");

        var id = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                 ?? user?.FindFirst("sub")?.Value
                 ?? user?.FindFirst("oid")?.Value;

        // Entra ID access tokens omit the `email` claim by default; the user's
        // email/UPN lives in `preferred_username` (and sometimes `upn`).
        // ClaimTypes.Upn covers the legacy JwtSecurityTokenHandler claim remap.
        var email = user?.FindFirst(ClaimTypes.Email)?.Value
                    ?? user?.FindFirst("email")?.Value
                    ?? user?.FindFirst("preferred_username")?.Value
                    ?? user?.FindFirst(ClaimTypes.Upn)?.Value
                    ?? user?.FindFirst("upn")?.Value;

        return new CurrentUser(
            Id: id,
            Name: name,
            Email: email,
            IsAuthenticated: isAuthenticated
        );
    }

    public bool IsInRole(string role)
    {
        return _httpContextAccessor.HttpContext?.User?.IsInRole(role) ?? false;
    }
}
```

- [ ] **Step 2: Delete the old implementation file**

Run:
```bash
rm backend/src/Anela.Heblo.Application/Features/Users/CurrentUserService.cs
```

Then check whether the directory is now empty and delete it if so:
```bash
rmdir backend/src/Anela.Heblo.Application/Features/Users 2>/dev/null || true
```
Expected: directory removed if empty; the command silently no-ops if not.

- [ ] **Step 3: Update the `using` in `ServiceCollectionExtensions.cs` to the new namespace**

Open `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`. Find line 9:

```csharp
using Anela.Heblo.Application.Features.Users;
```

Replace with:

```csharp
using Anela.Heblo.API.Features.Users;
```

Leave the rest of the file unchanged for this task; the inline DI registration on line 130 will be removed in Task 3.

- [ ] **Step 4: Update the `using` in `CurrentUserServiceTests.cs`**

Open `backend/test/Anela.Heblo.Tests/Features/Users/CurrentUserServiceTests.cs`. Find line 2:

```csharp
using Anela.Heblo.Application.Features.Users;
```

Replace with:

```csharp
using Anela.Heblo.API.Features.Users;
```

- [ ] **Step 5: Update the `using` in `CurrentUserServiceIsInRoleTests.cs`**

Open `backend/test/Anela.Heblo.Tests/Application/Users/CurrentUserServiceIsInRoleTests.cs`. Find line 2:

```csharp
using Anela.Heblo.Application.Features.Users;
```

Replace with:

```csharp
using Anela.Heblo.API.Features.Users;
```

- [ ] **Step 6: Add a project reference from the test project to the API project if it does not already exist**

The test project must be able to resolve `Anela.Heblo.API.Features.Users.CurrentUserService`. Inspect:

```bash
cat backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj | grep -i ProjectReference
```

Expected: an entry like `<ProjectReference Include="..\..\src\Anela.Heblo.API\Anela.Heblo.API.csproj" />` already exists (the tests already reference API). If — and only if — no such reference exists, add it inside the existing `<ItemGroup>` containing other `ProjectReference` entries:

```xml
<ProjectReference Include="..\..\src\Anela.Heblo.API\Anela.Heblo.API.csproj" />
```

Do not add the reference if it is already present.

- [ ] **Step 7: Build the solution**

Run:
```bash
cd backend && dotnet build
```
Expected: `Build succeeded.` with no new warnings or errors.

- [ ] **Step 8: Run the relocated tests to confirm they still pass**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~CurrentUserService" --no-build
```
Expected: same 11 tests PASS — behavior is unchanged.

- [ ] **Step 9: Confirm no stale references to the old namespace remain**

Run:
```bash
grep -rn "Anela\.Heblo\.Application\.Features\.Users" backend/
```
Expected: zero results. (The only files that referenced the old namespace were the three updated in Steps 3–5.)

- [ ] **Step 10: Commit**

```bash
git add backend/src/Anela.Heblo.API/Features/Users/CurrentUserService.cs \
        backend/src/Anela.Heblo.Application/Features/Users \
        backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs \
        backend/test/Anela.Heblo.Tests/Features/Users/CurrentUserServiceTests.cs \
        backend/test/Anela.Heblo.Tests/Application/Users/CurrentUserServiceIsInRoleTests.cs
git commit -m "refactor: move CurrentUserService implementation from Application to API layer"
```

---

## Task 3: Create `UsersModule.cs` and re-route DI through it

**Files:**
- Create: `backend/src/Anela.Heblo.API/Features/Users/UsersModule.cs`
- Modify: `backend/src/Anela.Heblo.API/Program.cs` (add `AddUsersModule()` call)
- Modify: `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` (remove inline registration + now-unused usings)

- [ ] **Step 1: Create `UsersModule.cs` exposing `AddUsersModule()`**

Create `backend/src/Anela.Heblo.API/Features/Users/UsersModule.cs` with this exact content:

```csharp
using Anela.Heblo.Domain.Features.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.API.Features.Users;

public static class UsersModule
{
    public static IServiceCollection AddUsersModule(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();

        // Lifetime preserved from prior registration; HttpContextAccessor uses AsyncLocal
        // so per-request reads remain correct under a singleton.
        services.AddSingleton<ICurrentUserService, CurrentUserService>();

        return services;
    }
}
```

- [ ] **Step 2: Wire `AddUsersModule()` into `Program.cs` immediately before `AddCrossCuttingServices()`**

Open `backend/src/Anela.Heblo.API/Program.cs`. Find this block (around lines 76–78):

```csharp
        builder.Services.AddScoped<ISmartsuppWebhookMetrics, SmartsuppWebhookMetrics>();
        builder.Services.AddXccServices(builder.Configuration); // Cross-cutting concerns (audit, telemetry, etc.)
        builder.Services.AddCrossCuttingServices(); // Cross-cutting services from API layer
```

Replace with:

```csharp
        builder.Services.AddScoped<ISmartsuppWebhookMetrics, SmartsuppWebhookMetrics>();
        builder.Services.AddXccServices(builder.Configuration); // Cross-cutting concerns (audit, telemetry, etc.)
        builder.Services.AddUsersModule(); // Users feature composition root (API-layer adapter for ICurrentUserService)
        builder.Services.AddCrossCuttingServices(); // Cross-cutting services from API layer
```

You will also need a `using` directive for the new module at the top of `Program.cs`. Find the existing using block and add (alphabetically grouped with the other `Anela.Heblo.API.*` usings; the file already uses `using Anela.Heblo.API.MCP;`):

```csharp
using Anela.Heblo.API.Features.Users;
```

- [ ] **Step 3: Remove the inline DI registrations from `AddCrossCuttingServices`**

Open `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`. Find lines 121–131 inside `AddCrossCuttingServices`:

```csharp
    public static IServiceCollection AddCrossCuttingServices(this IServiceCollection services)
    {
        // Register HttpContextAccessor for user service
        services.AddHttpContextAccessor();

        // Register TimeProvider
        services.AddSingleton(TimeProvider.System);

        // Register Current User Service
        services.AddSingleton<ICurrentUserService, CurrentUserService>();

        // Register HttpClient for E2E testing middleware
```

Replace with (removes the `AddHttpContextAccessor()` and the `AddSingleton<ICurrentUserService, ...>` registration; keeps `TimeProvider` and `AddHttpClient`):

```csharp
    public static IServiceCollection AddCrossCuttingServices(this IServiceCollection services)
    {
        // Register TimeProvider
        services.AddSingleton(TimeProvider.System);

        // Register HttpClient for E2E testing middleware
```

- [ ] **Step 4: Remove the now-unused usings from `ServiceCollectionExtensions.cs`**

`ServiceCollectionExtensions.cs` previously imported `Anela.Heblo.API.Features.Users` (after Task 2) and `Anela.Heblo.Domain.Features.Users` (existing) only because of the line you just deleted. Verify by reading the file — if no other reference to either namespace remains, remove both `using` directives.

Open `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`. Lines 9 and 11 (currently `using Anela.Heblo.API.Features.Users;` and `using Anela.Heblo.Domain.Features.Users;`) — delete them.

If `dotnet build` (Step 5) reports either namespace as still needed, restore that single using and proceed.

- [ ] **Step 5: Build the solution**

Run:
```bash
cd backend && dotnet build
```
Expected: `Build succeeded.` with no new warnings or errors. If a `CS0246` or `CS0103` surfaces for `ICurrentUserService` or `CurrentUserService` inside `ServiceCollectionExtensions.cs`, restore the deleted `using` for whichever namespace the compiler asks for.

- [ ] **Step 6: Run `CurrentUserService` tests**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~CurrentUserService" --no-build
```
Expected: 11 tests PASS.

- [ ] **Step 7: Run full backend test suite to confirm no regression in handlers that depend on `ICurrentUserService`**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests --no-build
```
Expected: all tests PASS (baseline count unchanged). Pay attention to handler tests under `Features/Manufacture`, `Features/Purchase`, `Features/Marketing`, `Features/Journal`, `Features/GridLayouts`, and `Features/Logistics` — those handlers all inject `ICurrentUserService` and resolve through the DI container in integration paths.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.API/Features/Users/UsersModule.cs \
        backend/src/Anela.Heblo.API/Program.cs \
        backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs
git commit -m "refactor: consolidate Users-module DI into UsersModule.cs composition root"
```

---

## Task 4: Remove the unused `Microsoft.AspNetCore.Http` import from `UserManagementModule.cs`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/UserManagement/UserManagementModule.cs`

This is the only other `using Microsoft.AspNetCore.Http;` left in the Application project. Removing it is a prerequisite for removing the package reference in Task 5.

- [ ] **Step 1: Delete the unused import**

Open `backend/src/Anela.Heblo.Application/Features/UserManagement/UserManagementModule.cs`. Find line 6:

```csharp
using Microsoft.AspNetCore.Http;
```

Delete this single line. Leave the rest of the file (including the `// Note: HttpContextAccessor must be registered in the API layer` comment on line 36) untouched.

- [ ] **Step 2: Build the solution**

Run:
```bash
cd backend && dotnet build
```
Expected: `Build succeeded.` — the import was dead, so removing it cannot break anything.

- [ ] **Step 3: Confirm there are no other `using Microsoft.AspNetCore.*` imports in the Application project**

Run:
```bash
grep -rn "using Microsoft\.AspNetCore" backend/src/Anela.Heblo.Application
```
Expected: zero results.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/UserManagement/UserManagementModule.cs
git commit -m "chore: remove unused Microsoft.AspNetCore.Http import from UserManagementModule"
```

---

## Task 5: Remove the `Microsoft.AspNetCore.Http.Abstractions` package reference from the Application csproj

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`

This converts the architectural rule into a compile-time gate. Without this, a future commit could re-introduce `using Microsoft.AspNetCore.Http;` in Application without breaking the build.

- [ ] **Step 1: Remove the package reference**

Open `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`. Find line 21:

```xml
    <PackageReference Include="Microsoft.AspNetCore.Http.Abstractions" Version="2.2.0" />
```

Delete this single line. Leave the surrounding `<ItemGroup>` and other `<PackageReference>` entries untouched.

- [ ] **Step 2: Build the solution**

Run:
```bash
cd backend && dotnet build
```
Expected: `Build succeeded.` with no new errors. If the build fails with `CS0246` for any `Microsoft.AspNetCore.Http.*` symbol inside `Anela.Heblo.Application`, that is a hidden reference the grep did not catch — investigate the failing file, decide whether to relocate the offending code (preferred) or restore the package reference (last resort), and only proceed with the build green.

- [ ] **Step 3: Confirm no `Microsoft.AspNetCore` source references remain in the Application project**

Run:
```bash
grep -rn "Microsoft\.AspNetCore" backend/src/Anela.Heblo.Application
```
Expected: zero results. (No `using` directives, no fully-qualified usages, no csproj entries.)

- [ ] **Step 4: Run the full backend test suite**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests --no-build
```
Expected: all tests PASS — no behavioral change.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
git commit -m "refactor: drop Microsoft.AspNetCore.Http.Abstractions from Application csproj"
```

---

## Task 6: Lock the boundary in with an architecture test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`

Per the arch-review's recommended follow-up (and to make NFR-3 a CI gate that survives future refactors), add a test that asserts no type in the `Anela.Heblo.Application` assembly references any namespace starting with `Microsoft.AspNetCore.`.

- [ ] **Step 1: Write the new architecture test**

Open `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`. Find the end of the existing `Logistics_types_should_not_reference_Purchase_owned_namespaces` method (around line 266, the closing `}` before the `private static bool IsForbidden(...)` helper). Add this new `[Fact]` method directly after that closing brace and before the `IsForbidden` helper:

```csharp
    [Fact]
    public void Application_types_should_not_reference_AspNetCore_namespaces()
    {
        // NFR-3 from spec 2026-05-26: the Application layer must remain free of any
        // Microsoft.AspNetCore.* type references. CurrentUserService was relocated to
        // the API project to enforce this. This test prevents regression.
        const string ApplicationNamespacePrefix = "Anela.Heblo.Application";
        const string ForbiddenPrefix = "Microsoft.AspNetCore";

        var assembly = Assembly.Load("Anela.Heblo.Application");
        var applicationTypes = assembly.GetTypes()
            .Where(t => t.Namespace is not null
                && t.Namespace.StartsWith(ApplicationNamespacePrefix, StringComparison.Ordinal))
            .ToList();

        var violations = new List<string>();

        foreach (var applicationType in applicationTypes)
        {
            foreach (var (referencedType, memberDescription) in EnumerateReferencedTypes(applicationType))
            {
                if (referencedType.Namespace is null)
                    continue;

                if (!referencedType.Namespace.Equals(ForbiddenPrefix, StringComparison.Ordinal)
                    && !referencedType.Namespace.StartsWith(ForbiddenPrefix + ".", StringComparison.Ordinal))
                    continue;

                violations.Add($"{applicationType.FullName} -> {referencedType.FullName} (via {memberDescription})");
            }
        }

        violations.Should().BeEmpty(
            "Application layer must not reference Microsoft.AspNetCore.* types. " +
            "Move ASP.NET Core-dependent code to the API or Infrastructure layer and " +
            "expose it through a framework-neutral abstraction in Domain or Application. " +
            "Found:\n  " + string.Join("\n  ", violations));
    }
```

- [ ] **Step 2: Build to make sure the new test compiles**

Run:
```bash
cd backend && dotnet build test/Anela.Heblo.Tests
```
Expected: `Build succeeded.`

- [ ] **Step 3: Run the new test**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~Application_types_should_not_reference_AspNetCore_namespaces" --no-build
```
Expected: PASS. If it fails, the failure message lists every type/member combination that still pulls in `Microsoft.AspNetCore.*` — investigate the offending file, relocate the code or its abstraction, and rerun. Do not work around the test by adding an allowlist; the whole point is to ban this dependency outright.

- [ ] **Step 4: Run the full module-boundary suite to make sure the new test does not break existing rules**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ModuleBoundariesTests" --no-build
```
Expected: all rules PASS, including the new one.

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "test: lock in Application -> Microsoft.AspNetCore.* boundary via architecture test"
```

---

## Task 7: Final validation

**Files:** none modified beyond formatting that `dotnet format` may apply.

- [ ] **Step 1: Run `dotnet format` and verify there are no changes**

Run:
```bash
cd backend && dotnet format --verify-no-changes
```
Expected: exit code 0. If `dotnet format` reports issues, run `dotnet format` (without `--verify-no-changes`) to fix them, then re-run with `--verify-no-changes` to confirm clean.

- [ ] **Step 2: Full backend build**

Run:
```bash
cd backend && dotnet build
```
Expected: `Build succeeded.` with no warnings/errors.

- [ ] **Step 3: Full backend test suite**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests --no-build
```
Expected: all tests PASS — baseline count preserved + the new boundary test added in Task 6.

- [ ] **Step 4: Final invariant grep**

Run:
```bash
grep -rn "Microsoft\.AspNetCore" backend/src/Anela.Heblo.Application
grep -rn "Anela\.Heblo\.Application\.Features\.Users" backend/
```
Expected: both commands return zero matches.

- [ ] **Step 5: Verify file layout matches the plan**

Run:
```bash
ls backend/src/Anela.Heblo.API/Features/Users/
ls backend/src/Anela.Heblo.Application/Features/Users/ 2>&1 || echo "old directory removed: OK"
```
Expected: the API folder contains `CurrentUserService.cs` and `UsersModule.cs`. The Application folder either doesn't exist (preferred) or is empty.

- [ ] **Step 6: Commit any format-only changes (if produced by Step 1)**

If `dotnet format` modified files in earlier steps, capture them now:

```bash
git status
# If files were changed by dotnet format, stage and commit them:
git add -u backend/
git commit -m "chore: apply dotnet format"
```

If nothing changed, skip this step — do not create an empty commit.

---

## Acceptance Criteria Map (Spec → Plan)

| Spec | Where it is satisfied |
|------|-----------------------|
| FR-1 (interface accessible to Application) | Already satisfied — `ICurrentUserService` lives in `Anela.Heblo.Domain.Features.Users`. Verified in Task 1 (baseline) and protected by Task 2 (no namespace renames). |
| FR-2 (relocate implementation) | Task 2 |
| FR-3 (consolidate DI into `UsersModule.cs`) | Task 3 |
| FR-4 (preserve runtime behavior end-to-end) | Task 2 Step 8, Task 3 Step 7, Task 5 Step 4, Task 7 Step 3 — every existing test must pass at each checkpoint. |
| FR-5 (update affected usings + zero remaining old-namespace hits) | Task 2 Steps 3–5 + Step 9; final grep in Task 7 Step 4. |
| NFR-1 (no perf impact) | No code-path changes; lifetimes unchanged; verified by passing test suite. |
| NFR-2 (security — identical identity reads) | Implementation moved verbatim in Task 2 Step 1; tests for claim selection (`Email`/`preferred_username`/`upn`, `sub`/`oid`/`NameIdentifier`) re-run in Task 2 Step 8 and Task 3 Step 6. |
| NFR-3 (Application has zero ASP.NET Core source or package refs) | Source removed in Task 2 + Task 4. Package reference removed in Task 5. Locked in by Task 6 architecture test. Final grep in Task 7 Step 4. |
| NFR-4 (testability preserved) | Test files updated only by `using` directive in Task 2; no production fakes/mocks change. |
| Arch-review amendment 1 (FR-1 already met) | Documented above; Task 1 Step 4 confirms. |
| Arch-review amendment 2 (delete package reference) | Task 5 |
| Arch-review amendment 3 (delete unused `using` in `UserManagementModule.cs:6`) | Task 4 |
| Arch-review amendment 4 (architecture test follow-up) | Task 6 |
