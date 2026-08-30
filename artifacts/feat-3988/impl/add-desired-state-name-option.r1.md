# Implementation: add-desired-state-name-option

## What was implemented
Added a new `DesiredStateName` string option to `PrintPickingListOptions`, defaulting to `"Balí se"` and placed immediately after `DesiredStateId` so the ID/name pair sits together. Added the corresponding `"DesiredStateName": "Balí se"` key to the `"ExpeditionList"` section of `appsettings.json`, directly after `"DesiredStateId"`, for documentary symmetry. This task only introduces the option — it is not yet consumed by any handler (that is a separate follow-up task, `wire-handler-desired-state-name`, which will use it in `PrintExpeditionOrderHandler.cs`).

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/PrintPickingListOptions.cs` — added `public string DesiredStateName { get; set; } = "Balí se";` immediately after the `DesiredStateId` property.
- `backend/src/Anela.Heblo.API/appsettings.json` — added `"DesiredStateName": "Balí se",` immediately after `"DesiredStateId": 26,` in the `"ExpeditionList"` configuration section (around line 540-541).

## Tests
N/A — config-only change. No behavior is wired to the new option yet, so no test coverage is expected or added in this task.

## How to verify
1. `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — expect `Build succeeded.` with 0 errors (pre-existing warnings unrelated to this change are expected and unchanged).
2. Inspect `PrintPickingListOptions.cs` — confirm `DesiredStateName` property exists with default `"Balí se"`, positioned right after `DesiredStateId`.
3. Inspect `appsettings.json` around line 540 — confirm `"DesiredStateName": "Balí se"` sits right after `"DesiredStateId": 26,` in the `ExpeditionList` section.

## Notes
- No deviations from the specified task. Changes match the exact diffs provided in the task description.
- The `DesiredStateName` key was not added to any Key Vault secret, per the task instructions — it is not a secret and the plain `appsettings.json` value is correct since the C# default already matches.
- Handler wiring is explicitly out of scope for this task and was not touched (`PrintExpeditionOrderHandler.cs` is untouched).
- Build was run for the `Anela.Heblo.Application` project only, as specified; it succeeded with 0 errors and only pre-existing, unrelated warnings (139 warnings, all pre-dating this change — verified none reference `ExpeditionList` or `PrintPickingListOptions`).

## PR Summary
Adds a `DesiredStateName` configuration option to `PrintPickingListOptions`, paired with the existing `DesiredStateId`, defaulting to `"Balí se"`. Mirrors the same key/value in `appsettings.json` for documentary symmetry with the numeric `DesiredStateId`. This is a config-only, additive change with no behavioral impact — the option is not yet consumed anywhere; a follow-up task will wire it into `PrintExpeditionOrderHandler`.

### Changes
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/PrintPickingListOptions.cs` — new `DesiredStateName` property (default `"Balí se"`)
- `backend/src/Anela.Heblo.API/appsettings.json` — new `"DesiredStateName": "Balí se"` entry in the `ExpeditionList` section

## Status
DONE
