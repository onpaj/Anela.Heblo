# Code Review: Consume Rule in OpenOrResumeBoxByCode Handler

## Summary

This is a clean de-duplication task that replaces the handler's inline deny-list (`!= Closed && != Stocked`) with a call to the shared `TransportBoxStateRules.OccupiesCode` predicate. The corrected comment now names the predicate as the source of the ordering guarantee it depends on. Comprehensive test coverage for all non-resumable states is added, including a regression test for the exact cascade bug described in the issue. Behaviour is byte-for-byte identical; all tests pass.

## Review Result: PASS

### task: consume-rule-in-open-or-resume-handler
**Status:** PASS

## Docs to Update

No documentation updates needed. This is a pure de-duplication with identical behaviour for all enum values. The error code (1405) is already mapped in the frontend, and no public API or operational changes are introduced.

## Overall Notes

- **Semantic equivalence verified**: The old inline condition `existing.State != TransportBoxState.Closed && existing.State != TransportBoxState.Stocked` is logically equivalent to the new `TransportBoxStateRules.OccupiesCode(existing.State)` which returns `!CodeReleasingStates.Contains(state)` where the releasing set is `{Closed, Stocked}`.

- **Import already present**: The required `using Anela.Heblo.Domain.Features.Logistics.Transport;` is already in the handler (line 4).

- **No inline Closed/Stocked comparisons remain**: Grep confirms no stray `TransportBoxState.Closed` or `TransportBoxState.Stocked` comparisons in the handler. The `== TransportBoxState.Opened` check on line 52 correctly remains (it's the resume branch, not the occupancy rule).

- **Comment correctly updated**: The amended comment at lines 69-70 now names `TransportBoxStateRules.OccupiesCodePredicate` as the source of truth for the ordering guarantee, fixing the false assertion that was present before.

- **Test coverage is comprehensive**: All 5 new tests verify:
  - `Success == false` for all code-occupying states (Quarantine, Error, Reserve, Received)
  - `ErrorCode == TransportBoxDuplicateActiveBoxFound`
  - `Params["state"]` correctly set to the state name
  - `Params["code"] == "B001"`
  - Neither `AddAsync` nor `SaveChangesAsync` called
  
  The cascade test `Handle_QuarantineBoxResolvedOverNewerStockedBox_DoesNotMintThirdBox` specifically pins the bug scenario from issue #3887 where a Quarantine box and a newer Stocked box share a code — the handler now correctly denies the scan instead of minting a third box.

- **Pre-existing tests unchanged**: The diff confirms no modifications to the 7 pre-existing tests, validating the proof that behaviour is identical.

- **Build and format pass**: Implementation summary confirms `dotnet build` and `dotnet format` succeed with no new warnings or changes.
