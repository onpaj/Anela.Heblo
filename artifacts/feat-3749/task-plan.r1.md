# Implementation Plan: Move `InfrastructureConfigurationKeys` out of Domain layer (#3749)

## Overview

`InfrastructureConfigurationKeys` is a `static class` of three `const string` fields (an env-var name and two auth-bypass config-key names) that currently lives in `backend/src/Anela.Heblo.Domain/Shared/InfrastructureConfigurationKeys.cs`, namespace `Anela.Heblo.Domain.Shared`. It holds no domain concept — Domain must not know about infrastructure/deployment concerns. This plan relocates the class, unmodified in body, to `backend/src/Anela.Heblo.Application/Shared/InfrastructureConfigurationKeys.cs`, namespace `Anela.Heblo.Application.Shared` (a flat sibling of the existing `ErrorCodes.cs`, `BaseResponse.cs`, `ListResponse.cs`, `HttpStatusCodeAttribute.cs` in that same folder), and updates the single `using Anela.Heblo.Domain.Shared;` line in each of the 10 confirmed consumer files to `using Anela.Heblo.Application.Shared;`.

This is a pure structural refactor: no constant values, method signatures, `.csproj` files, or runtime behavior change. No new `<ProjectReference>` is required anywhere — `Anela.Heblo.API.csproj` and `Anela.Heblo.Adapters.Microsoft365.csproj` already reference `Anela.Heblo.Application.csproj`, and the class body itself has zero dependency on anything in `Anela.Heblo.Domain`.

A full-tree grep for `InfrastructureConfigurationKeys` across `backend/src` and `backend/test` returns exactly 11 hits: the one definition file plus these 10 consumers (independently re-verified while writing this plan, matching the spec and architecture review):

1. `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Microsoft365AdapterServiceCollectionExtensions.cs`
2. `backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs`
3. `backend/src/Anela.Heblo.API/Infrastructure/Authentication/HangfireAuthenticationMiddleware.cs`
4. `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireDashboardTokenAuthorizationFilter.cs`
5. `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/CatalogDocumentsModule.cs`
6. `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`
7. `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs`
8. `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankModule.cs`
9. `backend/src/Anela.Heblo.Application/Shared/Rag/SharedRagModule.cs`
10. `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs`

Each of these 10 files has exactly one `using Anela.Heblo.Domain.Shared;` line, used solely to resolve `InfrastructureConfigurationKeys` — no other `Domain.Shared` symbol (`Cooling`, `CurrencyCode`, `Result`) is referenced in any of them (independently verified below, file by file), so a straight one-line text substitution is safe everywhere.

**Repo layout note:** the solution file `Anela.Heblo.sln` lives at the repository root (one level above `backend/`), not inside `backend/`. All `dotnet build` / `dotnet test` / `dotnet format` commands in this plan are run from the **repository root**, explicitly naming the solution file — running `dotnet build` from inside `backend/` fails immediately with `MSB1003: Specify a project or solution file` because that directory contains no `.sln` and multiple `.csproj` files.

**Using-directive ordering note:** this repository has no `.editorconfig` and no StyleCop analyzer package configured, so there is no enforced `using`-directive sort order for `dotnet format` to apply. Each edit below replaces the `using Anela.Heblo.Domain.Shared;` line in place, at its existing position in the file — no reordering of the surrounding `using` block is needed or should be done.

## File Structure

- **Move**: `backend/src/Anela.Heblo.Domain/Shared/InfrastructureConfigurationKeys.cs` → `backend/src/Anela.Heblo.Application/Shared/InfrastructureConfigurationKeys.cs` (namespace `Anela.Heblo.Domain.Shared` → `Anela.Heblo.Application.Shared`; class body byte-for-byte unchanged).
- **Edit** (one-line `using` swap only, no other change): the 10 consumer files listed above.
- **No other file is touched.** `Cooling.cs`, `CurrencyCode.cs`, `Result.cs`, and `Domain/Shared/Rag/` remain in `Anela.Heblo.Domain/Shared/` untouched. No `.csproj` file is modified.

