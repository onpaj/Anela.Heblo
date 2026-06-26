# Implementation: create-authorization-contract

## What was implemented
Created the `Contracts/` directory under the Authorization feature module and added the `IEntraAccessUserSource` interface along with the `EntraAccessUserRecord` sealed record as a single file. This contract defines how the Authorization module retrieves Entra (Azure AD) base members.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Authorization/Contracts/IEntraAccessUserSource.cs` — defines `IEntraAccessUserSource` interface with `GetBaseMembersAsync` and the `EntraAccessUserRecord(string Id, string Email, string DisplayName)` sealed record

## Tests
None required for this task.

## How to verify
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj

## Notes
Build completed with 0 errors (139 pre-existing warnings, unrelated to this change). The sealed record is used as internal domain transport; both the interface and the record live in the same namespace so no OpenAPI generator can see the record.

## Status
DONE
