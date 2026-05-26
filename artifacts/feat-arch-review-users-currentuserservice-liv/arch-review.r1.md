# Architecture Review: Relocate `CurrentUserService` Out of Application Layer

## Skip Design: true

Pure backend structural refactor — no UI, no API contracts, no visual components.

## Architectural Fit Assessment

The spec aligns cleanly with the project's documented Clean Architecture and Vertical Slice conventions (`docs/architecture/development_guidelines.md`, `docs/architecture/filesystem.md`). Two things make the work straightforward:

- The contract `ICurrentUserService` and its DTO `CurrentUser` already live in `Anela.Heblo.Domain/Features/Users/` (verified — see `ICurrentUserService.cs:3`, `CurrentUser.cs:3`). FR-1 is therefore satisfied by the current code; no interface move is required.
- The "implementation in the outer ring, registered via per-module composition root" pattern is established: `Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs` already demonstrates an API-layer feature folder, and every Application feature has a `{Feature}Module.cs` (verified across 30+ modules).

Two integration concerns surface from the exploration:

1. **`Anela.Heblo.Application.csproj` declares `Microsoft.AspNetCore.Http.Abstractions` 2.2.0** (line 21). Moving `CurrentUserService` is necessary but not sufficient — the package reference must also be removed for NFR-3 (zero ASP.NET Core in source) to hold. After the move, `Application/Features/UserManagement/UserManagementModule.cs:6` still has `using Microsoft.AspNetCore.Http;` but only for a comment-style hint; that import is dead and must be deleted as part of this work, otherwise removing the package reference breaks the build.
2. The current registration is **`AddSingleton<ICurrentUserService, CurrentUserService>()`** (`ServiceCollectionExtensions.cs:130`). Singleton + `IHttpContextAccessor` works (the accessor uses `AsyncLocal<HttpContext>`), but it is an unusual choice. Spec FR-3 says "match the current lifetime exactly" — we will keep it Singleton and call out the surprise rather than change it.

A naming collision risk exists: there is already an `Anela.Heblo.Application.Features.UserManagement` namespace (Graph-backed user lookup). The new module is `Users` (current-request identity), so the names do not collide — but the proximity is real, and the new module's purpose should be obvious from the file name.

## Proposed Architecture

### Component Overview

```
Anela.Heblo.Domain/Features/Users/        (unchanged — referenced by Application consumers)
├── ICurrentUserService.cs                  contract
├── CurrentUser.cs                          DTO record
└── CurrentUserExtensions.cs

Anela.Heblo.Application/                  (no ASP.NET Core anywhere after this change)
└── Features/**/...Handler.cs               consumers inject ICurrentUserService (unchanged)

Anela.Heblo.API/Features/Users/           (NEW — outer-ring implementation + composition)
├── CurrentUserService.cs                   moved from Application; behavior verbatim
└── UsersModule.cs                          AddUsersModule() extension
                                            └── AddHttpContextAccessor()
                                            └── AddSingleton<ICurrentUserService, CurrentUserService>()

Anela.Heblo.API/Program.cs                (one line added: services.AddUsersModule())
Anela.Heblo.API/Extensions/
 └── ServiceCollectionExtensions.cs        (remove inline registration; AddHttpContextAccessor()
                                            stays only if other API-layer code calls it before
                                            UsersModule registration runs)
```

### Key Design Decisions

#### Decision 1: Implementation lands in `Anela.Heblo.API/Features/Users/`, not in `Application/Features/Users/Infrastructure/`

**Options considered:**
- (A) `Anela.Heblo.API/Features/Users/CurrentUserService.cs`
- (B) `Anela.Heblo.Application/Features/Users/Infrastructure/CurrentUserService.cs`

**Chosen approach:** (A) API project.

**Rationale:** Option (B) keeps `Microsoft.AspNetCore.Http.Abstractions` in the Application csproj, which violates NFR-3 ("Application project has zero references — direct or transitive package-level — to `Microsoft.AspNetCore.*` types in source code"). The whole point of the refactor is to delete that package reference. Option (A) is also the brief's preferred placement and matches existing precedent (`API/Features/ExpeditionList/`). The Application project becomes a pure use-case layer that depends only on Domain and framework-neutral abstractions.

#### Decision 2: `UsersModule.cs` lives in `Anela.Heblo.API/Features/Users/`, not in Application

