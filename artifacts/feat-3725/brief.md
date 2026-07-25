## Module
UserManagement

## Finding
`IGraphService` declares a `SearchUsersAsync(string query, ...)` method at `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs:14` that has **zero callers** anywhere in the Application, API, or Domain layers.

The method is:
- Declared on the interface (`IGraphService.cs:14`)
- Fully implemented in `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs:192–262` (~70 lines of production Graph API code)
- Stubbed in `MockGraphService.cs:22`
- Covered by its own test file `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceSearchTests.cs` (5 tests)

A search across `Anela.Heblo.Application`, `Anela.Heblo.API`, and `Anela.Heblo.Domain` finds no invocation of `SearchUsersAsync` outside of the interface definition and the adapter implementations themselves.

## Why it matters
This is a YAGNI violation: the method forces every current and future `IGraphService` adapter to implement a search capability that delivers zero feature value. It also bloats the test surface (5 dedicated tests) and keeps ~70 lines of Graph API search code in production — all to support a use-case that does not yet exist. If `IGraphService` ever needs a new implementation (e.g. a test double for a new module), the author must implement `SearchUsersAsync` or inherit a confusing no-op stub.

## Suggested fix
Remove `SearchUsersAsync` from `IGraphService`, its implementation in `GraphService.cs` and `MockGraphService.cs`, and the `GraphServiceSearchTests.cs` file. If user-search is needed in the future, it should be added as a new method at that point, driven by a real use-case.

The minimal change is:
1. Delete `IGraphService.SearchUsersAsync` (line 14 of `IGraphService.cs`)
2. Delete the implementation in `GraphService.cs` (lines 192–262)
3. Delete the stub in `MockGraphService.cs` (lines 22–26)
4. Delete `GraphServiceSearchTests.cs` (or remove the 5 `SearchUsersAsync` test methods)

---
_Filed by daily arch-review routine on 2026-07-22._
