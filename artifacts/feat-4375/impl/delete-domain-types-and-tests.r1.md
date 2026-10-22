# Implementation: delete-domain-types-and-tests

## What was implemented
Removed the unused `ApplicationConfiguration` and `ConfigurationConstants` domain types and the `ApplicationConfigurationTests` test class.

## Files created/modified
- Deleted `backend/src/Anela.Heblo.Domain/Features/Configuration/ApplicationConfiguration.cs`
- Deleted `backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs`
- Deleted `backend/test/Anela.Heblo.Tests/Features/Configuration/ApplicationConfigurationTests.cs`

## Tests
Grep shows no remaining references. Test project builds with 0 errors; `Features.Configuration` filter: 12 passed, 0 failed. `dotnet format --verify-no-changes` clean.

## How to verify
`dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` and `dotnet test ... --filter "FullyQualifiedName~Features.Configuration"`.

## Notes
None.

## PR Summary
Removed dead `ApplicationConfiguration`/`ConfigurationConstants` domain types and their tests.

### Changes
- Deleted the two domain files and the corresponding test class.

## Status
DONE
