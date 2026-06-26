# Implementation: update-handler-tests

## What was implemented
Rewrote `GetEntraAccessUsersHandlerTests` to mock `IEntraAccessUserSource` instead of `IGraphService`. Removed the three old usings (`UserManagement.Contracts`, `UserManagement.Services`, `Domain.Features.Authorization`) and added `Authorization.Contracts`. Updated both tests to use `Mock<IEntraAccessUserSource>`, the new `GetBaseMembersAsync` method, and `EntraAccessUserRecord` positional constructor `(Id, Email, DisplayName)`.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Authorization/GetEntraAccessUsersHandlerTests.cs` — replaced IGraphService mock with IEntraAccessUserSource mock; updated test data from UserDto object initializers to EntraAccessUserRecord positional constructors; renamed empty-list test from `WhenGraphReturnsEmpty` to `WhenSourceReturnsEmpty` and used `It.IsAny<CancellationToken>()` for robustness

## Tests
- Test: GetEntraAccessUsersHandlerTests — 2 tests pass (125 total Authorization tests pass, 1 skipped integration test)

## How to verify
```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Authorization"
```

## Notes
No deviations from the task spec. The AccessMatrixGen tool error seen during full `dotnet test` builds is pre-existing and unrelated to this change.

## Status
DONE
