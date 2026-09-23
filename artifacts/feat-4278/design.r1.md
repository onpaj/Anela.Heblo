# Design: FluentValidation for ImportBankStatementRequest

## Component Design

### `ImportBankStatementRequestValidator` (new)
- **Location:** `backend/src/Anela.Heblo.Application/Features/Bank/Validators/ImportBankStatementRequestValidator.cs`
- **Type:** `public class ImportBankStatementRequestValidator : AbstractValidator<ImportBankStatementRequest>`
- **Responsibility:** The single source of truth for whether an `ImportBankStatementRequest` is well-formed enough to attempt an import. Owns exactly two rules:
  1. `AccountName` is non-empty and matches a configured `BankAccountConfiguration.Name`.
  2. `DateFrom.Date <= DateTo.Date`.
- **Dependencies:** `IOptions<BankAccountSettings>` (constructor-injected), read once in the constructor into a local `settings` variable and closed over by the `Must(...)` lambda. No other dependencies, no I/O, fully synchronous.
- **Lifetime:** `Scoped` (registered via `services.AddScoped<IValidator<ImportBankStatementRequest>, ImportBankStatementRequestValidator>()`), consistent with `GetBankStatementListRequestValidator`'s registration and with `IOptions<BankAccountSettings>` being safe to resolve at any lifetime (it's a singleton-backed options snapshot).

### `ImportBankStatementHandler` (modified)
- **Location:** `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs` (existing file, no rename).
- **Change:** The account lookup at line 50 changes from `.SingleOrDefault(a => a.Name == request.AccountName)` + null-check + `throw new ArgumentException(...)` to `.Single(a => a.Name == request.AccountName)`. No other line in the handler changes. The handler's constructor, dependencies, logging for the import-run lifecycle (start/completed/failed), and all downstream processing (`ProcessStatementAsync`, `InsertNewAsync`, `UpsertExistingAsync`) are unchanged.
- **Contract:** `IRequestHandler<ImportBankStatementRequest, BankStatementImportResultDto>` — unchanged. Callers (the controller, and anything else invoking this via `IMediator.Send`) see no interface change; they only observe that a request with an unknown/empty `AccountName` or `DateFrom > DateTo` now throws `FluentValidation.ValidationException` *before* `Handle` is ever entered, rather than `ImportBankStatementHandler` throwing `ArgumentException` from inside `Handle`.

### `BankModule.AddBankModule` (modified)
- **Location:** `backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs` (existing file).
- **Change:** Two additional `services.AddScoped(...)` lines, placed immediately after the existing list-validator/behavior pair (current lines 29–32), registering:
  - `IValidator<ImportBankStatementRequest>` → `ImportBankStatementRequestValidator`
  - `IPipelineBehavior<ImportBankStatementRequest, BankStatementImportResultDto>` → `ValidationBehavior<ImportBankStatementRequest, BankStatementImportResultDto>`
- No other line in this file changes.

### Reused, unmodified components
- `ValidationBehavior<TRequest, TResponse>` (`Anela.Heblo.Application.Common.Behaviors`) — generic MediatR pipeline behavior that runs all registered `IValidator<TRequest>` and throws `FluentValidation.ValidationException` on any failure. Already correct and generic; reused as-is for the new `TRequest`/`TResponse` pair.
- `ValidationExceptionHandler` (`Anela.Heblo.API.Infrastructure.ExceptionHandling`) — global `IExceptionHandler` already registered in the API host; already maps any `FluentValidation.ValidationException` to a 400 `ProblemDetails` with an `errors` extension array. No change needed; it is type-based (`is ValidationException`), so it automatically covers the new validator without any registration change on its side.
- `BankStatementsController.ImportStatements` — unchanged. It already just calls `_mediator.Send(importRequest)` with no try/catch around the specific exception types being changed here, so the new failure mode (thrown before `Handle`, caught globally) requires no controller change.

## Data Schemas

### Request shape (unchanged)
```csharp
public class ImportBankStatementRequest : IRequest<BankStatementImportResultDto>
{
    public string AccountName { get; set; } = null!;
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
}
```
No property added, removed, or retyped. `BankImportRequestDto` (the HTTP-facing DTO the controller maps from) is likewise unchanged.

### Error response shape for the new failure paths (via existing `ValidationExceptionHandler`, unchanged handler code — shown here for the two new scenarios this validator introduces)

Unknown/empty `AccountName`:
```json
{
  "status": 400,
  "title": "Validation Failed",
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "errors": [
    { "propertyName": "AccountName", "errorMessage": "AccountName is required" }
  ]
}
```
or, when non-empty but unknown:
```json
{
  "status": 400,
  "title": "Validation Failed",
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "errors": [
    { "propertyName": "AccountName", "errorMessage": "Account name Foo not found in BankAccounts configuration." }
  ]
}
```

`DateFrom` later than `DateTo`:
```json
{
  "status": 400,
  "title": "Validation Failed",
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "errors": [
    { "propertyName": "DateFrom", "errorMessage": "DateFrom must not be later than DateTo" }
  ]
}
```

This shape is produced entirely by the existing, unmodified `ValidationExceptionHandler` — it is documented here only so the developer and any test author know exactly what the response body looks like for these two new scenarios; nothing about the handler itself is part of this change.

### Success response shape (unchanged)
`BankStatementImportResultDto` — untouched by this change; a request that passes validation flows through `Handle` exactly as before and returns the same shape it does today.
