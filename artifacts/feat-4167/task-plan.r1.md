# Relocate Department and IDepartmentClient Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `Department` and `IDepartmentClient` from `Anela.Heblo.Domain.Features.InvoiceClassification` to `Anela.Heblo.Domain.Features.Analytics`, and update every real consumer's `using` statement, with zero behavior change.

**Architecture:** Two domain files move folder + get a one-line namespace edit; four consumer files (two source, one DI registration, one test) each swap their `using Anela.Heblo.Domain.Features.InvoiceClassification;` for `using Anela.Heblo.Domain.Features.Analytics;` (or the equivalent fully-qualified reference). `DepartmentSyncService.cs` and its test are explicitly untouched — they use unrelated same-named types from `Rem.FlexiBeeSDK` and `Anela.Heblo.Persistence.Analytics.Entities`.

**Tech Stack:** .NET 8 / C#, xUnit, Moq, FluentAssertions.

---

## Task Overview

| # | Task | Files touched |
|---|------|----------------|
| 1 | `relocate-domain-types` | Move `Department.cs`, `IDepartmentClient.cs` into `Features/Analytics/` |
| 2 | `update-flexi-department-client` | `FlexiDepartmentClient.cs` |
| 3 | `update-flexi-department-query-service-and-tests` | `FlexiDepartmentQueryService.cs`, `FlexiDepartmentQueryServiceTests.cs` |
| 4 | `update-di-registration-and-verify` | `FlexiAdapterServiceCollectionExtensions.cs`, full-solution build + test verification |

Execute tasks in order 1 → 4. Each task must build (or, for task 1 alone, leave the solution in a known-broken intermediate state that task 2–4 fix — see note in Task 1) and each task ends with its own commit, per the "frequent commits" convention. Only Task 4 needs a full solution build/test run since the solution will not compile again until every consumer is fixed.

---

### task: relocate-domain-types

**Files:**
- Move: `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Department.cs` → `backend/src/Anela.Heblo.Domain/Features/Analytics/Department.cs`
- Move: `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IDepartmentClient.cs` → `backend/src/Anela.Heblo.Domain/Features/Analytics/IDepartmentClient.cs`

**Context:** These two types currently live in `Features/InvoiceClassification/` but have zero consumers inside that module — `ClassificationRule.Department`/`ClassificationHistory.Department` are plain `string?` fields unrelated to this `Department` class. Their only real consumers are in the Flexi Accounting adapter area, so they belong in `Features/Analytics/` (an existing domain folder, no naming collision there today). This task moves the files and updates only the `namespace` line in each — the class/interface bodies are otherwise untouched.

- [ ] **Step 1: Confirm current state before moving**

Run:
```bash
cd backend
cat src/Anela.Heblo.Domain/Features/InvoiceClassification/Department.cs
cat src/Anela.Heblo.Domain/Features/InvoiceClassification/IDepartmentClient.cs
```
Expected: `Department.cs` shows namespace `Anela.Heblo.Domain.Features.InvoiceClassification;` and a class with `Id`/`Name` string properties. `IDepartmentClient.cs` shows the same namespace and an interface with `GetDepartmentsAsync`/`GetDepartmentByIdAsync`.

- [ ] **Step 2: Move `Department.cs` and update its namespace**

```bash
git mv backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Department.cs backend/src/Anela.Heblo.Domain/Features/Analytics/Department.cs
```

Then edit the moved file's first line from:
```csharp
namespace Anela.Heblo.Domain.Features.InvoiceClassification;
```
to:
```csharp
namespace Anela.Heblo.Domain.Features.Analytics;
```

The full resulting file must read exactly:
```csharp
namespace Anela.Heblo.Domain.Features.Analytics;

public class Department
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
```

- [ ] **Step 3: Move `IDepartmentClient.cs` and update its namespace**

```bash
git mv backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IDepartmentClient.cs backend/src/Anela.Heblo.Domain/Features/Analytics/IDepartmentClient.cs
```

Then edit the moved file's first line the same way. The full resulting file must read exactly:
```csharp
namespace Anela.Heblo.Domain.Features.Analytics;

public interface IDepartmentClient
{
    Task<IEnumerable<Department>> GetDepartmentsAsync(CancellationToken cancellationToken = default);
    Task<Department?> GetDepartmentByIdAsync(string departmentId, CancellationToken cancellationToken = default);
}
```

(`Department` here resolves within the same `Analytics` namespace — no `using` needed inside this file.)

- [ ] **Step 4: Confirm the old files are gone and new files exist**

Run:
```bash
ls backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ | grep -i department
ls backend/src/Anela.Heblo.Domain/Features/Analytics/ | grep -i department
```
Expected: first command prints nothing (no match); second command prints `Department.cs` and `IDepartmentClient.cs`.

- [ ] **Step 5: Commit**

The solution will not build after this step alone (four consumer files still reference the old namespace) — that is expected and gets fixed by Tasks 2–4. Commit anyway to keep history bite-sized; do not run `dotnet build` yet.

