# Specification: UsersModule.cs for Users Module DI Registration

## Summary
Create a dedicated `UsersModule.cs` for the `Users` feature module to bring it in line with the project's documented module-registration convention. Move the existing `ICurrentUserService → CurrentUserService` binding out of the API-layer composition root (`ServiceCollectionExtensions.AddCrossCuttingServices`) and into the new module, then aggregate it from `ApplicationModule.AddApplicationServices`. This is a structural refactor with no behavior change.

## Background
`docs/architecture/development_guidelines.md` (§ "Module Registration", lines 100–129) requires every feature module to expose its own `{Feature}Module.cs` with an `Add{Feature}Module(this IServiceCollection)` extension. Every feature under `backend/src/Anela.Heblo.Application/Features/` follows this pattern (e.g. `JournalModule`, `GridLayoutsModule`, `UserManagementModule`) — except `Users`, which has only `CurrentUserService.cs` and no module file.

The single binding the module owns — `ICurrentUserService → CurrentUserService` — currently lives in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:130` inside `AddCrossCuttingServices()`. This violates two boundaries:

1. The `Users` module is no longer self-describing — a future developer adding Users-related services has no obvious place to register them.
2. The API layer's composition-root utility owns a binding that belongs to the Application layer module.

Note: the `UserManagement` module (Microsoft Graph integration) is a separate concern from `Users` (current-user identity from `HttpContext`); both can coexist after this refactor.

The Users folder location may change pending resolution of issue #1716 (mentioned in the brief). That refactor is **not** in scope here — this spec targets only the missing `UsersModule.cs` at the current path.

## Functional Requirements

### FR-1: Create `UsersModule.cs`
Add a new file at `backend/src/Anela.Heblo.Application/Features/Users/UsersModule.cs` exposing an `AddUsersModule` extension method on `IServiceCollection` and registering the `ICurrentUserService` binding.

**Acceptance criteria:**
- File exists at `backend/src/Anela.Heblo.Application/Features/Users/UsersModule.cs`.
- Declares a `public static class UsersModule` in namespace `Anela.Heblo.Application.Features.Users`.
- Exposes `public static IServiceCollection AddUsersModule(this IServiceCollection services)`.
- Method body registers `services.AddSingleton<ICurrentUserService, CurrentUserService>();` (lifetime preserved verbatim from the original).
- Method returns `services` for fluent chaining (matches sibling modules — see `JournalModule.cs:18`).
- File matches the structural conventions of `JournalModule.cs` and `GridLayoutsModule.cs` (single static class, no constructor logic, standard DI extension pattern).

### FR-2: Aggregate `AddUsersModule()` from `ApplicationModule`
Invoke the new module's registration from `ApplicationModule.AddApplicationServices` so it participates in the standard Application-layer aggregation.

**Acceptance criteria:**
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs` imports `Anela.Heblo.Application.Features.Users`.
- `services.AddUsersModule();` is called inside `AddApplicationServices` alongside other feature module registrations (placement near `services.AddUserManagement(configuration);` at line 86 is acceptable).
- The call is **not** conditional on configuration — `ICurrentUserService` is unconditionally required.

### FR-3: Remove the inline binding from the API composition root
Delete the inline `services.AddSingleton<ICurrentUserService, CurrentUserService>();` call from `AddCrossCuttingServices()` so the binding lives in exactly one place.

**Acceptance criteria:**
- The line at `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:130` is removed along with the adjacent `// Register Current User Service` comment.
- Unused `using Anela.Heblo.Application.Features.Users;` and `using Anela.Heblo.Domain.Features.Users;` imports in that file are removed **only if** no other reference in the file uses them (verify by grep before deletion).
- `services.AddHttpContextAccessor();` (the dependency `CurrentUserService` consumes) remains in `AddCrossCuttingServices()` — that is an API-layer concern and stays put.

### FR-4: Preserve DI registration order semantics
`CurrentUserService` depends on `IHttpContextAccessor`, which is registered in `AddCrossCuttingServices()` in the API layer. In the current `Program.cs` ordering (lines 74–78), `AddApplicationServices` runs **before** `AddCrossCuttingServices`. Since ASP.NET Core DI resolves dependencies at request time (not registration time), this ordering remains safe — but it must be verified.

**Acceptance criteria:**
- Application starts successfully (`dotnet run`) and `ICurrentUserService` can be resolved from any scope.
- `Program.cs` call order is unchanged (`AddApplicationServices` then `AddCrossCuttingServices`).
- No new explicit registration of `IHttpContextAccessor` is introduced inside `UsersModule` — that registration stays in the API layer (matching the pattern documented in `UserManagementModule.cs:36`: *"Note: HttpContextAccessor must be registered in the API layer"*).

### FR-5: All existing tests pass
`ICurrentUserService` is consumed by ~112 files (handlers, services, tests). The refactor must not change behavior observable by any consumer.

**Acceptance criteria:**
- `dotnet build` succeeds with zero warnings or errors introduced by the change.
- `dotnet test` passes the full backend test suite (no test modifications expected).
- `dotnet format` reports no violations on changed files.

## Non-Functional Requirements

### NFR-1: Behavior parity
No runtime behavior change. `CurrentUserService` continues to be resolved as a singleton, continues to read claims from `IHttpContextAccessor.HttpContext.User`, and continues to be available to every consumer that injects `ICurrentUserService`.

### NFR-2: Convention conformance
The result must satisfy `docs/architecture/development_guidelines.md` § "Module Registration" — i.e. the Users module is self-describing and a future developer adding e.g. `IUserPreferencesService` has an obvious file to register it in.

### NFR-3: Surgical change
Only the three files identified below are modified. No adjacent cleanup, no unrelated refactors, no comment rewording outside the immediate change site.

## Data Model
N/A — no schema, entity, or persistence change.

## API / Interface Design
N/A — no HTTP, MediatR, or contract surface change. `ICurrentUserService` interface in `Anela.Heblo.Domain/Features/Users/ICurrentUserService.cs` is untouched.

## Affected Files

| File | Change |
|------|--------|
| `backend/src/Anela.Heblo.Application/Features/Users/UsersModule.cs` | **NEW** — declares `UsersModule.AddUsersModule()` and registers the singleton binding. |
| `backend/src/Anela.Heblo.Application/ApplicationModule.cs` | Add `using Anela.Heblo.Application.Features.Users;` and `services.AddUsersModule();` call. |
| `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` | Delete `services.AddSingleton<ICurrentUserService, CurrentUserService>();` at line 130 and its comment; prune now-unused `using` directives. |

## Dependencies
- No new NuGet packages.
- No new project references — `Anela.Heblo.Application` already references `Anela.Heblo.Domain` (where `ICurrentUserService` lives).
- Depends on existing `IHttpContextAccessor` registration in `AddCrossCuttingServices()` remaining in place.

## Out of Scope
- **Issue #1716** (relocation of the Users module's implementation directory). This spec targets the current path `backend/src/Anela.Heblo.Application/Features/Users/`. If #1716 is resolved later, `UsersModule.cs` moves with the rest of the folder.
- Refactoring `CurrentUserService` internals (claim resolution logic, role checks).
- Introducing new Users-module services beyond the existing `ICurrentUserService`.
- Changes to `UserManagementModule` (the Microsoft Graph integration is a distinct module).
- Moving `IHttpContextAccessor` registration into the Application layer.
- Promoting `CurrentUserService` to internal-sealed or otherwise tightening its visibility (existing tests reference it directly via the interface, but the class itself remains public for now).

## Open Questions
None.

## Status: COMPLETE