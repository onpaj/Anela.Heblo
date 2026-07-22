## Module
Bank

## Finding
`GetBankStatementListRequest` declares four date filter fields as `string?` instead of `DateTime?`:

```csharp
// backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListRequest.cs lines 10-13
public string? StatementDate { get; set; }
public string? ImportDate { get; set; }
public string? DateFrom { get; set; }
public string? DateTo { get; set; }
```

This causes two downstream problems:

**1. Handler does string-to-date parsing** (`GetBankStatementListHandler.cs` lines 29-32, 66-67):
```csharp
DateTime? statementDate = ParseDateOrNull(request.StatementDate);
DateTime? importDate    = ParseDateOrNull(request.ImportDate);
DateTime? dateFrom      = ParseDateOrNull(request.DateFrom);
DateTime? dateTo        = ParseDateOrNull(request.DateTo);

// helper at lines 66-67:
private static DateTime? ParseDateOrNull(string? value) =>
    !string.IsNullOrWhiteSpace(value) && DateTime.TryParse(value, out var parsed) ? parsed : null;
```

**2. Validator re-implements date parsing** (`GetBankStatementListRequestValidator.cs` lines 29-43):
```csharp
RuleFor(x => x.DateFrom!)
    .Must(BeParseableDate)
    .WithMessage("DateFrom must be a valid date")
    .When(x => !string.IsNullOrWhiteSpace(x.DateFrom));
// ...
private static bool BeParseableDate(string? value) =>
    string.IsNullOrWhiteSpace(value) || DateTime.TryParse(value, out _);
```

`DateTime.TryParse` is called three places for the same fields — the validator, the handler's `ParseDateOrNull` helper, and the validator's cross-field `DateFromIsNotLaterThanDateTo` check.

The controller already receives these as plain `string?` query parameters and passes them through unchanged (`BankStatementsController.cs` lines 84-88).

## Why it matters
Input parsing — converting a raw string to a typed value — belongs at the HTTP boundary (ASP.NET Core model binding), not inside a MediatR handler. The handler's job is business logic. `ParseDateOrNull` is not business logic; it is a workaround for the `string?` field type. The duplication between validator and handler means a future format change (e.g. accepting ISO-8601 only) requires edits in two places and is likely to be missed in one of them.

## Suggested fix
Change the four fields in `GetBankStatementListRequest` and the corresponding controller parameters to `DateTime?`:

```csharp
// GetBankStatementListRequest.cs — replace lines 10-13:
public DateTime? StatementDate { get; set; }
public DateTime? ImportDate { get; set; }
public DateTime? DateFrom { get; set; }
public DateTime? DateTo { get; set; }
```

```csharp
// BankStatementsController.cs — replace string? params with DateTime?:
[FromQuery] DateTime? statementDate = null,
[FromQuery] DateTime? importDate = null,
[FromQuery] DateTime? dateFrom = null,
[FromQuery] DateTime? dateTo = null,
```

With `DateTime?` fields, ASP.NET Core model binding rejects invalid dates with a standard 400 before the handler is ever invoked. In the handler, drop `ParseDateOrNull` and pass the typed values directly to `BankStatementListFilter`. In the validator, the `BeParseableDate` rule and `DateFromIsNotLaterThanDateTo` simplify to a single typed comparison with no string parsing.

---
_Filed by daily arch-review routine on 2026-07-15._
