# Architecture Review: Default-initialize BankAccountSettings.Accounts

## Skip Design: true

## Architectural Fit Assessment
This is a trivial, self-contained defensive-programming cleanup, not a feature in the architectural sense. `BankAccountSettings` is a plain `IOptions<T>`-bound settings class in `Anela.Heblo.Domain.Features.Bank`; its `Accounts` property is consumed by exactly two Application-layer handlers (`GetBankAccountsHandler`, `ImportBankStatementHandler`), both already read via `IOptions<BankAccountSettings>.Value`. Adding `= new();` to the property is idiomatic .NET (the same pattern .NET options classes generally use for collection properties) and does not touch DI registration, configuration binding, module boundaries, or any public contract. There is no UI, no new endpoint, no schema/migration, and no new dependency. The change is fully contained within the existing Domain/Application layering and introduces no new integration points.

## Proposed Architecture

### Component Overview
No new components. Existing shape is unchanged:

```
appsettings (BankAccounts section)
        │  (IOptions<BankAccountSettings> binding)
        ▼
BankAccountSettings.Accounts : List<BankAccountConfiguration>   ← add "= new();" here
        │                             │
        ▼                             ▼
GetBankAccountsHandler        ImportBankStatementHandler
(remove `?? new List<>()`)    (remove `?.` guards)
```

### Key Design Decisions

#### Decision 1: Initialize the collection at the settings-class boundary vs. guard at each call site
**Options considered:**
1. Keep the status quo — each caller null-guards independently.
2. Default-initialize `Accounts` to `new()` in `BankAccountSettings` and drop the per-caller guards.
3. Introduce a computed/wrapped accessor (e.g. a method `GetAccounts()`) that returns a safe list.

**Chosen approach:** Option 2 — `public List<BankAccountConfiguration> Accounts { get; set; } = new();`

**Rationale:** This matches the brief and spec exactly, is the smallest possible change, and follows the standard .NET convention that `IOptions`-bound collection properties default to an empty collection rather than `null` (binding overwrites the initializer with actual entries when the section is populated, and leaves it as the initialized empty list when the section is absent — this is standard `ConfigurationBinder` behavior, not something this change needs to implement). Option 3 would add an abstraction layer for no benefit here — there's no logic beyond "don't be null." Option 1 is the status quo the brief identifies as the problem (boilerplate, and a missed guard becomes a `NullReferenceException`).

## Implementation Guidance

### Directory / Module Structure
No new files or directories. Edit in place:
- `backend/src/Anela.Heblo.Domain/Features/Bank/BankAccountSettings.cs`
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankAccounts/GetBankAccountsHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs`

### Interfaces and Contracts
No interface or contract changes. `IOptions<BankAccountSettings>` registration, `GetBankAccountsResponse`, and `BankStatementImportResultDto` are all unchanged. This is purely an internal implementation simplification.

### Data Flow
Unchanged. Configuration still binds into `BankAccountSettings` via `IOptions<T>`; handlers still read `_bankSettings.Accounts`. The only behavioral difference is that an unset/empty `BankAccounts` config section now yields `Accounts.Count == 0` instead of `Accounts == null` — which is exactly what both existing call sites already normalize to via their guards, so there is no observable behavior change on the success path.

One accepted, intentional wording change (already called out in the spec, FR-3): in `ImportBankStatementHandler`, when zero accounts are configured, the `ArgumentException` message's "Available accounts" list becomes `""` (empty string) instead of the literal `"None"`. This is a diagnostic-message-only difference with no functional impact — no test or caller should be asserting on that exact string, but confirm this during implementation (see Risks).

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| An existing test asserts on the literal `"None"` string in the `ArgumentException` message from `ImportBankStatementHandler` | Low | Search `backend/test` for `"None"` / this exception message before merging; update the assertion to expect `""` if found (spec FR-3 explicitly accepts this wording change) |
| A test elsewhere directly constructs `new BankAccountSettings()` and asserts `Accounts == null` | Very low | Search for `BankAccountSettings` in test code; unlikely given the property was clearly meant to be non-null per the brief |

## Specification Amendments
None. The spec (`spec.r1.md`) is complete, correctly scoped, and directly implementable as written — FR-1 through FR-3 map 1:1 onto the three file edits verified above.

## Prerequisites
None. No migration, no config change, no infrastructure work. Implementation can start immediately.