**Options considered:**
- (A) `Anela.Heblo.API/Features/Users/UsersModule.cs`
- (B) `Anela.Heblo.Application/Features/Users/UsersModule.cs` (matching the other 30 modules)

**Chosen approach:** (A) Same project as the implementation it composes.

**Rationale:** A composition root that references types it doesn't own is a smell. The implementation type (`CurrentUserService`) and its framework dependency (`AddHttpContextAccessor`) both live in the API project; the module that wires them belongs there too. Other modules in `Anela.Heblo.Application` register Application-layer services — the Users feature is the exception because its only registration is an API-layer adapter. Locating `UsersModule.cs` in API makes that boundary visible.

#### Decision 3: Preserve Singleton lifetime; do not change to Scoped

**Options considered:**
- (A) Keep `AddSingleton<ICurrentUserService, CurrentUserService>()`
- (B) Quietly upgrade to Scoped

**Chosen approach:** (A) Verbatim preservation.

**Rationale:** FR-3 mandates exact lifetime preservation. Singleton + `IHttpContextAccessor` is safe because the accessor reads `AsyncLocal<HttpContext>` per call, not constructor-captured state. Changing it would be an undocumented behavior change in a "structural-only" refactor. The risk is captured below; cleanup belongs to a separate change.

#### Decision 4: Remove `Microsoft.AspNetCore.Http.Abstractions` from `Anela.Heblo.Application.csproj` as part of this refactor

**Options considered:**
- (A) Remove the package reference now
- (B) Leave it for a follow-up

**Chosen approach:** (A) Remove immediately.

**Rationale:** NFR-3 names "transitive package-level" references explicitly. If the package stays, the boundary is not actually enforced — a future commit could re-introduce a `using Microsoft.AspNetCore.Http;` in Application without breaking the build. Removing it converts the architectural rule into a compile-time gate. The only other `using Microsoft.AspNetCore.Http;` in the Application project is in `UserManagement/UserManagementModule.cs:6`, where it is unused — delete it as part of this change (small surgical fix, not scope creep).

## Implementation Guidance

### Directory / Module Structure

**Create:**
- `backend/src/Anela.Heblo.API/Features/Users/CurrentUserService.cs` — moved verbatim. Namespace: `Anela.Heblo.API.Features.Users`.
- `backend/src/Anela.Heblo.API/Features/Users/UsersModule.cs` — namespace: `Anela.Heblo.API.Features.Users`.

**Delete:**
- `backend/src/Anela.Heblo.Application/Features/Users/CurrentUserService.cs`
- The (now empty) directory `backend/src/Anela.Heblo.Application/Features/Users/` if no other files land in it.

