# Design: Default-initialize BankAccountSettings.Accounts and remove redundant null-guards

## Component Design
- `BankAccountSettings` (`backend/src/Anela.Heblo.Domain/Features/Bank/BankAccountSettings.cs`): options class bound via `IOptions<BankAccountSettings>`. `Accounts` gets a default initializer (`= new();`) so it is a `List<BankAccountConfiguration>` that is never `null`, whether the `BankAccounts` configuration section is missing, empty, or populated. No other members change.
- `GetBankAccountsHandler.Handle`: reads `_bankSettings.Accounts` directly, dropping the `?? new List<BankAccountConfiguration>()` fallback. Same responsibility and output shape as before.
- `ImportBankStatementHandler.Handle`: reads `_bankSettings.Accounts` directly (no `?.`) to locate the requested account and to build the "available accounts" diagnostic string. Same responsibility, lookup logic, and exception type as before.

No component boundaries, interfaces, or DI registrations change — this is an internal simplification of three existing files.

## Data Schemas
No schema, DTO, or persisted-entity changes. `BankAccountConfiguration`, `GetBankAccountsResponse`, and `BankStatementImportResultDto` are all unchanged in shape and success-path values.

The only observable difference is the initializer on `BankAccountSettings.Accounts`:

```csharp
public List<BankAccountConfiguration> Accounts { get; set; } = new();
```

and, as an accepted side effect, the `ArgumentException` message thrown by `ImportBankStatementHandler` when zero accounts are configured now lists available accounts as `""` instead of the literal `"None"`.
