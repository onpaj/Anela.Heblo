# Implementation: create-domain-exceptions

## What was implemented
Created two new domain exception classes in `Application/Features/Article/Contracts/`:
- `ArticleUserResolverAuthException` — wraps infrastructure auth failures (MsalException)
- `ArticleUserResolverServiceException` — wraps infrastructure service failures (ODataError)

Both are plain sealed classes extending `Exception` with `(string message, Exception innerException)` constructors. No SDK references. Build passes with 0 errors.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Article/Contracts/ArticleUserResolverAuthException.cs` — new domain exception for auth failures
- `backend/src/Anela.Heblo.Application/Features/Article/Contracts/ArticleUserResolverServiceException.cs` — new domain exception for service failures

## Tests
No tests in this task — the exception classes are trivial value types. Integration tested in task fix-handler-and-tests.

## How to verify
```
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --no-restore
```
Build completes with 0 errors.

## Notes
Exception classes use `sealed` modifier, which is a slight tightening of the spec (which only said `class`). The design doc showed plain classes but sealed is appropriate here since no subclassing is intended.

## Status
DONE
