# Specification: Relocate CurrentUserService Out of Application Layer

## Summary
The `CurrentUserService` implementation currently resides in the Application layer but takes a direct dependency on `IHttpContextAccessor`, a web-framework type from `Microsoft.AspNetCore.Http`. This refactoring moves the implementation to an appropriate outer ring (API or Infrastructure), keeps the `ICurrentUserService` abstraction accessible to Application code, and consolidates the module's DI wiring into a dedicated `UsersModule.cs` to align with the project's Clean Architecture and Vertical Slice conventions.

## Background
The codebase follows Clean Architecture: the Application layer holds use cases and orchestration logic and must not depend on web-host or other infrastructure types. The current placement of `CurrentUserService` in `Anela.Heblo.Application/Features/Users/CurrentUserService.cs` violates that boundary because it imports `Microsoft.AspNetCore.Http` and depends on `IHttpContextAccessor`.

Consequences of the current placement:
- The Application project transitively pulls in ASP.NET Core types, blocking reuse from background workers, console tools, and pure unit-test contexts.
- The DI registration lives in `Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` rather than a per-module composition root, so the Users module lacks a self-contained registration surface and diverges from the pattern already used by other modules.
- The same adapter pattern is documented in `docs/architecture/development_guidelines.md` under *Cross-Module Communication* and is not being followed for this module.

This issue was identified by the daily arch-review routine on 2026-05-25.

## Functional Requirements

### FR-1: Keep `ICurrentUserService` accessible to Application code
The abstraction `ICurrentUserService` must remain in a layer that the Application project can reference without violating Clean Architecture (Domain or Application). The interface defines the contract the Application layer relies on to retrieve the current user.
**Acceptance criteria:**
- `ICurrentUserService` resides in a layer at or below Application (Domain preferred per the brief's suggested fix; Application is acceptable if no Domain dependency exists today).
- All existing Application-layer call sites (e.g., `MediatR` handlers) continue to compile and resolve the dependency unchanged.
- No file in the Application project imports `Microsoft.AspNetCore.Http` after the change.

### FR-2: Relocate the `CurrentUserService` implementation out of the Application layer
The concrete `CurrentUserService` class (which depends on `IHttpContextAccessor`) must move out of `Anela.Heblo.Application` into an outer ring that is permitted to reference ASP.NET Core types.
**Acceptance criteria:**
- The file `backend/src/Anela.Heblo.Application/Features/Users/CurrentUserService.cs` no longer exists.
- The implementation lives in `Anela.Heblo.API/Features/Users/` (preferred per the brief) or, if more consistent with sibling modules, in an `Infrastructure/` subfolder accessible from the composition root.
- Namespace of the relocated class matches its new physical location and the conventions in `docs/architecture/filesystem.md`.
- Behavior (claims read, null-handling, exceptions) is preserved verbatim — no logic changes.

### FR-3: Consolidate Users-module DI wiring into a `UsersModule.cs`
The DI registration for `ICurrentUserService → CurrentUserService` currently inlined in `Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` (around line 130) must move to a dedicated `UsersModule.cs` composition root, matching how other modules are wired.
**Acceptance criteria:**
- A new file (e.g., `Anela.Heblo.API/Features/Users/UsersModule.cs` or the matching infrastructure folder) contains an extension method such as `AddUsersModule(this IServiceCollection services)`.
- The `ICurrentUserService` registration and any `AddHttpContextAccessor()` call required by it are inside that module method.
- `ServiceCollectionExtensions.cs` invokes `services.AddUsersModule()` in place of the inline registration; no other inline Users wiring remains there.
- Service lifetime (Scoped/Transient/Singleton) matches the current registration exactly.

### FR-4: Preserve runtime behavior end-to-end
The refactor is structural only. The values returned by `ICurrentUserService` (user id, email, claims, authenticated flag, etc.) must be identical for the same HTTP request before and after the change.
**Acceptance criteria:**
- All existing unit and integration tests that exercise `ICurrentUserService` (directly or transitively) pass without modification beyond namespace/using updates.
- A manual smoke test of an authenticated endpoint that surfaces the current user (e.g., any handler resolving `ICurrentUserService`) returns the same user identity as before.
- `dotnet build` and `dotnet format` pass cleanly on the backend solution.

### FR-5: Update affected `using` directives and references
All consumers of `CurrentUserService` (production code and tests) must reference the relocated implementation and the (possibly relocated) interface from their new namespaces.
**Acceptance criteria:**
- A repository-wide search for the old namespace of `CurrentUserService` returns no results after the change.
- A repository-wide search for `Microsoft.AspNetCore.Http` inside `Anela.Heblo.Application` returns no results.
- The project compiles with no new warnings introduced by the move.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected. DI resolution path and per-request claim lookup remain unchanged. No new allocations or framework calls introduced.

### NFR-2: Security
- The change must not alter how user identity is read from `HttpContext` (claims, authentication scheme, anonymous fallback).
- No new logging of user identity, tokens, or claims is introduced.
- Existing authorization behavior (anywhere `ICurrentUserService` is consulted) must be byte-identical.

### NFR-3: Architectural Conformance
- After the change, the Application project has zero references — direct or transitive package-level — to `Microsoft.AspNetCore.*` types in source code.
- The Users module follows the same `XxxModule.cs` composition-root pattern as other modules in the repository.
- The placement matches the *Cross-Module Communication* adapter pattern in `docs/architecture/development_guidelines.md`.

### NFR-4: Testability
- The relocated implementation remains substitutable: unit tests in Application can use a fake/stub `ICurrentUserService` without referencing ASP.NET Core types.
- The relocation does not require any test to start an HTTP host that did not previously require one.

## Data Model
No data model changes. This is a structural/architectural refactor of in-process service composition only. No database schema, DTOs, contracts, or persisted entities are touched.

## API / Interface Design
No external API changes. The public method surface of `ICurrentUserService` is unchanged.

Internal composition changes:
- **Interface placement (preferred):** `Anela.Heblo.Domain` (or remain in `Anela.Heblo.Application` if it currently lives there and no Domain-side consumer needs it). The interface itself has no framework dependency.
- **Implementation placement:** `Anela.Heblo.API/Features/Users/CurrentUserService.cs` (preferred per brief) — the API project is already permitted to depend on ASP.NET Core.
- **Composition root:** `Anela.Heblo.API/Features/Users/UsersModule.cs` exposing `IServiceCollection AddUsersModule(this IServiceCollection services)`.
- **API project bootstrap:** `ServiceCollectionExtensions.cs` calls `services.AddUsersModule()` in place of the inline registration; the surrounding ordering of module registrations is preserved.

## Dependencies
- `Microsoft.AspNetCore.Http.IHttpContextAccessor` — remains a dependency of the relocated implementation; no longer leaks into the Application project.
- Existing MediatR handlers and any other consumers that inject `ICurrentUserService` — must continue to resolve through DI unchanged.
- Companion arch-review issue: introduction of `UsersModule.cs` for the Users module's composition root (this spec implements that consolidation as part of FR-3).

## Out of Scope
- Adding new functionality to `ICurrentUserService` (e.g., new claims, new methods).
- Changing authentication or authorization mechanisms.
- Renaming `ICurrentUserService` or its members.
- Refactoring other modules that may have similar boundary violations — those are addressed by their own arch-review issues.
- Introducing new abstractions over `HttpContext` beyond what already exists.
- Database, DTO, or OpenAPI client changes.
- Frontend changes.

## Open Questions
None.

## Status: COMPLETE