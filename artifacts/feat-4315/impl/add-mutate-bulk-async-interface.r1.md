# Implementation: add-mutate-bulk-async-interface

## What was implemented

Added the `MutateBulkAsync` method signature (with XML docs) to
`IUserDashboardSettingsMutator`, following the interface's existing
provisioning → lock → load → mutate → save scaffold documented on
`MutateAsync`. This is the interface-only step in a three-task sequence;
the concrete implementation is a separate follow-up task
(`implement-mutate-bulk-async`), so the build is expected to fail after
this change because `UserDashboardSettingsMutator` does not yet implement
the new member.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/IUserDashboardSettingsMutator.cs` — added `using Anela.Heblo.Application.Features.Dashboard.Contracts;` and the new `MutateBulkAsync(string? userId, IReadOnlyList<UserDashboardTileDto> tiles, CancellationToken cancellationToken)` method with XML doc comments describing upsert-by-id semantics and the always-persist behavior needed to preserve `SaveUserSettingsHandler`'s existing "always persist on save" contract.

## Tests

None — this task only changes an interface declaration; no test file was
specified in the task context, and no runtime behavior exists yet to test.

## How to verify

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: build FAILS with `'UserDashboardSettingsMutator' does not
implement interface member 'IUserDashboardSettingsMutator.MutateBulkAsync(...)'`.
Confirmed — see build output captured during this task. This is the
expected, task-context-documented outcome; the next task
(`implement-mutate-bulk-async`) implements the method body.

## Notes

Followed the task context exactly as written (Steps 1-3): added the
method signature verbatim, ran the build to confirm the expected failure,
and committed only the interface file change.

## PR Summary
Added the `MutateBulkAsync` signature to `IUserDashboardSettingsMutator`, the first of three tasks that introduce a bulk upsert path for dashboard tile settings so `SaveUserSettingsHandler` can stop duplicating `UserDashboardSettingsMutator`'s per-tile mutation scaffold.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/IUserDashboardSettingsMutator.cs` — added `MutateBulkAsync` interface method and its XML documentation

## Status
DONE
