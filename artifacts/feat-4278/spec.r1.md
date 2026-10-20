# Specification: FluentValidation for ImportBankStatementRequest

## Summary
`POST /api/bank-statements/import` currently returns HTTP 500 when the caller supplies an `AccountName` that is not configured in `BankAccounts` settings, because `ImportBankStatementHandler.Handle` throws a raw `ArgumentException` instead of failing validation. This spec adds a FluentValidation validator for `ImportBankStatementRequest`, wires it into the existing MediatR validation pipeline (the same pattern already used for `GetBankStatementListRequest`), and removes the now-redundant manual `ArgumentException` throw, so that both an unknown account name and an invalid date range surface as HTTP 400 with a structured `ProblemDetails` body instead of an unhandled exception.

## Background
The Bank module's list endpoint (`GetBankStatementList`) validates input via `GetBankStatementListRequestValidator`, registered in `BankModule.AddBankModule` alongside a `ValidationBehavior<,>` pipeline behavior. `FluentValidation.ValidationException` thrown by that behavior is caught centrally by `ValidationExceptionHandler` (`Anela.Heblo.API/Infrastructure/ExceptionHandling/ValidationExceptionHandler.cs`) and mapped to a 400 `ProblemDetails` response with a per-field `errors` array.

The import endpoint (`ImportBankStatementRequest`, handled by `ImportBankStatementHandler`) has no such validator. Today, an unknown `AccountName` reaches line 50 of the handler:

```csharp
var accountSetting = _bankSettings.Accounts.SingleOrDefault(a => a.Name == request.AccountName);
if (accountSetting == null)
{
    throw new ArgumentException(
        $"Account name {request.AccountName} not found in {BankAccountSettings.ConfigurationKey} configuration. Available accounts: {availableAccounts}");
}
```

`ArgumentException` is not handled by any registered `IExceptionHandler` for this case, so ASP.NET Core's default problem-details middleware (or an unhandled-exception fallback) returns HTTP 500. Clients (the SPA, or an automated caller with a stale account name) cannot distinguish "you sent bad input" from "the server crashed." This is the same class of defect already fixed for Analytics in issue #4229, and the fix here follows the identical mechanism.

Additionally, unlike `GetBankStatementListRequestValidator` (which validates `DateFrom <= DateTo` when both are present), `ImportBankStatementRequest` has no server-side check that `DateFrom` is not later than `DateTo`. Today an inverted range is passed straight through to `IBankClient.GetStatementsAsync`, whose behavior for an inverted range is client-specific and not currently guaranteed to fail cleanly.

## Functional Requirements

### FR-1: Validate `AccountName` against configured bank accounts
`ImportBankStatementRequest.AccountName` must be validated to be non-empty and to match (ordinal, case-sensitive — matching the existing `SingleOrDefault(a => a.Name == request.AccountName)` comparison in the handler) one of the `Name` values in `BankAccountSettings.Accounts` (bound from configuration section `BankAccounts`).

**Acceptance criteria:**
- Given `AccountName` is null or empty, validation fails with a message identifying `AccountName` as required.
- Given `AccountName` does not match any configured account's `Name`, validation fails with a message that includes the supplied name (mirroring the existing handler's `Account name {name} not found ...` wording is acceptable but not required verbatim).
- Given `AccountName` matches a configured account's `Name` exactly, validation passes for this rule.
- The validation failure is surfaced as HTTP 400 with a `ProblemDetails` body (via the existing `ValidationExceptionHandler`), not HTTP 500.

### FR-2: Validate `DateFrom` is not later than `DateTo`
`ImportBankStatementRequest` must be validated so that `DateFrom.Date <= DateTo.Date`.

**Acceptance criteria:**
- Given `DateFrom.Date > DateTo.Date`, validation fails with a message "DateFrom must not be later than DateTo" (matching the wording used by `GetBankStatementListRequestValidator`).
- Given `DateFrom.Date <= DateTo.Date` (including equal dates), validation passes for this rule.
- Unlike the list endpoint's validator (where both dates are optional `DateTime?`), `DateFrom` and `DateTo` on `ImportBankStatementRequest` are non-nullable `DateTime`, so this rule is unconditional (no `.When(...)` guard is needed).

### FR-3: Register the validator in the MediatR pipeline
`ImportBankStatementRequestValidator` must be registered in `BankModule.AddBankModule` as `IValidator<ImportBankStatementRequest>`, and a `ValidationBehavior<ImportBankStatementRequest, BankStatementImportResultDto>` pipeline behavior must be registered, following the exact pattern already used for `GetBankStatementListRequestValidator` / `GetBankStatementListResponse` in the same method.

**Acceptance criteria:**
- `services.AddScoped<IValidator<ImportBankStatementRequest>, ImportBankStatementRequestValidator>();` is present in `BankModule.AddBankModule`.
- `services.AddScoped<IPipelineBehavior<ImportBankStatementRequest, BankStatementImportResultDto>, ValidationBehavior<ImportBankStatementRequest, BankStatementImportResultDto>>();` is present in `BankModule.AddBankModule`.
- A request that fails validation never reaches `ImportBankStatementHandler.Handle` (the pipeline behavior short-circuits by throwing `FluentValidation.ValidationException` before `next()` is invoked).

