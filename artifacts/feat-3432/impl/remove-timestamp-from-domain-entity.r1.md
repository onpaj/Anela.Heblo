# Implementation: remove-timestamp-from-domain-entity

## What was implemented

Removed `Timestamp` property and its `DateTime.UtcNow` assignment from `ApplicationConfiguration` domain entity. Moved timestamp stamping to `GetConfigurationHandler.Handle()` at response-construction time. Added a tight unit test asserting `Timestamp` is `BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5))`.

## Files created/modified

- `backend/src/Anela.Heblo.Domain/Features/Configuration/ApplicationConfiguration.cs` — removed `Timestamp` property and `Timestamp = DateTime.UtcNow` from constructor
- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs` — changed `Timestamp = appConfig.Timestamp` to `Timestamp = DateTime.UtcNow`
- `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs` — added `Handle_SetsTimestampAtResponseConstructionTime` test

## Tests

- `GetConfigurationHandlerTests.cs` — 5/5 tests pass including the new `Handle_SetsTimestampAtResponseConstructionTime`

## How to verify

```bash
cd /home/user/worktrees/feature-3432-Arch-Review-Configuration-Applicationconfiguration
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetConfigurationHandlerTests"
```

## Notes

The `Anela.Heblo.Xcc` project has a pre-existing build error (`GenerateTargetFrameworkMonikerAttribute`) that affects the full solution build but does not impact the test or the changed projects. This error existed before this change.

## PR Summary

Removed non-deterministic `DateTime.UtcNow` side effect from `ApplicationConfiguration` domain constructor. The `Timestamp` property was a transport concern that didn't belong in the domain layer — it existed solely to be copied into the HTTP response DTO. Moving the assignment to the handler restores SRP and makes the domain entity fully deterministic and testable.

### Changes
- `backend/src/Anela.Heblo.Domain/Features/Configuration/ApplicationConfiguration.cs` — removed `Timestamp` property and constructor assignment
- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs` — stamps `DateTime.UtcNow` directly on `GetConfigurationResponse`
- `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs` — added deterministic Timestamp assertion test

## Status
DONE
