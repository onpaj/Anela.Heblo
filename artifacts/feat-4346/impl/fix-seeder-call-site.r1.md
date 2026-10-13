# Implementation: fix-seeder-call-site

## What was implemented
Changed `RecurringJobSeeder.cs:51` to pass `existingConfig` (the matched single row) instead of `existing` (the full list) to `HasSeededFieldsChanged`, fixing CS1503.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs` - one-token call-site fix

## Tests
Existing `RecurringJobSeederTests`: 8 passed, 0 failed.

## How to verify
`cd backend && dotnet build src/Anela.Heblo.Application` (0 errors); `dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~RecurringJobSeederTests"`.

## Notes
Implemented directly by the orchestrator (single-line change; no subagent needed). dotnet format produced no further diff.

## PR Summary
Fixes the main-branch build break (CS1503) in `RecurringJobSeeder` by passing the matched `existingConfig` to `HasSeededFieldsChanged` rather than the whole list.

### Changes
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs` - call-site argument fix

## Status
DONE
