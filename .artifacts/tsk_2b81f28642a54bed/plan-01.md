## Summary

`BankStatementsController.ImportStatements` manually maps the handler's `ImportBankStatementResponse` onto `BankStatementImportResultDto` field-by-field, even though the two types are structurally identical. This is business-logic-in-controller plus a maintenance hazard (two types to keep in sync). Fix: make `ImportBankStatementHandler` return `BankStatementImportResultDto` directly — matching the existing pattern used by `GetBankStatementByIdHandler` — and delete the now-redundant `ImportBankStatementResponse`.

## Context

Confirmed by reading the code:
- `BankStatementsController.cs:43-60` — `ImportStatements` sends `ImportBankStatementRequest`, gets back `ImportBankStatementResponse`, then hand-builds a `BankStatementImportResultDto` with identical fields (`Statements`, `SuccessCount`, `ErrorCount`, `SkippedCount`).
- `ImportBankStatementResponse` (`UseCases/ImportBankStatement/ImportBankStatementResponse.cs`) extends `BaseResponse` and adds `HasErrors => ErrorCount > 0`.
- `BankStatementImportResultDto` (`Contracts/BankStatementImportResultDto.cs`) is a plain class with the same four fields plus `TotalCount => Statements.Count` and its own `HasErrors => ErrorCount > 0`. It does **not** extend `BaseResponse`.
- `BankImportJobBase.ExecuteAsync` (`Infrastructure/Jobs/BankImportJobBase.cs:69-83`) consumes the handler's response via `response.HasErrors`, `response.SuccessCount`, `response.ErrorCount`, `response.SkippedCount` — all of which exist on `BankStatementImportResultDto`, so the job needs no changes.
- Other three `Bank` controller actions (`GetAccounts`, `GetBankStatements`, `GetBankStatement`) all pass the handler's response/DTO straight to `Ok(...)` — this action is the sole outlier.

Test surface that references the type being deleted (found via grep, not yet fixed):
- `backend/test/Anela.Heblo.Tests/Features/Bank/Infrastructure/Jobs/BankImportJobBaseTests.cs` — constructs `new ImportBankStatementResponse()` and casts mediator callbacks as `IRequest<ImportBankStatementResponse>` (lines 31, 56-57, 76-77, 94-95, 112, 127).
- `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportIntegrationTests.cs` — deserializes the HTTP response body via `JsonSerializer.Deserialize<ImportBankStatementResponse>(...)` in 4 places (lines 66, 119, 180, 223).
- `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs` — uses `var response = await _handler.Handle(...)` throughout (no explicit type reference), so it compiles unchanged against the new return type; only the mocked `IRequestHandler<...>` under test is unaffected since it's constructed directly, not through DI/mediator.

No frontend impact: the OpenAPI contract shape (`ActionResult<BankStatementImportResultDto>`) on the controller action is unchanged, so the generated TS client and `BankStatementImportChart.tsx` / `useBankStatements.ts` consumers are unaffected.

## Functional requirements

**FR-1: `ImportBankStatementHandler` returns `BankStatementImportResultDto` directly.**
- `ImportBankStatementRequest` implements `IRequest<BankStatementImportResultDto>` instead of `IRequest<ImportBankStatementResponse>`.
- `ImportBankStatementHandler` implements `IRequestHandler<ImportBankStatementRequest, BankStatementImportResultDto>`; `Handle` returns `new BankStatementImportResultDto { Statements = imports, SuccessCount = successCount, ErrorCount = errorCount, SkippedCount = skippedCount }` (same field assignments as today, just the target type changes).
- Acceptance: `dotnet build` succeeds; `ImportBankStatementHandlerTests` pass unchanged (they use `var response = ...` and only touch the four shared fields plus `HasErrors`, all present on the DTO).

**FR-2: `ImportBankStatementResponse.cs` is deleted.**
- The class becomes unused after FR-1 and FR-3.
- Acceptance: no remaining references to `ImportBankStatementResponse` in `backend/src` or `backend/test` (verified by grep after the change); solution builds.

**FR-3: `BankStatementsController.ImportStatements` no longer constructs a DTO manually.**
- Replace the current 60-line body's manual mapping with `var response = await _mediator.Send(importRequest); return Ok(response);`.
- Acceptance: action signature (`ActionResult<BankStatementImportResultDto>`, route, `[FromBody] BankImportRequestDto`) is unchanged so the OpenAPI contract and generated TS client do not regenerate differently; behavior (HTTP 200 with the same JSON shape) is unchanged.

**FR-4: `BankImportJobBase` requires no logic changes.**
- It already only reads `HasErrors`, `SuccessCount`, `ErrorCount`, `SkippedCount` — all present on `BankStatementImportResultDto`.
- Acceptance: `BankImportJobBaseTests` pass after updating their mediator setup to the new type (see FR-5); no changes to `BankImportJobBase.cs` itself.