```bash
git add backend/src/Anela.Heblo.Domain/Features/Analytics/Department.cs backend/src/Anela.Heblo.Domain/Features/Analytics/IDepartmentClient.cs
git add backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Department.cs backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IDepartmentClient.cs
git commit -m "refactor(domain): move Department and IDepartmentClient to Analytics module"
```

---

### task: update-flexi-department-client

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentClient.cs`

**Context:** This file implements the Domain `IDepartmentClient` interface and returns `Department` instances. It already disambiguates the Domain interface from the FlexiBee SDK's own `IDepartmentClient` (aliased locally) by fully qualifying the base interface in the class declaration. Only the `using` for the plain `Department` type and the fully-qualified base-interface reference need to change.

- [ ] **Step 1: View current file content**

Run:
```bash
cat backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentClient.cs
```
Expected top of file:
```csharp
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Microsoft.Extensions.Caching.Memory;
using IDepartmentClient = Rem.FlexiBeeSDK.Client.Clients.Accounting.Departments.IDepartmentClient;

namespace Anela.Heblo.Adapters.Flexi.Accounting.Departments;

public class FlexiDepartmentClient : Domain.Features.InvoiceClassification.IDepartmentClient
```

- [ ] **Step 2: Update the `using` and the base-interface qualification**

Change:
```csharp
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Microsoft.Extensions.Caching.Memory;
using IDepartmentClient = Rem.FlexiBeeSDK.Client.Clients.Accounting.Departments.IDepartmentClient;

namespace Anela.Heblo.Adapters.Flexi.Accounting.Departments;

public class FlexiDepartmentClient : Domain.Features.InvoiceClassification.IDepartmentClient
```
to:
```csharp
using Anela.Heblo.Domain.Features.Analytics;
using Microsoft.Extensions.Caching.Memory;
using IDepartmentClient = Rem.FlexiBeeSDK.Client.Clients.Accounting.Departments.IDepartmentClient;

namespace Anela.Heblo.Adapters.Flexi.Accounting.Departments;

public class FlexiDepartmentClient : Domain.Features.Analytics.IDepartmentClient
```

Every other line in the file — the constructor, `GetDepartmentsAsync`, `GetDepartmentByIdAsync`, caching logic — is unchanged. Do not touch the `IDepartmentClient` alias line; it must keep pointing at the FlexiBee SDK type so `_client` (the injected constructor parameter) still resolves to the SDK client, not the Domain interface.

- [ ] **Step 3: Verify no other reference to the old namespace remains in this file**

Run:
```bash
grep -n "InvoiceClassification" backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentClient.cs
```
Expected: no output (empty).

- [ ] **Step 4: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentClient.cs
git commit -m "refactor(flexi): point FlexiDepartmentClient at Domain.Features.Analytics"
```

---

### task: update-flexi-department-query-service-and-tests

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentQueryService.cs`
- Modify: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Accounting/Departments/FlexiDepartmentQueryServiceTests.cs`

**Context:** `FlexiDepartmentQueryService` consumes `IDepartmentClient`/`Department` via a plain `using`. Its unit test does the same to construct a `Mock<IDepartmentClient>` and build `List<Department>` fixtures. Both need only their `using` directive swapped — no logic or assertions change.

- [ ] **Step 1: View current `FlexiDepartmentQueryService.cs`**

Run:
```bash
cat backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentQueryService.cs
```
Expected top of file:
```csharp
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Domain.Features.InvoiceClassification;

namespace Anela.Heblo.Adapters.Flexi.Accounting.Departments;
```

- [ ] **Step 2: Update `FlexiDepartmentQueryService.cs`'s `using`**

Change:
```csharp
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Domain.Features.InvoiceClassification;
```
to:
```csharp
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Domain.Features.Analytics;
```
The rest of the file (`FlexiDepartmentQueryService` class body, constructor, `GetDepartmentsAsync` mapping to `DepartmentDto`) is unchanged.

- [ ] **Step 3: View current `FlexiDepartmentQueryServiceTests.cs`**

Run:
```bash
cat backend/test/Anela.Heblo.Adapters.Flexi.Tests/Accounting/Departments/FlexiDepartmentQueryServiceTests.cs
```
Expected top of file:
```csharp
using Anela.Heblo.Adapters.Flexi.Accounting.Departments;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using FluentAssertions;
using Moq;
using Xunit;
```

- [ ] **Step 4: Update `FlexiDepartmentQueryServiceTests.cs`'s `using`**

Change:
```csharp
using Anela.Heblo.Adapters.Flexi.Accounting.Departments;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using FluentAssertions;
using Moq;
using Xunit;
```
to:
```csharp
using Anela.Heblo.Adapters.Flexi.Accounting.Departments;
using Anela.Heblo.Domain.Features.Analytics;
using FluentAssertions;
using Moq;
using Xunit;
```
The rest of the test file — `Mock<IDepartmentClient>` setup, `List<Department>` fixtures, both `[Fact]` methods and their assertions — is unchanged; it now resolves `IDepartmentClient`/`Department` from the new namespace.

- [ ] **Step 5: Verify no remaining reference to the old namespace in either file**

