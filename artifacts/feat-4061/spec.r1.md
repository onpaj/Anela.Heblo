# Specification: Default-initialize BankAccountSettings.Accounts and remove redundant null-guards

## Summary
`BankAccountSettings.Accounts` (`List<BankAccountConfiguration>`) has no default initializer, so it is `null` whenever the `BankAccounts` configuration section is missing or empty. This forces every caller to defensively null-guard the property, and two handlers currently do so with scattered, inconsistent boilerplate. This change adds a single default initializer to the settings class and removes the now-unnecessary null-guards from both callers.

## Background
`BankAccountSettings` is bound from configuration via `IOptions<BankAccountSettings>` and exposes `Accounts`, the list of configured bank accounts. Because the property has no default value, binding leaves it `null` when the `BankAccounts` section is absent or empty (e.g. a misconfigured environment), rather than an empty list. Two handlers already work around this:
- `GetBankAccountsHandler.Handle` falls back with `(_bankSettings.Accounts ?? new List<BankAccountConfiguration>())`.
- `ImportBankStatementHandler.Handle` uses `_bankSettings.Accounts?.SingleOrDefault(...)` and a second null-conditional a few lines later to build a diagnostic "available accounts" string.

Every future caller of `_bankSettings.Accounts` must remember to add the same guard, or risk a `NullReferenceException` instead of a meaningful error. Initializing the property to an empty list at the source (KISS / defensive-programming at the boundary) removes this repeated burden and lets callers iterate the collection unconditionally.

## Functional Requirements

### FR-1: Default-initialize `BankAccountSettings.Accounts`
Change the `Accounts` property in `backend/src/Anela.Heblo.Domain/Features/Bank/BankAccountSettings.cs` to default to an empty list:

```csharp
public List<BankAccountConfiguration> Accounts { get; set; } = new();
```

**Acceptance criteria:**
- `Accounts` is never `null`, whether or not the `BankAccounts` configuration section is present, empty, or fully populated.
- When the `BankAccounts` section is missing or empty, `Accounts` is an empty list (`Count == 0`), not `null`.
- When the `BankAccounts` section is populated, binding still produces the expected list of `BankAccountConfiguration` entries (no behavior change for the populated case).

### FR-2: Simplify `GetBankAccountsHandler`
Remove the now-unnecessary null fallback in `GetBankAccountsHandler.Handle` (`backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankAccounts/GetBankAccountsHandler.cs`, line 24):

```csharp
// Before
var accounts = (_bankSettings.Accounts ?? new List<BankAccountConfiguration>())
    .Select(...)

// After
var accounts = _bankSettings.Accounts
    .Select(...)
```

**Acceptance criteria:**
- The `?? new List<BankAccountConfiguration>()` fallback is removed.
- Behavior is unchanged: with no configured accounts, `GetBankAccountsResponse.Accounts` is still an empty list; with configured accounts, the same DTOs are returned as before.

### FR-3: Simplify `ImportBankStatementHandler`
Remove the null-conditional operators on `_bankSettings.Accounts` in `ImportBankStatementHandler.Handle` (`backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs`, lines 54 and 57–59):

```csharp
// Before
var accountSetting = _bankSettings.Accounts?.SingleOrDefault(a => a.Name == request.AccountName);
if (accountSetting == null)
{
    var availableAccounts = _bankSettings.Accounts != null
        ? string.Join(", ", _bankSettings.Accounts.Select(a => a.Name))
        : "None";
    ...
}

// After
var accountSetting = _bankSettings.Accounts.SingleOrDefault(a => a.Name == request.AccountName);
if (accountSetting == null)
{
    var availableAccounts = string.Join(", ", _bankSettings.Accounts.Select(a => a.Name));
    ...
}
```

**Acceptance criteria:**
- `_bankSettings.Accounts?.SingleOrDefault(...)` becomes `_bankSettings.Accounts.SingleOrDefault(...)`.
- The `_bankSettings.Accounts != null ? ... : "None"` conditional is removed; `availableAccounts` is computed directly via `string.Join(", ", _bankSettings.Accounts.Select(a => a.Name))`.
- When no accounts are configured, the resulting `availableAccounts` string is empty (`""`) rather than the literal `"None"` produced by the old code path — this is an accepted, intentional side effect of the simplification (a minor wording change to a log/exception message, not a functional regression).
- The thrown `ArgumentException` message and all other exception/logging behavior remain otherwise unchanged.
- No other logic in the method (state lookup, statement processing, dedup, error handling) is touched.

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — this is a null-check removal with no algorithmic or I/O change. No measurable performance impact.

### NFR-2: Security
Not applicable — no change to authentication, authorization, or data sensitivity. `BankAccountConfiguration` entries (account numbers, provider credentials references, etc.) are unaffected; only the collection's nullability changes.

## Data Model
No data model changes. `BankAccountSettings.Accounts` remains a `List<BankAccountConfiguration>`; only its default value changes (`null` → `new()`). No changes to `BankAccountConfiguration` or any persisted entity.

## API / Interface Design
No public API surface changes. `GetBankAccountsResponse` and `BankStatementImportResultDto` shapes and their success-path values are unchanged. The only observable behavior difference is the exact text of the `availableAccounts` string in the `ArgumentException` thrown by `ImportBankStatementHandler` when zero accounts are configured (see FR-3 acceptance criteria).

## Dependencies
None. Self-contained change within `Anela.Heblo.Domain` (settings class) and two existing handlers in `Anela.Heblo.Application`. No new packages, services, or configuration changes required.

## Out of Scope
- Any change to how `BankAccountSettings` is registered or bound in DI/configuration (`IOptions<BankAccountSettings>` wiring).
- Any change to `BankAccountConfiguration` itself.
- Any change to other bank-related handlers not named in the brief.
- Any change to logging levels, message wording (beyond the direct effect described in FR-3), or exception types.
- Adding unit tests beyond what the existing test suite already covers (not required by the brief; existing tests, if any, must continue to pass).

## Open Questions
None.

## Status: COMPLETE