**Modify:**
- `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — remove `<PackageReference Include="Microsoft.AspNetCore.Http.Abstractions" .../>` (line 21).
- `backend/src/Anela.Heblo.Application/Features/UserManagement/UserManagementModule.cs` — remove the unused `using Microsoft.AspNetCore.Http;` (line 6).
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — remove `using Anela.Heblo.Application.Features.Users;` (line 9), remove the inline `AddHttpContextAccessor()` and `AddSingleton<ICurrentUserService, CurrentUserService>()` (lines 123–130) from `AddCrossCuttingServices`. Both moves go into `UsersModule.AddUsersModule()`.
- `backend/src/Anela.Heblo.API/Program.cs` — add `builder.Services.AddUsersModule();` before `AddCrossCuttingServices()` (so other code that may consume `IHttpContextAccessor` during DI build still finds it; ordering is conservative).
- `backend/test/Anela.Heblo.Tests/Features/Users/CurrentUserServiceTests.cs:2` and `backend/test/Anela.Heblo.Tests/Application/Users/CurrentUserServiceIsInRoleTests.cs:2` — update `using Anela.Heblo.Application.Features.Users;` → `using Anela.Heblo.API.Features.Users;`. No logic changes.

### Interfaces and Contracts

No public contract changes. Specifically:

- **`Anela.Heblo.Domain.Features.Users.ICurrentUserService`** — unchanged, still owns `CurrentUser GetCurrentUser()` and `bool IsInRole(string role)`.
- **`Anela.Heblo.Domain.Features.Users.CurrentUser`** — unchanged record.
- **`AddUsersModule` extension** (new):
  ```csharp
  namespace Anela.Heblo.API.Features.Users;

  public static class UsersModule
  {
      public static IServiceCollection AddUsersModule(this IServiceCollection services)
      {
          services.AddHttpContextAccessor();
          services.AddSingleton<ICurrentUserService, CurrentUserService>();
          return services;
      }
  }
  ```

### Data Flow

Unchanged. Per HTTP request: ASP.NET Core middleware sets `HttpContext` on `IHttpContextAccessor` → any MediatR handler that injects `ICurrentUserService` calls `GetCurrentUser()` → implementation reads `HttpContext.User.Claims` and returns a `CurrentUser` record. The only thing that moves is which assembly hosts the implementation type — the call graph and per-request semantics are identical.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Removing `Microsoft.AspNetCore.Http.Abstractions` breaks a hidden Application-layer use of `Microsoft.AspNetCore.Http.*` not surfaced by grep | Medium | Run `dotnet build` on the whole solution after the package removal — the compiler will flag every remaining symbol. Also run `grep -r "Microsoft.AspNetCore" backend/src/Anela.Heblo.Application` before committing. |
| Singleton lifetime is preserved but is a known smell with `IHttpContextAccessor` | Low | Out of scope per FR-3. Document the choice with a one-line comment on the registration: `// Lifetime preserved from prior registration; HttpContextAccessor uses AsyncLocal so per-request reads remain correct.` File a separate follow-up ticket if cleanup is desired. |
| Tests in `backend/test/Anela.Heblo.Tests/Application/Users/` reside under an `Application/` folder but will reference an API-layer type after the move — confusing structure | Low | Acceptable for this PR (FR-5 covers the using-directive update). Optional follow-up: physically relocate the test files under `backend/test/Anela.Heblo.Tests/Api/Users/` to mirror the production layout. Not required for spec acceptance. |
| Future regressions reintroduce ASP.NET types in Application without a compile error (e.g., via a different transitive package) | Medium | Extend `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` with a new rule that scans every type in `Anela.Heblo.Application` for namespace references starting with `Microsoft.AspNetCore.`. The existing reflection-based pattern there (lines 13–100+) covers this idiom; one new `ModuleBoundaryRule` entry is sufficient. |
| `AddCrossCuttingServices()` runs before `AddUsersModule()` and something inside it needs `IHttpContextAccessor` during the build phase | Low | None observed in the current code. Call `AddUsersModule()` immediately before `AddCrossCuttingServices()` in `Program.cs` (`builder.Services.AddUsersModule(); builder.Services.AddCrossCuttingServices();`) so the order is deterministic. |

## Specification Amendments

The spec is implementable as written, with three small clarifications worth incorporating:

1. **FR-1 (interface placement) is already satisfied.** `ICurrentUserService` and `CurrentUser` already live in `Anela.Heblo.Domain/Features/Users/`. The spec implies the interface "may need to remain" or "be moved to Domain" — neither is needed. Update the FR-1 status to "already met; verify no regression."
2. **Add an explicit task: remove `Microsoft.AspNetCore.Http.Abstractions` from `Anela.Heblo.Application.csproj`.** Without this, NFR-3 ("zero package-level references to Microsoft.AspNetCore.*") is not actually met. Currently FR/NFR text targets source-code imports but the package stays.
3. **Add an explicit task: delete the unused `using Microsoft.AspNetCore.Http;` from `Application/Features/UserManagement/UserManagementModule.cs:6`.** This is the only other ASP.NET Core import in the Application project; without removing it, the package removal in (2) breaks the build.
4. **Add a recommended (not required) follow-up:** extend `ModuleBoundariesTests.cs` with a `Application -> Microsoft.AspNetCore.*` boundary rule to lock in NFR-3 as a CI gate.

## Prerequisites

None. The work is in-process source-only refactoring:

- No database migrations.
- No configuration (`appsettings.*.json`) changes.
- No infrastructure changes (Docker, CI/CD, Azure).
- No API contract changes — the OpenAPI client does not need regeneration.
- No frontend impact.

Validation per `CLAUDE.md`: `dotnet build`, `dotnet format`, and the existing test suite under `backend/test/` (especially `Tests/Features/Users/CurrentUserServiceTests.cs` and `Tests/Application/Users/CurrentUserServiceIsInRoleTests.cs`) cover the refactor's correctness.