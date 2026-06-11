## Module
Analytics

## Finding

`GetMarginReportHandler.Handle()` — lines 33–52 — manually validates the same three rules already declared in `GetMarginReportRequestValidator`:

| Handler check (lines 33–52) | Validator rule (`GetMarginReportRequestValidator`) |
|---|---|
| `if (request.StartDate > request.EndDate)` → `ErrorCodes.InvalidDateRange` | `RuleFor(x => x.StartDate).LessThanOrEqualTo(x => x.EndDate)` |
| `if (totalDays > MAX_REPORT_PERIOD_DAYS)` → `ErrorCodes.InvalidReportPeriod` | `Must(x => … TotalDays <= MAX_REPORT_PERIOD_DAYS)` |
| `if (totalDays < MIN_REPORT_PERIOD_DAYS)` → `ErrorCodes.InvalidReportPeriod` | `Must(x => … TotalDays >= MIN_REPORT_PERIOD_DAYS)` |

The comment on line 33 in the handler says:
```csharp
// Basic input validation (kept here for backward compatibility with tests)
```

`GetMarginReportRequestValidator` is registered in `AnalyticsModule` (`services.AddScoped<IValidator<GetMarginReportRequest>, GetMarginReportRequestValidator>()`) and is wired into the MediatR pipeline. If the pipeline runs the validator before the handler (the normal configuration), the handler-level checks are unreachable dead code for any invalid input.

The same pattern exists in `GetProductMarginAnalysisHandler` lines 30–39 vs `GetProductMarginAnalysisRequestValidator`.

## Why it matters

- **Dead code**: the comment itself admits these checks exist only because of test coupling. Tests that directly instantiate and call `Handle()` without the MediatR pipeline bypass validation — they should be testing the handler's domain logic, not re-testing validation rules.
- **Divergence risk**: if validation constants change in `AnalyticsConstants`, the validator and handler must both be updated. They can drift silently.
- **Misleading intent**: a future reader sees handler-level validation and may conclude the validator isn't in the pipeline.

## Suggested fix

Remove the three manual `if`-checks from `GetMarginReportHandler.Handle()` (lines 33–52) and from `GetProductMarginAnalysisHandler.Handle()` (lines 30–39). Update any unit tests that invoke `Handle()` directly without the pipeline to either: (a) pass valid inputs (letting the handler focus on business-logic paths), or (b) be moved to validator unit tests. The validation responsibility belongs exclusively in the FluentValidation classes.

---
_Filed by daily arch-review routine on 2026-05-28._