Because splitting "move the class" from "update its 10 consumers" would leave an intermediate commit/state where the solution does not build (10 files would reference a symbol that no longer exists in its old namespace), this plan is deliberately a single task: the move and all 10 consumer edits are one atomic, independently testable unit of work — the smallest unit that has a green build/test cycle on both sides of it.

---

### task: relocate-infrastructure-configuration-keys-and-update-consumers

**Goal:** Move `InfrastructureConfigurationKeys` from `Anela.Heblo.Domain.Shared` to `Anela.Heblo.Application.Shared`, update all 10 consumers' `using` directives, and verify with a full solution build plus the existing configuration test suite. This task is self-contained — it is the only task in this plan.

**Working directory for all commands below:** repository root (the directory containing `Anela.Heblo.sln`, i.e. the git worktree root — the parent of `backend/`).

#### Step 1 — Move the class file with its namespace changed

Current content of `backend/src/Anela.Heblo.Domain/Shared/InfrastructureConfigurationKeys.cs`:

```csharp
namespace Anela.Heblo.Domain.Shared;

public static class InfrastructureConfigurationKeys
{
    public const string APP_VERSION = "APP_VERSION";
    public const string USE_MOCK_AUTH = "UseMockAuth";
    public const string BYPASS_JWT_VALIDATION = "BypassJwtValidation";
}
```

Run:

```bash
git mv backend/src/Anela.Heblo.Domain/Shared/InfrastructureConfigurationKeys.cs backend/src/Anela.Heblo.Application/Shared/InfrastructureConfigurationKeys.cs
```

Then edit the moved file's first line so the full file reads exactly:

```csharp
namespace Anela.Heblo.Application.Shared;

public static class InfrastructureConfigurationKeys
{
    public const string APP_VERSION = "APP_VERSION";
    public const string USE_MOCK_AUTH = "UseMockAuth";
    public const string BYPASS_JWT_VALIDATION = "BypassJwtValidation";
}
```

(Only the `namespace` line changes — from `Anela.Heblo.Domain.Shared` to `Anela.Heblo.Application.Shared`. The three `const string` members are untouched: `APP_VERSION == "APP_VERSION"`, `USE_MOCK_AUTH == "UseMockAuth"`, `BYPASS_JWT_VALIDATION == "BypassJwtValidation"`.)

Verify the old path is gone and the new path exists:

```bash
test ! -f backend/src/Anela.Heblo.Domain/Shared/InfrastructureConfigurationKeys.cs && echo "OLD PATH GONE"
test -f backend/src/Anela.Heblo.Application/Shared/InfrastructureConfigurationKeys.cs && echo "NEW PATH EXISTS"
```

Expected output: both `OLD PATH GONE` and `NEW PATH EXISTS` printed.

#### Step 2 — Update consumer 1: Microsoft365 adapter DI registration

File: `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Microsoft365AdapterServiceCollectionExtensions.cs`

Before (lines 1–8):

```csharp
using Anela.Heblo.Adapters.Microsoft365.Photobank;
using Anela.Heblo.Adapters.Microsoft365.UserManagement;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Domain.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
```

After (only line 6 changes):

```csharp
using Anela.Heblo.Adapters.Microsoft365.Photobank;
using Anela.Heblo.Adapters.Microsoft365.UserManagement;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Application.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
```

Do not touch anything else in this file (in particular, leave the two `InfrastructureConfigurationKeys.USE_MOCK_AUTH` / `.BYPASS_JWT_VALIDATION` usages at lines 18–19 exactly as they are — only the `using` line changes).

#### Step 3 — Update consumer 2: API authentication extensions

File: `backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs`

Before (lines 1–9):

```csharp
using Microsoft.Identity.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Anela.Heblo.API.Infrastructure.Authentication;
using Anela.Heblo.API.Infrastructure;
using Anela.Heblo.Domain.Shared;
using Anela.Heblo.Domain.Features.Authorization;
```