**FR-5: Update tests that reference the deleted type.**
- `BankImportJobBaseTests.cs`: change `new ImportBankStatementResponse()` → `new BankStatementImportResultDto()` (and the equivalents with `SuccessCount`/`ErrorCount` set), and `IRequest<ImportBankStatementResponse>` → `IRequest<BankStatementImportResultDto>` in the `Callback<...>` generic casts. Add the `Anela.Heblo.Application.Features.Bank.Contracts` using if not already present.
- `BankStatementImportIntegrationTests.cs`: change `JsonSerializer.Deserialize<ImportBankStatementResponse>(...)` → `JsonSerializer.Deserialize<BankStatementImportResultDto>(...)` in all 4 occurrences.
- Acceptance: both test files compile and all their tests pass; no other test files reference `ImportBankStatementResponse` (confirmed via grep — `ImportBankStatementHandlerTests.cs` needs no change).

## Non-functional requirements

- No behavioral change to the HTTP response body — this is a pure internal refactor. Existing consumers (frontend, integration tests once updated) must see byte-identical JSON shape for the same inputs.
- No new abstractions introduced; the fix removes a type rather than adding one, consistent with the "surgical changes" project rule.

## Data model

Before:
- `ImportBankStatementResponse : BaseResponse` (application-layer response) — `Statements`, `SuccessCount`, `ErrorCount`, `SkippedCount`, `HasErrors`.
- `BankStatementImportResultDto` (API contract DTO) — same four fields plus `TotalCount`, `HasErrors`. Built manually in the controller from the former.

After:
- `ImportBankStatementResponse` removed.
- `BankStatementImportResultDto` becomes both the handler's return type and the API contract type — single source of truth, matching `GetBankStatementByIdHandler` → `BankStatementImportDto?` and `GetBankStatementListHandler` → `GetBankStatementListResponse` precedent already in this controller.

## Interfaces

- `POST /api/bank-statements/import` — request/response shape unchanged (`BankImportRequestDto` in, `BankStatementImportResultDto` out as JSON). No OpenAPI contract diff expected other than possibly the schema's own internal `$ref` name if Swagger previously exposed `ImportBankStatementResponse` anywhere (it does not — the action was already typed `ActionResult<BankStatementImportResultDto>`, only the internal mediator plumbing changes).

## Dependencies and scope

**In scope:**
- `ImportBankStatementRequest.cs`, `ImportBankStatementHandler.cs`, `BankStatementsController.cs` (backend/src/Anela.Heblo.API/Controllers).
- Delete `ImportBankStatementResponse.cs`.
- Update `BankImportJobBaseTests.cs` and `BankStatementImportIntegrationTests.cs` to reference `BankStatementImportResultDto` instead of the deleted type.

**Out of scope:**
- `BankImportJobBase.cs` itself (no logic change needed).
- `BankStatementImportResultDto`'s shape (`TotalCount`, `HasErrors`) — kept as-is.
- Any other `Bank` module arch-review findings (tracked separately, e.g. the `BankMappingProfile`, `GetAccounts` controller, and import-job-classes findings already filed).
- Frontend — no client-visible contract change.

## Rough plan

1. Change `ImportBankStatementRequest : IRequest<ImportBankStatementResponse>` → `IRequest<BankStatementImportResultDto>`; add the `Contracts` using if needed.
2. Change `ImportBankStatementHandler`'s class declaration to `IRequestHandler<ImportBankStatementRequest, BankStatementImportResultDto>` and its `Handle` return statement to construct `BankStatementImportResultDto` (same field assignments).
3. Delete `ImportBankStatementResponse.cs`.
4. Simplify `BankStatementsController.ImportStatements` to `var response = await _mediator.Send(importRequest); return Ok(response);`.
5. Update `BankImportJobBaseTests.cs`: replace `new ImportBankStatementResponse(...)` constructions and `IRequest<ImportBankStatementResponse>` casts with `BankStatementImportResultDto`.
6. Update `BankStatementImportIntegrationTests.cs`: replace the 4 `JsonSerializer.Deserialize<ImportBankStatementResponse>` calls with `BankStatementImportResultDto`.
7. Grep the whole repo for `ImportBankStatementResponse` to confirm zero remaining references.
8. Run `dotnet build` and the full `Anela.Heblo.Tests` suite (at minimum the `Bank` feature tests: `ImportBankStatementHandlerTests`, `BankImportJobBaseTests`, `BankStatementImportIntegrationTests`, `BankStatementsControllerTests`) plus `dotnet format`.

## Open questions

- None — the finding's suggested fix is unambiguous and the codebase already establishes the target pattern (`GetBankStatementByIdHandler` returning the DTO directly). Default taken: keep `BankStatementImportResultDto`'s shape exactly as-is (don't fold in `BaseResponse`'s members) since the controller/job only use fields already present on the DTO.
