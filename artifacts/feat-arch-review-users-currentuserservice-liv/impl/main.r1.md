All 7 tasks complete. Writing the output artifact:

---

# Implementation: Relocate CurrentUserService Out of Application Layer

## What was implemented

A structural Clean Architecture refactoring that moves the `CurrentUserService` implementation from the Application layer (where it violated boundaries by importing ASP.NET Core types) to the API layer. The Application project's dependency on `Microsoft.AspNetCore.Http.Abstractions` was removed, and the boundary is now enforced at compile time via a new architecture test.

## Files created/modified

- `backend/src/Anela.Heblo.API/Features/Users/CurrentUserService.cs` — moved verbatim from Application; namespace changed to `Anela.Heblo.API.Features.Users`
- `backend/src/Anela.Heblo.API/Features/Users/UsersModule.cs` — new composition root exposing `AddUsersModule()` (registers `AddHttpContextAccessor` + `AddSingleton<ICurrentUserService, CurrentUserService>`)
- `backend/src/Anela.Heblo.API/Program.cs` — added `builder.Services.AddUsersModule()` before `AddCrossCuttingServices()`
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — removed inline `AddHttpContextAccessor()` and `AddSingleton<ICurrentUserService, ...>()` from `AddCrossCuttingServices()`; removed stale using directives
- `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — removed `Microsoft.AspNetCore.Http.Abstractions` package reference
- `backend/src/Anela.Heblo.Application/Features/UserManagement/UserManagementModule.cs` — removed dead `using Microsoft.AspNetCore.Http;`
- `backend/test/Anela.Heblo.Tests/Features/Users/CurrentUserServiceTests.cs` — updated using to `Anela.Heblo.API.Features.Users`
- `backend/test/Anela.Heblo.Tests/Application/Users/CurrentUserServiceIsInRoleTests.cs` — updated using to `Anela.Heblo.API.Features.Users`
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — added `Application_types_should_not_reference_AspNetCore_namespaces` [Fact] as CI gate
- **Deleted:** `backend/src/Anela.Heblo.Application/Features/Users/CurrentUserService.cs`

## Tests

- `CurrentUserServiceTests.cs` — 8 tests covering claim extraction (sub, oid, NameIdentifier, email, preferred_username, upn, anonymous) — all pass
- `CurrentUserServiceIsInRoleTests.cs` — 3 tests covering role membership — all pass
- `ModuleBoundariesTests.cs` — 7 rules total (6 existing + 1 new AspNetCore boundary rule) — all pass
- Full suite: 3981 passed, 3 skipped, 0 failed

## How to verify

```bash
cd backend
dotnet build                                          # 0 errors
dotnet test test/Anela.Heblo.Tests --no-build         # 3981 pass
grep -rn "Microsoft.AspNetCore" src/Anela.Heblo.Application  # 0 results
ls src/Anela.Heblo.API/Features/Users/               # CurrentUserService.cs + UsersModule.cs
```

## Notes

- `ICurrentUserService` and `CurrentUser` already resided in `Anela.Heblo.Domain.Features.Users` — no interface relocation was needed (FR-1 was pre-satisfied).
- Singleton lifetime preserved verbatim per FR-3; the comment in `UsersModule.cs` explains why `IHttpContextAccessor` is safe under a singleton (uses `AsyncLocal<HttpContext>` per-request).
- Commits: `3334f67c` (move), `49651d60` (UsersModule DI), `9dbf136c` (remove unused import), `a400f80d` (remove package ref), `ee9fabd5` (architecture test)

## PR Summary

Moves `CurrentUserService` from the Application layer to `Anela.Heblo.API/Features/Users/` to fix a Clean Architecture boundary violation where the Application project imported `Microsoft.AspNetCore.Http`. Creates a `UsersModule.cs` composition root following the project's existing vertical-slice module pattern. Removes the now-unnecessary `Microsoft.AspNetCore.Http.Abstractions` package reference from the Application csproj, converting the architectural rule into a compile-time gate. Adds a reflection-based architecture test that asserts no Application type references `Microsoft.AspNetCore.*` namespaces, locking the boundary in CI.

### Changes
- `backend/src/Anela.Heblo.API/Features/Users/CurrentUserService.cs` — moved from Application; namespace updated
- `backend/src/Anela.Heblo.API/Features/Users/UsersModule.cs` — new composition root with `AddUsersModule()`
- `backend/src/Anela.Heblo.API/Program.cs` — wires `AddUsersModule()` before `AddCrossCuttingServices()`
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — removed inline user service DI
- `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — dropped `Microsoft.AspNetCore.Http.Abstractions` package ref
- `backend/src/Anela.Heblo.Application/Features/UserManagement/UserManagementModule.cs` — removed dead using
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — new boundary test as CI gate

## Status
DONE