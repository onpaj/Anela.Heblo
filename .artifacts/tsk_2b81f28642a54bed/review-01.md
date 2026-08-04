# Review: consolidate `ImportBankStatementResponse` into `BankStatementImportResultDto`

## Verdict: done

## What was checked

- Diff (`git show 31a9bc67`) matches `plan-01.md` / `design-01.md` exactly:
  - `ImportBankStatementRequest` now `IRequest<BankStatementImportResultDto>`.
  - `ImportBankStatementHandler` returns `BankStatementImportResultDto` directly (same four field assignments).
  - `ImportBankStatementResponse.cs` deleted.
  - `BankStatementsController.ImportStatements` reduced to `var response = await _mediator.Send(importRequest); return Ok(response);` — now consistent with the other three Bank actions that pass mediator results straight to `Ok()`.
  - `BankImportJobBase` required no change, as anticipated — it only reads `HasErrors`/`SuccessCount`/`ErrorCount`/`SkippedCount`, all present on `BankStatementImportResultDto`.
  - Test files (`BankStatementImportIntegrationTests.cs`, `BankImportJobBaseTests.cs`) updated mechanically to reference the new type; no behavioral changes.
- `grep -rn "ImportBankStatementResponse"` across the repo — no remaining references anywhere.
- `dotnet build ../Anela.Heblo.sln` — 0 errors, 250 pre-existing warnings, none newly introduced.
- `dotnet test .../Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Bank" --no-build` — **337 passed, 0 failed**.
- `dotnet format ../Anela.Heblo.sln --verify-no-changes --include <changed files>` — exit code 0, no formatting drift.

## Assessment

This fully resolves both problems from the finding: the controller no longer manually maps between two near-identical types, and the duplicate `ImportBankStatementResponse` type is gone. The change is surgical — only the response-type plumbing and directly-dependent test files were touched; no unrelated refactoring. HTTP contract is unchanged (same field names/shapes on the wire), so no OpenAPI/TypeScript regen was needed, consistent with the design doc's claim.

No correctness, architecture, or completeness issues found.