After (only line 8 changes):

```csharp
using Microsoft.Identity.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Anela.Heblo.API.Infrastructure.Authentication;
using Anela.Heblo.API.Infrastructure;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
```

Note: `using Anela.Heblo.Domain.Features.Authorization;` stays — it is a different namespace (`Domain.Features.Authorization`, not `Domain.Shared`) and is not affected by this change.

#### Step 4 — Update consumer 3: Hangfire authentication middleware

File: `backend/src/Anela.Heblo.API/Infrastructure/Authentication/HangfireAuthenticationMiddleware.cs`

Before (lines 1–3):

```csharp
using Anela.Heblo.Domain.Shared;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Extensions;
```

After (only line 1 changes):

```csharp
using Anela.Heblo.Application.Shared;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Extensions;
```

#### Step 5 — Update consumer 4: Hangfire dashboard authorization filter

File: `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireDashboardTokenAuthorizationFilter.cs`

Before (lines 1–5):

```csharp
using Hangfire.Dashboard;
using System.Security.Claims;
using Anela.Heblo.API.Infrastructure;
using Anela.Heblo.Domain.Shared;
using Anela.Heblo.Domain.Features.Authorization;
```

After (only line 4 changes):

```csharp
using Hangfire.Dashboard;
using System.Security.Claims;
using Anela.Heblo.API.Infrastructure;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
```

#### Step 6 — Update consumer 5: CatalogDocuments module

File: `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/CatalogDocumentsModule.cs`

Before (lines 1–5):

```csharp
using Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;
using Anela.Heblo.Application.Features.CatalogDocuments.Services;
using Anela.Heblo.Domain.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
```

After (only line 3 changes):

```csharp
using Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;
using Anela.Heblo.Application.Features.CatalogDocuments.Services;
using Anela.Heblo.Application.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
```

Note: leave the pre-existing string-literal call `configuration.GetValue<bool>("UseMockAuth", false)` at line 27 exactly as-is — it is a hardcoded literal, not a reference to `InfrastructureConfigurationKeys`, and is out of scope for this refactor (do not "fix" it to use the constant; that is not part of this task).

#### Step 7 — Update consumer 6: GetConfigurationHandler

File: `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`

Before (lines 1–6):

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Reflection;
using Anela.Heblo.Domain.Features.Configuration;
using Anela.Heblo.Domain.Shared;
using MediatR;
```

After (only line 5 changes):

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Reflection;
using Anela.Heblo.Domain.Features.Configuration;
using Anela.Heblo.Application.Shared;
using MediatR;
```

Leave `using Anela.Heblo.Domain.Features.Configuration;` untouched — different namespace, unaffected. Leave every reference to `InfrastructureConfigurationKeys.USE_MOCK_AUTH` (line 65) and `InfrastructureConfigurationKeys.APP_VERSION` (line 75) exactly as they are.

#### Step 8 — Update consumer 7: MeetingTasks module

File: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs`

Before (lines 1–8):

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Domain.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Persistence.MeetingTasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
```

