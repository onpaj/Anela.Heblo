## Module
Bank

## Finding
`ComgateCzkImportJob`, `ComgateEurImportJob`, and `ShoptetPayImportJob` (all in `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/Jobs/`) are ~60-line classes with identical constructor signatures, identical `ExecuteAsync` structure, and identical error handling. The only differences are:

| Class | `JobName` | Account name string | Date |
|---|---|---|---|
| `ComgateCzkImportJob` | `"daily-comgate-czk-import"` | `"ComgateCZK"` | `DateTime.Today.AddDays(-1)` |
| `ComgateEurImportJob` | `"daily-comgate-eur-import"` | `"ComgateEUR"` | `DateTime.Today.AddDays(-1)` |
| `ShoptetPayImportJob` | `"daily-shoptetpay-czk-import"` | `"ShoptetPay-CZK"` | `DateTime.Today` |

The `ExecuteAsync` body (check-enabled → try → log → send → log → catch+rethrow) is copy-pasted verbatim across all three files. Any bug fix or logging change must be applied to three places.

Additionally, the account name strings (`"ComgateCZK"`, `"ComgateEUR"`, `"ShoptetPay-CZK"`) are hardcoded magic strings that must match the `BankAccounts` configuration keys exactly. A configuration rename silently breaks the job with an `ArgumentException` at runtime.

## Why it matters
180 lines of near-identical code for what is fundamentally one operation with different parameters. The duplication violates DRY in a meaningful way (not speculative 3-line similarity) and increases maintenance cost — e.g., logging format changes, cancellation token handling, or retry logic must be replicated across all three files.

## Suggested fix
Extract a `BankImportJobBase` abstract class (or a single `ScheduledBankImportJob` parameterised via constructor) containing the `ExecuteAsync` template. Each concrete job (or a registration with different metadata) supplies only the account name, date offset, and cron expression. The account name strings should become constants (e.g. `BankAccountNames.ComgateCzk`) to make configuration mismatches detectable at compile time.

---
_Filed by daily arch-review routine on 2026-05-29._