Run:
```bash
grep -n "InvoiceClassification" \
  backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentQueryService.cs \
  backend/test/Anela.Heblo.Adapters.Flexi.Tests/Accounting/Departments/FlexiDepartmentQueryServiceTests.cs
```
Expected: no output (empty).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentQueryService.cs
git add backend/test/Anela.Heblo.Adapters.Flexi.Tests/Accounting/Departments/FlexiDepartmentQueryServiceTests.cs
git commit -m "refactor(flexi): point FlexiDepartmentQueryService and its tests at Domain.Features.Analytics"
```

---

### task: update-di-registration-and-verify

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs`

**Context:** This file registers `IDepartmentClient` → `FlexiDepartmentClient` in DI. Its `using Anela.Heblo.Domain.Features.InvoiceClassification;` (line 30 today) resolves the unqualified `IDepartmentClient` used in the registration call — it must move to `Anela.Heblo.Domain.Features.Analytics`. This is the file the original arch-review issue missed listing as a consumer; without this fix the solution will not compile. `DepartmentSyncService.cs` (registered a few lines below, at `services.AddScoped<IEntitySyncService, DepartmentSyncService>();`) is unrelated — it resolves its own `IDepartmentClient` type via a different `using Rem.FlexiBeeSDK.Client.Clients.Accounting.Departments;` already present in that file, not through this project's `using` list, so it needs no change here.

- [ ] **Step 1: View current relevant lines**

Run:
```bash
grep -n "using Anela.Heblo.Domain.Features.InvoiceClassification;\|AddScoped<IDepartmentClient" backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs
```
Expected:
```
30:using Anela.Heblo.Domain.Features.InvoiceClassification;
90:        services.AddScoped<IDepartmentClient, FlexiDepartmentClient>();
```

- [ ] **Step 2: Update the `using`**

Change line 30 from:
```csharp
using Anela.Heblo.Domain.Features.InvoiceClassification;
```
to:
```csharp
using Anela.Heblo.Domain.Features.Analytics;
```
Keep the line in the same alphabetically-grouped position among the other `using Anela.Heblo.Domain.Features.*;` lines (between `using Anela.Heblo.Domain.Features.Bank;` and `using Anela.Heblo.Domain.Features.Invoices;` alphabetically — `Analytics` sorts before `Bank`, so if the file's existing `using` block is not strictly alphabetized already, simply edit the line in place rather than reordering the block; do not reorder unrelated `using` lines).

Do not change line 90 (`services.AddScoped<IDepartmentClient, FlexiDepartmentClient>();`) or line 91 (`services.AddScoped<IDepartmentQueryService, FlexiDepartmentQueryService>();`) — both still compile correctly once the `using` resolves `IDepartmentClient` to `Anela.Heblo.Domain.Features.Analytics.IDepartmentClient`, matching `FlexiDepartmentClient`'s new base interface from the previous task. Do not touch line 116 (`services.AddScoped<IEntitySyncService, DepartmentSyncService>();`) — unrelated.

- [ ] **Step 3: Confirm no other reference to the old namespace remains anywhere in the backend source or tests**

Run:
```bash
cd backend
grep -rn "Anela\.Heblo\.Domain\.Features\.InvoiceClassification\.Department\b\|Anela\.Heblo\.Domain\.Features\.InvoiceClassification\.IDepartmentClient\b" src test
grep -rln "using Anela.Heblo.Domain.Features.InvoiceClassification;" src test | xargs -r grep -l "IDepartmentClient\|\bDepartment\b"
```
Expected: both commands produce no output. (The second command is a safety net: any file that still imports the old InvoiceClassification namespace *and* references `IDepartmentClient`/`Department` would indicate a missed consumer — there should be none.)

- [ ] **Step 4: Full solution build**

Run:
```bash
cd backend
dotnet build
```
Expected: `Build succeeded.` with 0 errors. (Per repo convention, also run `dotnet format` before considering the change complete — see Validation section of CLAUDE.md.)

- [ ] **Step 5: Run the affected test projects**

Run:
```bash
cd backend
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests --filter "FullyQualifiedName~Departments"
dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~InvoiceClassification"
```
Expected: both commands report all tests passed, 0 failed. The first run covers `FlexiDepartmentQueryServiceTests` (updated) and `DepartmentSyncServiceTests` (untouched — confirms it still compiles and passes unmodified). The second confirms InvoiceClassification's own test suite is unaffected, since no InvoiceClassification file changed.

- [ ] **Step 6: Run the full backend test suite as a final safety net**

Run:
```bash
cd backend
dotnet test
```
Expected: all tests pass, 0 failed.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs
git commit -m "refactor(flexi): update DI registration using for relocated IDepartmentClient"
```

This is the final task — after this commit, `Anela.Heblo.Domain.Features.InvoiceClassification` no longer contains `Department.cs` or `IDepartmentClient.cs`, both live under `Anela.Heblo.Domain.Features.Analytics`, all four real consumers compile against the new namespace, `DepartmentSyncService.cs` is untouched, and the full backend solution builds and tests green.
