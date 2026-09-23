# Code Review: make-errortype-settable-and-mapped

## Summary
Both required edits (settable `ErrorType` auto-property on
`BankStatementImportDto`, and the matching `ForMember` rule on
`BankMappingProfile`) were applied together and match the task context's
exact specified final file content byte-for-byte. Build succeeds with 0
errors. All four required test scopes were run: `BankMappingProfileTests`
(4/4 pass, including the previously-RED settability test), and
`GetBankStatementByIdHandlerTests` (4/4 pass, confirming both existing DTO
consumers derive `ErrorType` identically to before). `dotnet format
--verify-no-changes` reports no diff on the touched files.

## Review Result: PASS

### task: make-errortype-settable-and-mapped
**Status:** PASS

## Docs to Update
(None — this is an internal contract/mapping refactor with no public
behavior, CLI, or documented-process impact. The generated OpenAPI/
TypeScript client's `errorType` field becoming writable is the intended
fix for issue #4279 itself, not a new undocumented surface.)

## Overall Notes
- `BankStatementImportDto.cs` and `BankMappingProfile.cs` match the task
  context's specified final content exactly, including the removal of the
  now-unused `using Anela.Heblo.Domain.Features.Bank;` from the DTO.
- The full `Features.Bank` test scope was also run (Step 6): 119 passed,
  8 failed. All 8 failures are in
  `BankStatementImportRepositoryIntegrationTests` and fail identically
  with `System.ArgumentException: Docker is either not running or
  misconfigured` (a Testcontainers/PostgreSQL fixture requirement) — a
  pre-existing environmental limitation unrelated to this change, not a
  regression. Correctly left unfixed per the task context's explicit
  scope note.
- Deviation noted and accepted: the task context's Step 7 formatting
  command referenced `backend/Anela.Heblo.sln`, which does not exist —
  the solution file is at the repo root. The developer ran the equivalent
  `dotnet format Anela.Heblo.sln ...` from the repo root instead, with
  identical `--include`/`--verify-no-changes` arguments and the same
  outcome (exit 0, no diff). This is a path-correction, not a scope or
  behavior change, and does not affect PASS status.
- Production change was committed on its own commit, with the exact
  commit message specified in the task context's Step 8.
