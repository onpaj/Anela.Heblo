# Implementation: response-error-constructor

## What was implemented
Added the two-constructor shape to `GenerateLeafletResponse` (parameterless success constructor + `(ErrorCodes errorCode, Dictionary<string, string>? details = null)` error constructor calling the matching `BaseResponse` constructor), matching the pattern used by `SubmitLeafletFeedbackResponse`.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletResponse.cs` — added the two constructors; no other changes.

## Tests
None added — this is a pure constructor addition to a DTO with no behavior of its own (verified `BaseResponse`'s matching constructor signature first). It is exercised by the handler test added in the next task (`handler-returns-response`).

## How to verify
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```
Build succeeds with 0 errors. The existing success-path object-initializer usage in `GenerateLeafletHandler.cs` (`new GenerateLeafletResponse { Content = ..., ... }`) still compiles unchanged since the parameterless constructor remains.

Note: `dotnet test`/`dotnet build` at the solution or full-test-project level currently fail repo-wide due to a **pre-existing, unrelated** error (`ConfigurationConstants.APP_VERSION` missing in `GetConfigurationHandlerTests.cs`), confirmed present on `origin/main` before this branch existed. Verification here was scoped to the `Anela.Heblo.Application` project directly, which builds clean.

## Notes
No deviations from the task context. `BaseResponse`'s protected constructor `(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)` matches the snippet exactly.

## Status
DONE_WITH_CONCERNS
