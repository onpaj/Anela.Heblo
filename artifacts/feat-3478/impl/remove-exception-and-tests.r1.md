# Implementation: remove-exception-and-tests

## What was implemented
Deleted `EmptyRetrievalException.cs` after confirming (via repo-wide grep) it had zero remaining consumers following the previous three tasks (handler, controller, MCP tool).

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/EmptyRetrievalException.cs` — deleted.

## Tests
No new tests — this is a pure deletion of dead code, already covered by the prior tasks' test updates.

## How to verify
```bash
grep -rn "EmptyRetrievalException" backend/ --include="*.cs"   # expect no output
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj   # 0 errors
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj                   # 0 errors
```

## Notes
`dotnet test`/full-solution build still blocked by the pre-existing, unrelated `ConfigurationConstants.APP_VERSION` compile error (confirmed on `origin/main`), unrelated to this deletion.

## Status
DONE