After (only line 2 changes):

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Persistence.MeetingTasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
```

Same as Step 6: leave the hardcoded `configuration.GetValue<bool>("UseMockAuth", false)` string literal (line 23) untouched — out of scope.

#### Step 9 — Update consumer 8: Photobank module

File: `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankModule.cs`

Before (lines 18–21, showing the two adjacent `using` lines that matter):

```csharp
using Anela.Heblo.Application.Features.Photobank.Validators;
using Anela.Heblo.Domain.Shared;
using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Persistence.Photobank;
```

After (only the `Domain.Shared` line changes):

```csharp
using Anela.Heblo.Application.Features.Photobank.Validators;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Persistence.Photobank;
```

The full `using` block in this file (lines 1–25) has many other `Anela.Heblo.Application.Features.Photobank.*` lines above this pair (`Common.Behaviors`, `Photobank.Configuration`, `Photobank.Infrastructure.Jobs`, `Photobank.Services`, `Photobank.UseCases.*`, `Photobank.Validators`) — none of those are touched. Only the single `using Anela.Heblo.Domain.Shared;` line (immediately after `using Anela.Heblo.Application.Features.Photobank.Validators;` and immediately before `using Anela.Heblo.Domain.Features.Photobank;`) changes to `using Anela.Heblo.Application.Shared;`.

#### Step 10 — Update consumer 9: SharedRagModule

File: `backend/src/Anela.Heblo.Application/Shared/Rag/SharedRagModule.cs`

Before (lines 1–8):

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Shared.Rag.DocumentExtractors;
using Anela.Heblo.Application.Shared.Rag.OneDrive;
using Anela.Heblo.Domain.Features.Rag;
using Anela.Heblo.Domain.Shared;
using Anela.Heblo.Persistence.Rag;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
```

After (only line 5 changes):

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Shared.Rag.DocumentExtractors;
using Anela.Heblo.Application.Shared.Rag.OneDrive;
using Anela.Heblo.Domain.Features.Rag;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Persistence.Rag;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
```

Note this file (namespace `Anela.Heblo.Application.Shared.Rag`) already has two `using Anela.Heblo.Application.Shared.*` lines (`Rag.DocumentExtractors`, `Rag.OneDrive`) — the new `using Anela.Heblo.Application.Shared;` is a distinct, valid, non-duplicate directive (the parent namespace, not a re-statement of the child ones) and does not collide with them.

#### Step 11 — Update consumer 10: GetConfigurationHandlerTests

File: `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs`

Before (lines 1–6):

```csharp
using Anela.Heblo.Application.Features.Configuration;
using Anela.Heblo.Domain.Features.Configuration;
using Anela.Heblo.Domain.Shared;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
```

After (only line 3 changes):

```csharp
using Anela.Heblo.Application.Features.Configuration;
using Anela.Heblo.Domain.Features.Configuration;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
```

Leave every `InfrastructureConfigurationKeys.APP_VERSION` (lines 30, 46, 77, 95) and `InfrastructureConfigurationKeys.USE_MOCK_AUTH` (line 78) reference exactly as-is — only the `using` line changes.

#### Step 12 — Confirm no other reference to the old namespace remains for this symbol

Run from the repository root:

```bash
grep -rn "InfrastructureConfigurationKeys" backend/src backend/test --include="*.cs"
```

Expected output: exactly 11 lines — the `public static class InfrastructureConfigurationKeys` declaration in the new location (`backend/src/Anela.Heblo.Application/Shared/InfrastructureConfigurationKeys.cs`), plus all field-access usages inside the 10 consumer files. None of the 11 lines should mention `Anela.Heblo.Domain/Shared/InfrastructureConfigurationKeys.cs` as a path (it no longer exists).

Also run:

```bash
grep -rn "using Anela.Heblo.Domain.Shared;" backend/src backend/test --include="*.cs"
```

Expected output: **no output at all** (zero matches) — confirming none of the 10 files still imports the old namespace. (If any other file in the repo legitimately still needs `Anela.Heblo.Domain.Shared` for `Cooling`, `CurrencyCode`, or `Result`, it was never in the 10-file list and is untouched by this task — but per the spec/architecture-review's exhaustive grep, no such file exists today.)

#### Step 13 — Build the full solution

Run from the repository root:

```bash
dotnet build Anela.Heblo.sln
```

Expected output: build succeeds with `0 Error(s)` in the summary (warning count, if any, must match the pre-change baseline — this refactor introduces no new warnings). If any error references `InfrastructureConfigurationKeys` or `CS0246`/`CS0234` (type-or-namespace-not-found) in any file outside the 10 listed, stop and re-examine — it means an 11th consumer exists that the grep in Step 12 missed; add the same one-line `using` fix to that file and re-run this build step.

#### Step 14 — Run the full test suite, then specifically confirm the Configuration tests

Run from the repository root:

```bash
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~GetConfigurationHandlerTests"
```

Expected output: all tests in `GetConfigurationHandlerTests` pass (e.g. `Passed! - Failed: 0, Passed: 6, Skipped: 0` — exact count may vary slightly by test-runner version, but `Failed: 0` is the required condition). This confirms FR-4 (no behavioral change): the handler's version-resolution and mock-auth-reporting logic is unaffected by the namespace move.

Then run the full solution test suite to catch any other regression (e.g. an architecture-boundary/reflection test asserting something about `Domain.Shared` contents):

```bash
dotnet test Anela.Heblo.sln
```

Expected output: the same pass/fail counts as the pre-change baseline (no new failures). If a test under `Architecture/` or similar fails referencing `Domain.Shared` or `InfrastructureConfigurationKeys`, investigate whether that test hardcodes the old location and needs its own follow-up (out of scope for this plan to guess at in advance, since the architecture review found no such test today — but Step 14 is where it would surface).

#### Step 15 — Format check (optional but recommended per project conventions)

Run from the repository root:

```bash
dotnet format Anela.Heblo.sln --verify-no-changes
```

If this reports files needing changes, run `dotnet format Anela.Heblo.sln` to apply them, then re-run Step 13's build and Step 14's tests to confirm nothing broke. Given there is no `.editorconfig`/StyleCop `using`-order rule in this repo (see the ordering note at the top of this plan), this step is expected to report no changes; if it does report changes, they should be limited to the files touched in Steps 1–11 (or none at all) — do not let `dotnet format` touch unrelated files as part of this task.

#### Step 16 — Commit

Stage exactly the files this task touched:

```bash
git add backend/src/Anela.Heblo.Domain/Shared/InfrastructureConfigurationKeys.cs \
        backend/src/Anela.Heblo.Application/Shared/InfrastructureConfigurationKeys.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Microsoft365AdapterServiceCollectionExtensions.cs \
        backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs \
        backend/src/Anela.Heblo.API/Infrastructure/Authentication/HangfireAuthenticationMiddleware.cs \
        backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireDashboardTokenAuthorizationFilter.cs \
        backend/src/Anela.Heblo.Application/Features/CatalogDocuments/CatalogDocumentsModule.cs \
        backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs \
        backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs \
        backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankModule.cs \
        backend/src/Anela.Heblo.Application/Shared/Rag/SharedRagModule.cs \
        backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs
