# Specification: Type `GetBankStatementListRequest` date filters as `DateTime?`

## Summary
`GetBankStatementListRequest` currently declares its four date filter fields (`StatementDate`, `ImportDate`, `DateFrom`, `DateTo`) as `string?`, forcing the MediatR handler and the FluentValidation validator to each re-implement `DateTime.TryParse`-based parsing. This spec changes those fields — and the corresponding `BankStatementsController` query parameters — to `DateTime?`, so ASP.NET Core's model binder performs parsing and rejection at the HTTP boundary, and removes the now-redundant manual parsing code from the handler and validator. The change also ripples into the auto-generated TypeScript API client and the one frontend hook that calls this endpoint.

## Background
This finding was filed by the daily automated architecture-review routine (2026-07-15) against the Bank module. The routine flagged that `DateTime.TryParse` is invoked in three separate places for the same four fields — the validator's `BeParseableDate` rule, the validator's `DateFromIsNotLaterThanDateTo` cross-field check, and the handler's `ParseDateOrNull` helper — even though the controller already receives these values as raw query-string parameters and ASP.NET Core is fully capable of binding them directly to `DateTime?`. Input parsing belongs at the HTTP boundary, not duplicated across the handler (business logic) and validator (business rule enforcement). Confirmed against the current repository state:

- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListRequest.cs` (lines 10–13): four `string?` date fields.
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListHandler.cs` (lines 29–32, 66–67): `ParseDateOrNull` helper called four times.
- `backend/src/Anela.Heblo.Application/Features/Bank/Validators/GetBankStatementListRequestValidator.cs` (lines 29–53): `BeParseableDate` and `DateFromIsNotLaterThanDateTo`, both re-parsing strings.
- `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs` (lines 78–112): `[FromQuery] string?` parameters passed straight through to the request object.
- `backend/src/Anela.Heblo.Domain/Features/Bank/BankStatementListFilter.cs`: already typed as `DateTime?` — the handler's job today is solely to convert `string?` → `DateTime?` before constructing this filter, i.e. pure boilerplate.

Additional context discovered while grounding this spec (not called out in the brief, but load-bearing for scope):

