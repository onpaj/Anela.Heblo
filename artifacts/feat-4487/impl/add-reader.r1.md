# Implementation: add-reader

## What was implemented
Public `IUserDashboardSettingsReader` and internal scoped-ready `UserDashboardSettingsReader`. `GetEffectiveSettingsAsync` loads settings via the repository; null -> projects `BuildDefault`, otherwise `ProjectMissing`. Returns `UserDashboardSettingsDto`. No lock, no `AddAsync`/`UpdateAsync`, no entity mutation.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/IUserDashboardSettingsReader.cs`
- `backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/UserDashboardSettingsReader.cs`
- `backend/test/Anela.Heblo.Tests/Features/Dashboard/Infrastructure/UserDashboardSettingsReaderTests.cs`

## Tests
3 tests: new-user default projection, missing-tile projection (entity not mutated, LastModified preserved), fully provisioned unchanged; each verifies AddAsync/UpdateAsync Never.

## How to verify
`dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~Dashboard.Infrastructure"` (26/26 pass); dotnet format clean.

## Notes
DI registration deferred to rewire-consumers.

## PR Summary
Added read-only dashboard settings reader.

### Changes
- reader interface, implementation and tests

## Status
DONE
