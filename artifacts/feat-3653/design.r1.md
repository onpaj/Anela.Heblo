# Design: Type `GetBankStatementListRequest` date filters as `DateTime?`

## Component Design

### `GetBankStatementListRequest` (Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListRequest.cs)
MediatR request DTO (remains a plain class, per DTO conventions). Responsibility narrows to carrying already-typed filter values — it no longer participates in string parsing.

- `StatementDate`, `ImportDate`, `DateFrom`, `DateTo`: retyped `string?` → `DateTime?`.
- `Id`, `TransferId`, `Account`, `ErrorsOnly`, `Skip`, `Take`, `OrderBy`, `Ascending`: unchanged.

### `BankStatementsController.GetBankStatements` (API/Controllers/BankStatementsController.cs)
Owns the parsing/rejection boundary. The four `[FromQuery]` parameters (`statementDate`, `importDate`, `dateFrom`, `dateTo`) are retyped `string? = null` → `DateTime? = null`. With `[ApiController]` already applied, ASP.NET Core's model binder converts and validates these under `InvariantCulture` (set globally in `Program.cs`); a syntactically invalid value produces an automatic `400 ValidationProblemDetails` before the request reaches `MediatR.Send`. The action body is otherwise unchanged: it constructs `GetBankStatementListRequest` by passing the four values straight through.

### `GetBankStatementListHandler` (Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListHandler.cs)
Loses its transport-parsing responsibility entirely. The `ParseDateOrNull` private helper and its four call sites are removed. `Handle` constructs `BankStatementListFilter` using `request.StatementDate`, `request.ImportDate`, `request.DateFrom`, `request.DateTo` directly — no intermediate local `DateTime?` variables for dates. `NormalizeNullableString` and its use for `TransferId`/`Account` trimming, plus all skip/take/orderBy/ascending logic, are unchanged.

### `GetBankStatementListRequestValidator` (Application/Features/Bank/Validators/GetBankStatementListRequestValidator.cs)
Narrows to genuine business-rule validation only. `BeParseableDate` and the two `.Must(BeParseableDate)` rules ("DateFrom must be a valid date" / "DateTo must be a valid date") are removed — that concern is now fully owned by the controller's model binder. `DateFromIsNotLaterThanDateTo` is rewritten to compare typed `DateTime` values directly with a null-guard, and remains attached to `DateFrom`:

```csharp
RuleFor(x => x.DateFrom)
    .Must((req, dateFrom) => !dateFrom.HasValue || !req.DateTo.HasValue || dateFrom.Value.Date <= req.DateTo.Value.Date)
    .WithMessage("DateFrom must not be later than DateTo");
```

No `DateTime.TryParse` call remains anywhere in the validator. `TransferId`/`Account` length rules are unchanged.

### `frontend/src/api/hooks/useBankStatements.ts` — `useBankStatementsList`
Public contract to `ImportTab.tsx` is preserved unchanged: the hook's `GetBankStatementListRequest` TypeScript interface keeps `statementDate?: string; importDate?: string; dateFrom?: string; dateTo?: string;` (still `YYYY-MM-DD` strings sourced from `<input type="date">`). Internally, immediately before calling the regenerated `apiClient.bankStatements_GetBankStatements(...)` (whose parameters become `Date | null | undefined`), each present string is converted to a `Date`, using the same `?? undefined` guard style already used elsewhere in the hook — mirroring the existing `new Date(request.dateFrom)` pattern in `useBankStatementImport` in the same file:

```ts
request?.dateFrom ? new Date(request.dateFrom) : undefined
```

No change to `ImportTab.tsx` or any other consumer.

### `frontend/src/api/generated/api-client.ts`
Regenerated only (never hand-edited), per `docs/development/api-client-generation.md`. `bankStatements_GetBankStatements`'s `statementDate`, `importDate`, `dateFrom`, `dateTo` parameters change from `string | null | undefined` to `Date | null | undefined`, matching the controller signature change.

### Unaffected components (explicitly confirmed no change)
`BankStatementListFilter` (domain filter, already `DateTime?`), `IBankStatementImportRepository` / EF Core query implementation, `BankMappingProfile`, and `ImportTab.tsx` beyond compiling against the hook's unchanged public interface.

## Data Schemas

### Request DTO shape change
`GetBankStatementListRequest`:

```csharp
public DateTime? StatementDate { get; set; }   // was string?
public DateTime? ImportDate { get; set; }      // was string?
public DateTime? DateFrom { get; set; }        // was string?
public DateTime? DateTo { get; set; }          // was string?
```

All other properties unchanged.

### API query contract — `GET /api/bank-statements`
No route or response shape change. Query parameter value contract:

| Parameter | Before | After |
|---|---|---|
| `statementDate` | `string?` (any `DateTime.TryParse`-accepted format) | `DateTime?` (ASP.NET Core default model-binding conversion, invariant-culture, incl. ISO-8601) |
| `importDate` | `string?` | `DateTime?` |
| `dateFrom` | `string?` | `DateTime?` |
| `dateTo` | `string?` | `DateTime?` |

### Error response shape change (accepted, per spec NFR-2)
Invalid date values now short-circuit at ASP.NET Core model binding rather than FluentValidation:

- Before: `400` with FluentValidation-shaped body, e.g. `"DateFrom must be a valid date"`.
- After: `400` with standard `ValidationProblemDetails`, keyed by parameter name, e.g. `"dateFrom": ["The value 'not-a-date' is not valid."]`.

The one retained FluentValidation rule (`DateFrom` not later than `DateTo`) still surfaces as a FluentValidation-shaped `400` when both values are present and out of order — unchanged from today's behavior for that specific case.

### Frontend generated client signature change
`apiClient.bankStatements_GetBankStatements(...)` — `statementDate`, `importDate`, `dateFrom`, `dateTo` parameters: `string | null | undefined` → `Date | null | undefined`.

### No changes
`BankStatementListFilter` (already `DateTime?` for these four fields — no schema change), persisted data / domain entities, `BankImportRequestDto`, `GetBankStatementImportStatisticsRequest`.