- The frontend hook `frontend/src/api/hooks/useBankStatements.ts` (`GetBankStatementListRequest` interface, `useBankStatementsList`) currently passes `statementDate`, `importDate`, `dateFrom`, `dateTo` as raw strings straight through to the generated client method `apiClient.bankStatements_GetBankStatements(...)`.
- The generated client (`frontend/src/api/generated/api-client.ts`, line 1627) currently types these four parameters as `string | null | undefined`, matching today's `[FromQuery] string?` controller signature. Once the controller parameters become `DateTime?`, regenerating the OpenAPI client will change these parameter types to `Date | null | undefined` (consistent with how `BankImportRequestDto.dateFrom`/`dateTo` and `analytics_GetBankStatementImportStatistics` are already typed as `Date` elsewhere in the same file). This will break the frontend TypeScript build unless `useBankStatements.ts` is updated to convert its string inputs (which come from `<input type="date">` elements holding `YYYY-MM-DD` strings, per `frontend/src/components/customer/tabs/ImportTab.tsx`) into `Date` objects before calling the client — the same pattern already used a few lines below in `useBankStatementImport`'s `new Date(request.dateFrom)`.
- Existing unit tests in `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementListHandlerTests.cs` construct `GetBankStatementListRequest` with string date literals (e.g. `DateFrom = "2026-01-01"`) and include two tests specifically covering unparseable-string behavior (`Handle_IgnoresUnparseableDateStrings`, `Validate_RejectsUnparseableDateFrom`, `Validate_RejectsUnparseableDateTo`) that become inapplicable once the field type is `DateTime?` (the C# compiler will no longer allow an unparseable string literal to be assigned).

Per `docs/architecture/development_guidelines.md`, `GetBankStatementListRequest` is a DTO (MediatR request) — the "DTOs are classes, never records" rule already holds here (it's a class) and is unaffected by this change, since only property *types* change, not the type's own record/class nature.

## Functional Requirements

### FR-1: Retype `GetBankStatementListRequest` date fields
Change `StatementDate`, `ImportDate`, `DateFrom`, and `DateTo` on `GetBankStatementListRequest` from `string?` to `DateTime?`. No other properties on this class change.

**Acceptance criteria:**
- `GetBankStatementListRequest.cs` declares all four fields as `public DateTime? {Name} { get; set; }`.
- The class remains a plain C# class (not a record), consistent with existing DTO conventions.
- `Id`, `TransferId`, `Account`, `ErrorsOnly`, `Skip`, `Take`, `OrderBy`, `Ascending` are unchanged.

### FR-2: Retype `BankStatementsController` query parameters
Change the `GetBankStatements` action's `statementDate`, `importDate`, `dateFrom`, `dateTo` parameters from `[FromQuery] string? = null` to `[FromQuery] DateTime? = null`, and pass them unchanged into the constructed `GetBankStatementListRequest`.

**Acceptance criteria:**
- The four query parameters in `BankStatementsController.GetBankStatements` are typed `DateTime?`.
- A request with a syntactically invalid date value in any of these four query parameters (e.g. `?dateFrom=not-a-date`) is rejected by ASP.NET Core model binding with an HTTP 400 response *before* the MediatR pipeline (and therefore the validator and handler) executes.
- A request with a valid ISO-8601 date (e.g. `?dateFrom=2026-01-01`) binds correctly to a `DateTime` value with a zero time component.
- The XML doc comments on the action (lines 64–76) are updated only if their wording becomes inaccurate; otherwise left as-is (they already describe these as dates).

### FR-3: Remove manual date parsing from the handler
Remove the `ParseDateOrNull` private helper method and its four call sites from `GetBankStatementListHandler`. Pass `request.StatementDate`, `request.ImportDate`, `request.DateFrom`, `request.DateTo` directly into the `BankStatementListFilter` constructor (which already expects `DateTime?`).

**Acceptance criteria:**
- `GetBankStatementListHandler.cs` no longer contains a `ParseDateOrNull` method.
- `Handle` builds `BankStatementListFilter` using the request's date properties directly, with no intermediate local `DateTime?` variables for this purpose.
- `NormalizeNullableString` and its use for `TransferId`/`Account` trimming is unchanged — this finding is scoped to dates only.
- Existing repository-call behavior (skip/take/orderBy/ascending, filter shape) is unchanged for valid inputs.

### FR-4: Simplify the validator
Remove `BeParseableDate` and rewrite `DateFromIsNotLaterThanDateTo` (and the `RuleFor` wiring around it) to operate on typed `DateTime?` values with no string parsing. The "DateFrom must be a valid date" / "DateTo must be a valid date" rules are removed entirely, since an unparseable date can no longer reach the validator (FR-2 rejects it earlier). The cross-field "DateFrom must not be later than DateTo" rule is retained, simplified to a direct `DateTime` comparison guarded by both values being non-null.

**Acceptance criteria:**
- `GetBankStatementListRequestValidator.cs` contains no `DateTime.TryParse` calls.
- `BeParseableDate` no longer exists.
- A request with `DateFrom` later than `DateTo` (both non-null) still fails validation with a rule attached to `DateFrom`, matching current behavior (`Validate_RejectsDateFromLaterThanDateTo`-equivalent).
- A request with only one of `DateFrom`/`DateTo` set, or neither set, passes this rule (no false positives), matching current `.When(...)` guard semantics.
- `TransferId`/`Account` length rules are unchanged.

### FR-5: Regenerate and adapt the frontend OpenAPI client
Regenerate the TypeScript OpenAPI client (per `docs/development/api-client-generation.md`) so `bankStatements_GetBankStatements` reflects the new `DateTime?` query parameter types. Update `frontend/src/api/hooks/useBankStatements.ts` so `useBankStatementsList` continues to compile and behave correctly: convert the hook's incoming `statementDate` / `importDate` / `dateFrom` / `dateTo` string inputs (still `YYYY-MM-DD` strings sourced from `<input type="date">` in `ImportTab.tsx`) to `Date` objects immediately before calling the generated client method, mirroring the existing `new Date(request.dateFrom)` pattern already used in `useBankStatementImport` in the same file.

**Acceptance criteria:**
- `frontend/src/api/generated/api-client.ts` is regenerated (not hand-edited) and its `bankStatements_GetBankStatements` signature types the four date parameters as `Date | null | undefined`.
- `useBankStatementsList` still accepts `statementDate?: string`, `importDate?: string`, `dateFrom?: string`, `dateTo?: string` on its public `GetBankStatementListRequest` interface (no consumer-facing breaking change to `ImportTab.tsx` or any other caller) and internally converts each to `Date | undefined` before calling the generated client, using the same `?? undefined` handling already present for absent values.
- `npm run build` and `npm run lint` succeed with no new TypeScript errors in `frontend/src/api/hooks/useBankStatements.ts` or `frontend/src/components/customer/tabs/ImportTab.tsx`.
- No behavioral change to `ImportTab.tsx`'s filtering UX: submitting the existing date-range filter still produces equivalent server-side filtering.

### FR-6: Update backend unit tests
Update `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementListHandlerTests.cs` (both the `GetBankStatementListHandlerTests` and `GetBankStatementListRequestValidatorTests` classes) to compile against the new `DateTime?` field types and to reflect the new division of responsibility.

**Acceptance criteria:**
- All `DateFrom = "..."` / `DateTo = "..."` string literals in test `Arrange` sections are changed to `DateTime` literals (e.g. `new DateTime(2026, 1, 1)`).
- `Handle_IgnoresUnparseableDateStrings` is removed (unparseable strings can no longer be assigned to a `DateTime?` property; this scenario is no longer reachable at the handler layer).
- `Validate_RejectsUnparseableDateFrom` and `Validate_RejectsUnparseableDateTo` are removed for the same reason.
- `Validate_RejectsDateFromLaterThanDateTo` and `Validate_AcceptsValidDateRange` are retained, updated to use `DateTime` literals, and continue to pass.
- `Handle_PassesAllFilterFieldsToRepository`, `Handle_OmitsEmptyOrWhitespaceStringFilters`, `Validate_RejectsTransferIdLongerThan100Chars`, `Validate_RejectsAccountLongerThan100Chars`, and `Validate_AcceptsAllNullOptionalFields` continue to pass with no logic changes beyond literal types.
- All tests in the file pass under `dotnet test`.
- (Optional but recommended, not blocking) A new controller-level or integration test asserting that an invalid date query string (e.g. `?dateFrom=not-a-date`) yields an HTTP 400 via model binding may be added to document the new rejection point; if omitted, this is noted as a minor gap rather than a defect.

## Non-Functional Requirements

### NFR-1: Behavioral compatibility
For all currently valid inputs (parseable date strings, or absent date filters), the end-to-end filtering behavior of `GET /api/bank-statements` must be unchanged: the same records are returned for the same effective date values. This is a refactor of *where* parsing happens, not a change to filtering semantics.

### NFR-2: Error response shape change (accepted)
Invalid date values will now be rejected by ASP.NET Core model binding rather than by FluentValidation. The resulting HTTP 400 response body shape changes (standard ASP.NET Core model-binding `ValidationProblemDetails`, keyed by parameter name, e.g. `"dateFrom": ["The value 'not-a-date' is not valid."]`) instead of the current FluentValidation-shaped error (`"DateFrom must be a valid date"`). This is an accepted, intentional consequence of moving parsing to the HTTP boundary, per the brief. No frontend code currently parses or displays the specific FluentValidation error message text for these fields, so no additional frontend changes are required beyond FR-5.

### NFR-3: No persistence or migration impact
This change touches only request/DTO shape, controller signature, handler logic, and validator logic. `BankStatementListFilter`, `IBankStatementImportRepository`, and the EF Core query implementation are already `DateTime?`-typed and require no changes.

## Data Model
No changes to persisted data or domain entities. Only the transport-layer shape of the read-side query request changes:

- `GetBankStatementListRequest.{StatementDate, ImportDate, DateFrom, DateTo}`: `string?` → `DateTime?`.
- `BankStatementListFilter` (domain/query filter, `backend/src/Anela.Heblo.Domain/Features/Bank/BankStatementListFilter.cs`): unchanged, already `DateTime?`.

## API / Interface Design
`GET /api/bank-statements` — no route or response shape change. Query parameter *value contract* changes:

| Parameter | Before | After |
|---|---|---|
| `statementDate` | `string?` (any format `DateTime.TryParse` accepts) | `DateTime?` (ASP.NET Core default model-binding conversion, invariant-culture-friendly formats incl. ISO-8601) |
| `importDate` | `string?` | `DateTime?` |
| `dateFrom` | `string?` | `DateTime?` |
| `dateTo` | `string?` | `DateTime?` |

Invalid values for any of these four parameters now produce an HTTP 400 from the ASP.NET Core model binder rather than from the MediatR validation pipeline. Valid values (including the already-documented ISO date format used throughout the frontend) behave identically to today.

The generated TypeScript client method `apiClient.bankStatements_GetBankStatements(...)` (in `frontend/src/api/generated/api-client.ts`) changes the type of its `statementDate`, `importDate`, `dateFrom`, `dateTo` parameters from `string | null | undefined` to `Date | null | undefined` as a direct consequence of regeneration; see FR-5.

## Dependencies
- ASP.NET Core's built-in model binding for nullable `DateTime` query parameters (no new package).
- OpenAPI/NSwag client generation pipeline (`docs/development/api-client-generation.md`) must be re-run as part of this change so the TypeScript client stays in sync with the C# controller signature.
- No changes required to `BankStatementListFilter`, `IBankStatementImportRepository`, `BankStatementImportRepository`, or `BankMappingProfile` — all already assume `DateTime?`.

## Out of Scope
- Any change to `Id`, `TransferId`, `Account`, `ErrorsOnly`, `Skip`, `Take`, `OrderBy`, `Ascending` on `GetBankStatementListRequest`.
- Any change to `POST /api/bank-statements/import` (`BankImportRequestDto`), which already uses typed `DateTime` fields.
- Any change to `GET /api/bank-statements/accounts` or `GET /api/bank-statements/{id}`.
- Any change to `analytics_GetBankStatementImportStatistics` / `GetBankStatementImportStatisticsRequest`, which already uses `Date`-typed parameters on the frontend side and is a separate use case (`Analytics` feature area, not `GetBankStatementList`).
- Adding new date-format validation rules (e.g. restricting to ISO-8601 only) beyond what ASP.NET Core's default model binder already enforces — the brief cites this as a *future* concern this refactor makes easier, not something to implement now.
- Any UI/UX change to `ImportTab.tsx` beyond the minimal adaptation needed to keep it compiling and functionally equivalent against the regenerated client (FR-5).
- E2E test coverage: no existing E2E spec targets `/api/bank-statements`; none is added by this change.

## Open Questions
None.

## Status: COMPLETE
