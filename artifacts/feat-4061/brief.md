## Module
Bank

## Finding
`backend/src/Anela.Heblo.Domain/Features/Bank/BankAccountSettings.cs` line 7:

```csharp
public class BankAccountSettings
{
    public const string ConfigurationKey = "BankAccounts";
    public List<BankAccountConfiguration> Accounts { get; set; }
}
```

`Accounts` has no default value, so it is `null` when the config section is missing or empty. Every caller must independently remember to guard against null:

- `GetBankAccountsHandler.cs` line 24: `(_bankSettings.Accounts ?? new List<BankAccountConfiguration>())`
- `ImportBankStatementHandler.cs` lines 54–58: `_bankSettings.Accounts?.SingleOrDefault(...)` plus a follow-up null check and a second null-conditional on line 57 to list available accounts

Adding a caller means adding another null-guard. If one is missed the handler throws a `NullReferenceException` at runtime instead of returning a meaningful error.

## Why it matters
KISS / defensive programming: a mutable collection property should always be initialised so that callers can iterate it unconditionally. The scattered `?? new List<>()` calls are boilerplate that obscures the real logic.

## Suggested fix
Add a default initialiser in the settings class:

```csharp
public List<BankAccountConfiguration> Accounts { get; set; } = new();
```

Then simplify the callers:
- `GetBankAccountsHandler`: remove the `?? new List<BankAccountConfiguration>()` fallback.
- `ImportBankStatementHandler`: change `_bankSettings.Accounts?.SingleOrDefault(...)` to `_bankSettings.Accounts.SingleOrDefault(...)`.

One-line change in the settings class; the handlers become easier to read.

---
_Filed by daily arch-review routine on 2026-09-04._
