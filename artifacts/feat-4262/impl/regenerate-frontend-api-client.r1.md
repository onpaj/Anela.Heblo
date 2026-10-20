# Implementation: regenerate-frontend-api-client

## What was implemented
Regenerated the auto-generated TypeScript API client (`frontend/src/api/generated/api-client.ts`) from the backend's OpenAPI spec via NSwag, so the `ErrorCodes` union type includes the new `ShipmentValidationFailed` member added in the `add-validation-exception-and-error-code` task (`backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs:390`, value `2910`).

Ran, from repository root:
```
cd backend/src/Anela.Heblo.API && dotnet tool restore   # nswag.consolecore wasn't restored yet
cd ../../..
dotnet restore backend/src/Anela.Heblo.API              # NuGet assets file was missing (NETSDK1004)
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
```

The first `GenerateFrontendClientManual` attempt failed with `System.InvalidOperationException: Unable to retrieve project metadata` due to a transient MSBuild file lock on `Anela.Heblo.Application.deps.json` (contention from leftover MSBuild server nodes in the environment). Re-running the same command succeeded cleanly (`Duration: 00:00:34`, `Frontend API client generation completed.`).

## Files created/modified
- `frontend/src/api/generated/api-client.ts` — regenerated (not hand-edited). The only functional change is the new enum member `ShipmentValidationFailed = "ShipmentValidationFailed",` inserted into the `ErrorCodes` enum, alphabetically-by-insertion-order right after `ShipmentOrderWeightUnavailable` (NSwag preserves backend declaration order, matching where `ErrorCodes.ShipmentValidationFailed` sits in the C# enum). No other lines changed.

## Tests
None added — this is a generated-file regeneration task with no hand-written logic. Verification was via diff inspection and grep, per the task's acceptance criteria (see below).

## How to verify
1. `grep -n "ShipmentValidationFailed" frontend/src/api/generated/api-client.ts` → one match, `ShipmentValidationFailed = "ShipmentValidationFailed",` inside the `export enum ErrorCodes { ... }` block (confirmed at line 15234).
2. `git diff --stat frontend/src/api/generated/api-client.ts` → `1 file changed, 1 insertion(+)` — a minimal, additive diff limited to the new enum member, no other NSwag-determinism churn.
3. `git show --stat HEAD` (commit `8cf9da12b`) shows exactly that one file, one line.

## Notes
- No hand-editing was done to the generated file, per project convention (`docs/development/api-client-generation.md`).
- The diff is exactly the single new enum member expected — no incidental reordering or regeneration noise, so nothing needed investigation per Step 3's "stop and investigate" guard.
- Committed directly on the current branch as instructed by the task (`git add frontend/src/api/generated/api-client.ts && git commit -m "chore(api-client): regenerate for ShipmentValidationFailed error code"`), commit `8cf9da12b`.
- This task's own scope did not require running frontend or backend test suites (no hand-written code changed); the downstream `surface-validation-message-in-frontend` task is the one that will actually consume `ErrorCodes.ShipmentValidationFailed` in TypeScript and needs full FE build/lint validation.

## PR Summary
Regenerated `frontend/src/api/generated/api-client.ts` via NSwag so its `ErrorCodes` TypeScript union now includes `"ShipmentValidationFailed"`, matching the backend enum member added in an earlier task. The diff is a single additive line; no hand-editing was performed.

### Changes
- `frontend/src/api/generated/api-client.ts` — regenerated; adds `ShipmentValidationFailed` to the `ErrorCodes` enum

## Status
DONE