```

(`git add` on the old path is harmless/no-op since `git mv` in Step 1 already staged the rename; including it here is just explicit-and-safe.)

```bash
git commit -m "Move InfrastructureConfigurationKeys from Domain.Shared to Application.Shared (#3749)"
```

**Task done when:** Step 13's build shows `0 Error(s)`, Step 14's `GetConfigurationHandlerTests` filter run shows `Failed: 0`, and Step 14's full-suite run shows no new failures versus the pre-change baseline.

---

## Self-Review

- **Spec coverage:** FR-1 (relocate class, byte-for-byte body, namespace change) → Steps 1. FR-2 (update all 10 consumers' `using` directive, no other line touched) → Steps 2–11, verified by Step 12's greps. FR-3 (no `.csproj` changes) → no `.csproj` file appears anywhere in Steps 1–11 or the Step 16 commit list; Step 13's clean build is the acceptance check. FR-4 (no behavioral change) → Step 14 runs `GetConfigurationHandlerTests` plus the full suite.
- **Placeholder scan:** every step shows exact before/after file content or exact shell commands with expected output; no step says "similar to Step N" or "TBD".
- **Consistency:** the namespace `Anela.Heblo.Application.Shared` and file path `backend/src/Anela.Heblo.Application/Shared/InfrastructureConfigurationKeys.cs` are identical across Step 1's move and every subsequent step's `using` line. The list of 10 consumer files in the Overview, the File Structure section, and Steps 2–11 match exactly (same order, same paths).
