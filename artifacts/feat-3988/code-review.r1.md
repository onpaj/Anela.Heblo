## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- None

## Notes

Reviewed the full feature diff (`backend/src/Anela.Heblo.API/appsettings.json`,
`backend/src/Anela.Heblo.Application/Features/ExpeditionList/PrintPickingListOptions.cs`,
`backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/PrintExpeditionOrder/PrintExpeditionOrderHandler.cs`,
`backend/test/Anela.Heblo.Tests/Features/ExpeditionList/PrintExpeditionOrderHandlerTests.cs`)
against `spec.r1.md`.

- `PrintPickingListOptions.DesiredStateName` is added with default `"Balí se"`,
  preserving current runtime behavior when not overridden (FR-1, NFR-1 satisfied).
- `appsettings.json`'s `ExpeditionList` section now carries an explicit
  `"DesiredStateName": "Balí se"` entry next to `"DesiredStateId": 26` (FR-1
  discoverability requirement satisfied).
- `PrintExpeditionOrderHandler.cs:64` now reads
  `_options.Value.DesiredStateName` instead of the hardcoded literal, mirroring
  the existing `NonPrintableStates`-driven branch immediately below it (FR-2
  satisfied).
- `Handle_NonDefaultDesiredStateId_ChecksConfiguredValueNotHardcoded26` (test
  file lines 106-124) was updated exactly per spec: the options now set
  `DesiredStateId = 99, DesiredStateName = "Custom State"`, and the assertion
  changed from `.Should().Be("Balí se")` to `.Should().Be("Custom State")` —
  the test now proves the name tracks configuration the same way the ID does.
- `Handle_OrderInNonPrintableState_ReturnsInvalidStateError`'s
  `[InlineData(26, "Balí se")]` case (line 27) is unchanged and still passes:
  `CreateHandler()` (line 19-23) uses `Options.Create(new
  PrintPickingListOptions())`, so the default `DesiredStateName` ("Balí se")
  is used for status 26, matching the expected value.
- All other test cases in the file are untouched, consistent with the spec's
  "all other existing tests continue to pass unmodified" requirement.
- No module boundary, contract, or persistence changes; scope matches
  "Out of Scope" — no changes to `NonPrintableStates`, no frontend changes.

No correctness issues found. The change is a minimal, direct literal-to-config
substitution that mirrors an existing pattern already used one branch below it
in the same handler — nothing to flag for reuse, simplification, or
efficiency either.