### FR-4: Remove the redundant manual `ArgumentException` in the handler
Once the validator guarantees `AccountName` refers to a configured account, `ImportBankStatementHandler.Handle` no longer needs to defensively re-check and throw. The `if (accountSetting == null) { throw new ArgumentException(...); }` block (lines 50–61) is removed; `_bankSettings.Accounts.SingleOrDefault(...)` is kept (or changed to a non-null lookup) since the account is now guaranteed to exist by validation.

**Acceptance criteria:**
- The handler no longer contains a manual `ArgumentException` throw for an unknown account name.
- Existing handler tests that exercised the old "unknown account throws ArgumentException" behavior are updated: that responsibility moves to the new validator's unit tests, and the handler's own tests only exercise the "account is present" happy/error paths (validation is assumed to have already passed, consistent with how `GetBankStatementListHandlerTests` does not re-test list validation rules that `GetBankStatementListRequestValidatorTests` already covers).
- Since the account is now guaranteed present, the handler may use `.Single(...)` in place of `.SingleOrDefault(...)` plus a null check — this is a discretionary implementation choice for the developer, not a hard requirement; either is acceptable as long as the manual `ArgumentException` throw is gone.

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance impact expected. Validation runs once per request against an in-memory `List<BankAccountConfiguration>` (typically fewer than 10 entries) already held in `IOptions<BankAccountSettings>`. No additional I/O.

### NFR-2: Security
No new security surface. The set of valid account names is server-side configuration, not attacker-controlled; validation only prevents an already-authenticated, already-authorized caller (the endpoint is `[FeatureAuthorize(Feature.Customer_BankStatements)]`) from crashing the request pipeline with malformed input. The validator must not leak anything beyond what the current error message already exposes (the current `ArgumentException` message already includes the list of available account names in the exception message logged server-side and previously returned in the 500 response body in non-Production environments; the new validator error message is expected to be equivalent or less verbose — do not regress by exposing MORE configuration detail to the client than today, e.g. avoid unconditionally enumerating all configured account names in a message a client can see, unless it already does so intentionally elsewhere. Reusing the existing message text as-is is acceptable since it is not a new disclosure).

## Data Model
No changes to persisted data model. `BankAccountSettings` / `BankAccountConfiguration` (configuration-bound POCOs) are read-only inputs to the new validator; no new entities.

## API / Interface Design

**Endpoint:** `POST /api/bank-statements/import` (`BankStatementsController.ImportStatements`, unchanged signature — takes `BankImportRequestDto`, maps to `ImportBankStatementRequest`).

**Behavior change only** (no route/DTO shape change):

| Input | Before | After |
|---|---|---|
| Unknown `AccountName` | HTTP 500, unhandled `ArgumentException` | HTTP 400 `ProblemDetails` with `errors: [{ propertyName: "AccountName", errorMessage: "..." }]` |
| Empty `AccountName` | HTTP 500 (`SingleOrDefault` returns null → same `ArgumentException` path) | HTTP 400 `ProblemDetails`, `AccountName` required |
| `DateFrom > DateTo` | Passed through to bank client, undefined behavior downstream | HTTP 400 `ProblemDetails`, `errors: [{ propertyName: "", errorMessage: "DateFrom must not be later than DateTo" }]` (property-less rule, consistent with `GetBankStatementListRequestValidator`'s equivalent `RuleFor(x => x.DateFrom).Must((req, dateFrom) => ...)`) |
| Valid known `AccountName`, `DateFrom <= DateTo` | Proceeds to import | Unchanged — proceeds to import |

**New type:** `ImportBankStatementRequestValidator : AbstractValidator<ImportBankStatementRequest>` in `backend/src/Anela.Heblo.Application/Features/Bank/Validators/ImportBankStatementRequestValidator.cs`, taking `IOptions<BankAccountSettings>` as a constructor dependency (same DI shape already used elsewhere in this module for settings-dependent validation, e.g. via `IOptions<T>`).

## Dependencies
- `FluentValidation` (already a project dependency, already used in this module).
- `Anela.Heblo.Application.Common.Behaviors.ValidationBehavior<,>` (existing, reused as-is).
- `Anela.Heblo.API.Infrastructure.ExceptionHandling.ValidationExceptionHandler` (existing, reused as-is — already registered as a global `IExceptionHandler` and already correctly maps `FluentValidation.ValidationException` → 400; no change needed there).
- `Microsoft.Extensions.Options.IOptions<BankAccountSettings>` for reading configured account names inside the validator.

## Out of Scope
- Changing the `BankImportRequestDto` / `ImportBankStatementRequest` shape (property names/types stay the same).
- Adding client-side (React/TS) validation or UI messaging for these new 400 responses — this is a backend-only fix, matching the issue's scope (mirrors #4229, which was also backend-only).
- Any change to `ValidationExceptionHandler` or `ValidationBehavior<,>` — both already behave correctly and are only reused.
- Auditing or fixing other Bank-module handlers for similar unvalidated-exception patterns beyond `ImportBankStatementRequest` (e.g. `GetBankStatementByIdRequest`) — out of scope for this issue.
- Rate limiting, retry, or idempotency concerns for the import endpoint — unrelated to this validation gap.

## Open Questions

## Status: COMPLETE
