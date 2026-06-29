# Implementation: implement-tests

## What was implemented
Created unit tests for `ArgumentExceptionHandler` and `ValidationExceptionHandler` following the exact pattern from `UnauthorizedAccessExceptionHandlerTests.cs`.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Infrastructure/ExceptionHandling/ArgumentExceptionHandlerTests.cs` — 3 tests: Returns400WithProblemDetails (verifies status 400, title, detail), WhenArgumentNullException_Returns400 (subclass handling), WhenOtherException_ReturnsFalseAndDoesNotWriteBody
- `backend/test/Anela.Heblo.Tests/Infrastructure/ExceptionHandling/ValidationExceptionHandlerTests.cs` — 3 tests: Returns400WithProblemDetails (verifies status 400, title), ExposesErrorsInExtensions (verifies errors array with propertyName+errorMessage), WhenOtherException_ReturnsFalseAndDoesNotWriteBody

## How to verify
```
dotnet test --filter "FullyQualifiedName~ExceptionHandling"
```
All 9 ExceptionHandling tests pass (6 new + 3 existing).

## Status
DONE
