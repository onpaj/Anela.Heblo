# Implementation: implement-exception-handlers

## What was implemented
Created `ArgumentExceptionHandler` and `ValidationExceptionHandler` following the established `UnauthorizedAccessExceptionHandler` pattern, and registered both in `AddCrossCuttingServices()`.

## Files created/modified
- `backend/src/Anela.Heblo.API/Infrastructure/ExceptionHandling/ArgumentExceptionHandler.cs` — Maps `ArgumentException` → 400 ProblemDetails with `detail: ex.Message`
- `backend/src/Anela.Heblo.API/Infrastructure/ExceptionHandling/ValidationExceptionHandler.cs` — Maps `FluentValidation.ValidationException` → 400 ProblemDetails with `extensions.errors` array
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — Registered both handlers in correct order after `UnauthorizedAccessExceptionHandler`

## Tests
- `backend/test/Anela.Heblo.Tests/Infrastructure/ExceptionHandling/ArgumentExceptionHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Infrastructure/ExceptionHandling/ValidationExceptionHandlerTests.cs`

## How to verify
```
dotnet test --filter "FullyQualifiedName~ExceptionHandling"
```
All 9 ExceptionHandling tests pass.

## Status
DONE
