# Implementation: consume-rule-in-open-or-resume-handler

## What was implemented
Replaced the inline three-state deny-list in `OpenOrResumeBoxByCodeHandler` with a call to the
shared `TransportBoxStateRules.OccupiesCode` predicate (already introduced by
`add-transport-box-state-rules`), and corrected the stale comment above the "create new box" branch
to name `TransportBoxStateRules.OccupiesCodePredicate` as the actual source of the ordering
guarantee it depends on (amendment A4). This closes the last of the two enforcement points
identified in issue #3887 — the repository's `GetByCodeAsync`/`IsBoxCodeActiveAsync` and this
handler now both consume the same single definition of "does this state occupy a code."
Behaviour is unchanged for every current `TransportBoxState` value; this is a pure de-duplication.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/OpenOrResumeBoxByCode/OpenOrResumeBoxByCodeHandler.cs` — line 62 now calls `TransportBoxStateRules.OccupiesCode(existing.State)` instead of the inline `!= Closed && != Stocked` check; comment above the create-new-box branch corrected to name `TransportBoxStateRules.OccupiesCodePredicate`.
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/OpenOrResumeBoxByCodeHandlerTests.cs` — added `ReceivedBox`/`ReserveBox`/`QuarantineBox`/`ErrorBox` builders and five new tests: `Handle_BoxBusyInQuarantine_ReturnsDuplicateActiveBoxFound`, `Handle_BoxBusyInError_ReturnsDuplicateActiveBoxFound`, `Handle_BoxBusyInReserve_ReturnsDuplicateActiveBoxFound`, `Handle_BoxBusyInReceived_ReturnsDuplicateActiveBoxFound`, and the A4 cascade test `Handle_QuarantineBoxResolvedOverNewerStockedBox_DoesNotMintThirdBox`. No pre-existing test was modified.

## Tests
`OpenOrResumeBoxByCodeHandlerTests` now covers all non-resumable/code-occupying states
(`InTransit`, `Received`, `Reserve`, `Quarantine`, `Error`) plus the release states
(`Closed`, `Stocked`) and the resume state (`Opened`).

## How to verify
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~OpenOrResumeBoxByCodeHandlerTests"
```
Result: 12/12 passed (7 pre-existing + 5 new), 0 failed.

```bash
dotnet build Anela.Heblo.sln   # from repo root
dotnet format Anela.Heblo.sln --no-restore   # from repo root
```
Build: 0 errors (13 pre-existing warnings unrelated to this change; the `AccessMatrixGen`
MSB3073 warning is a known, documented, harmless pre-existing issue — see
`memory/gotchas/dotnet-build-hangs-nodereuse-accessmatrixgen.md`). Format: no changes needed.

## Notes
- `dotnet format`/`dotnet build` must be run from the repo root (`Anela.Heblo.sln` lives there,
  not under `backend/`) — running from `backend/` fails with `FileNotFoundException`.
- No frontend changes required; `ErrorCodes.TransportBoxDuplicateActiveBoxFound` (1405) is already
  mapped in `frontend/src/i18n.ts`.
- Depended on `add-transport-box-state-rules` (completed) for `TransportBoxStateRules`, and is
  functionally coupled to (but not code-dependent on) `consume-rule-in-transport-box-repository`
  (completed) for the `GetByCodeAsync` ordering the corrected comment describes.

## PR Summary
De-duplicates the box-code occupancy check in `OpenOrResumeBoxByCodeHandler` (the barcode-scan
busy/resume decision) onto the same `TransportBoxStateRules.OccupiesCode` predicate the repository
layer now uses, and fixes a comment that previously asserted an ordering guarantee the code did not
actually provide. Five new tests pin the previously-undocumented behaviour for `Quarantine`,
`Error`, `Reserve`, and `Received` boxes, plus a regression test for the exact duplicate-code
cascade described in the issue.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/OpenOrResumeBoxByCode/OpenOrResumeBoxByCodeHandler.cs` — deny-list de-duplicated onto `TransportBoxStateRules.OccupiesCode`; comment corrected
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/OpenOrResumeBoxByCodeHandlerTests.cs` — added busy-state coverage for Quarantine/Error/Reserve/Received and the A4 cascade test

## Status
DONE
