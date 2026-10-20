# Architecture Review: FluentValidation for ImportBankStatementRequest

## Skip Design: true

## Architectural Fit Assessment
This fits the existing MediatR + FluentValidation vertical-slice pattern exactly. The Bank module already runs this mechanism for `GetBankStatementListRequest`: a `Validators/` folder holds an `AbstractValidator<TRequest>`, `BankModule.AddBankModule` registers it as `IValidator<TRequest>` plus a matching `ValidationBehavior<TRequest, TResponse>` `IPipelineBehavior`, and a single global `IExceptionHandler` (`ValidationExceptionHandler`) turns any `FluentValidation.ValidationException` into a 400 `ProblemDetails`. This is also the identical mechanism used to fix the sibling defect in Analytics (#4229). There is no architectural gap to close — the module already has the plumbing; it is simply not wired up for this one request type. The change is additive and localized to the Bank module; no other module, controller, or cross-cutting concern needs to change.

The only integration points are:
1. `BankModule.AddBankModule` (DI registration) — same method, two more `services.AddScoped(...)` lines next to the existing list-validator registrations.
2. `ImportBankStatementHandler.Handle` — deletion of the now-redundant defensive `ArgumentException` block, since the pipeline now guarantees a valid account name before `Handle` runs.

No new abstractions, no new interfaces, no new cross-module contracts.

## Proposed Architecture

### Component Overview

```
BankStatementsController.ImportStatements
        |
        v
   IMediator.Send(ImportBankStatementRequest)
        |
        v
ValidationBehavior<ImportBankStatementRequest, BankStatementImportResultDto>   <-- NEW registration
        |  (runs IValidator<ImportBankStatementRequest>.ValidateAsync)
        |
   [ImportBankStatementRequestValidator]                                       <-- NEW class
        |  - AccountName non-empty + matches a configured BankAccountConfiguration.Name
        |  - DateFrom.Date <= DateTo.Date
        |
        |-- invalid --> throws FluentValidation.ValidationException
        |                      |
        |                      v
        |          ValidationExceptionHandler (existing, unchanged)
        |                      |
        |                      v
        |               HTTP 400 ProblemDetails
        |
        `-- valid --> next() --> ImportBankStatementHandler.Handle
                                       |  (ArgumentException block REMOVED —
                                       |   account presence now guaranteed)
                                       v
                                 existing import logic (unchanged)
```

This mirrors the already-running list-query path:

```
GetBankStatementList  -->  ValidationBehavior<GetBankStatementListRequest, GetBankStatementListResponse>
                       -->  GetBankStatementListRequestValidator
```

### Key Design Decisions

#### Decision 1: Validator reads account names from `IOptions<BankAccountSettings>`, not from a repository or the `IBankClientFactory`
**Options considered:**
- (a) Inject `IOptions<BankAccountSettings>` directly into the validator and check `Accounts.Any(a => a.Name == name)`.
- (b) Inject some higher-level "account lookup" service/abstraction.
- (c) Leave the check in the handler and only validate `AccountName` non-empty + date range in the validator.

**Chosen approach:** (a). `IOptions<BankAccountSettings>` is the same configuration object the handler already uses (`_bankSettings`), it requires no new abstraction, and it is exactly how `GetBankStatementListRequestValidator`'s sibling in Analytics/Catalog validate against configuration-backed allow-lists in this codebase's existing FluentValidation usage (validators here are plain `AbstractValidator<T>` with constructor-injected dependencies, registered `Scoped` — configuration is a natural, already-`Scoped`-compatible dependency via `IOptions<T>`).

**Rationale:** No new interface needed; keeps the validator a pure, side-effect-free check against already-loaded configuration (no I/O, no async needed inside the rule); testable by constructing `Options.Create(new BankAccountSettings { Accounts = [...] })` in unit tests, matching how other settings-dependent validators in this codebase are already tested.

#### Decision 2: Full removal of the handler's `ArgumentException` guard, not a defensive downgrade
**Options considered:**
- (a) Remove the `if (accountSetting == null) throw ...` block entirely and use `.Single(...)`.
- (b) Keep `.SingleOrDefault(...)` + null check but change the exception type to something that maps to 400 independently of the validator (defense in depth).
- (c) Leave the block entirely unchanged and only add the validator as a redundant, duplicate guard.

**Chosen approach:** (a), with `.Single(a => a.Name == request.AccountName)` replacing `.SingleOrDefault(...)` + the manual throw. Leaving a second, differently-behaved guard in the handler (options b/c) reintroduces exactly the inconsistency this issue is about — a request that reaches `Handle` with an unknown account name should never happen once the validator is wired into the pipeline, so a second bespoke check is dead code that only adds a second place to keep in sync with the validator's logic. If `.Single(...)` throws `InvalidOperationException` on a hypothetical race (e.g. hot-reloaded configuration changing between validation and handling), that is an actual server fault (500 is correct), not a client input error — which is the right outcome for a scenario the validator cannot reasonably predict.

**Rationale:** Matches how `GetBankStatementListHandler` behaves — it does not re-validate `Take`/`Skip`/date-range bounds that its validator already guarantees; the handler trusts the pipeline. Consistency of pattern within the same module outweighs marginal defense-in-depth value here, especially since both the validator and the handler read from the *same* `IOptions<BankAccountSettings>` snapshot within a single request, so there's no realistic window for them to disagree.

#### Decision 3: Property-less rule for the date-range check, matching the list validator's convention exactly
**Options considered:**
- (a) `RuleFor(x => x).Must(r => r.DateFrom.Date <= r.DateTo.Date).WithMessage(...)` (property-less, whole-object rule).
- (b) `RuleFor(x => x.DateFrom).Must((req, dateFrom) => dateFrom.Date <= req.DateTo.Date).WithMessage(...)` (attached to the `DateFrom` property, as `GetBankStatementListRequestValidator` does).

**Chosen approach:** (b) — attach to `DateFrom`, exactly as `GetBankStatementListRequestValidator` line 29–31 already does (`RuleFor(x => x.DateFrom).Must((req, dateFrom) => ...)`), rather than the property-less form the issue's own suggested-fix snippet used. No `.When(...)` guard is needed here (unlike the list validator) because `DateFrom`/`DateTo` are non-nullable `DateTime` on `ImportBankStatementRequest`, so the rule is unconditional.

**Rationale:** Keeps `propertyName` in the resulting `ProblemDetails.errors[]` array consistent with the sibling validator in the same module (`"DateFrom"` rather than `""`), which is a nicer client-facing contract and avoids introducing a second convention for the same kind of rule inside one module. This is a deliberate amendment to the issue's suggested-fix snippet — see **Specification Amendments** below.

## Implementation Guidance

### Directory / Module Structure
- New file: `backend/src/Anela.Heblo.Application/Features/Bank/Validators/ImportBankStatementRequestValidator.cs` (same folder as `GetBankStatementListRequestValidator.cs`).
- Modified: `backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs` — add the two DI registrations directly below the existing list-validator registrations (lines 29–32).
- Modified: `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs` — delete lines 50–61 (the `SingleOrDefault` + null-check + throw), replace with `.Single(a => a.Name == request.AccountName)`.
- New test file: `backend/test/Anela.Heblo.Tests/Features/Bank/Validators/ImportBankStatementRequestValidatorTests.cs` (new `Validators/` subfolder under `Features/Bank/` in the test project — the test project does not yet mirror the `Validators/` folder for Bank, unlike `Features/Analytics/Validators/` and `Features/Catalog/Validators/`, which already exist as precedent for this subfolder naming).
- Modified: `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs` — remove or repurpose any existing test(s) asserting the handler throws `ArgumentException` for an unknown account name (that behavior no longer exists in the handler); the validator's own test file now owns that assertion.

### Interfaces and Contracts

```csharp
namespace Anela.Heblo.Application.Features.Bank.Validators;

public class ImportBankStatementRequestValidator : AbstractValidator<ImportBankStatementRequest>
{
    public ImportBankStatementRequestValidator(IOptions<BankAccountSettings> bankSettings)
    {
        var settings = bankSettings.Value;

        RuleFor(x => x.AccountName)
            .NotEmpty().WithMessage("AccountName is required")
            .Must(name => settings.Accounts.Any(a => a.Name == name))
            .WithMessage(x => $"Account name {x.AccountName} not found in {BankAccountSettings.ConfigurationKey} configuration.")
            .When(x => !string.IsNullOrEmpty(x.AccountName));

        RuleFor(x => x.DateFrom)
            .Must((req, dateFrom) => dateFrom.Date <= req.DateTo.Date)
            .WithMessage("DateFrom must not be later than DateTo");
    }
}
```

Notes for the developer:
- `.When(x => !string.IsNullOrEmpty(x.AccountName))` on the second `AccountName` rule prevents the "not found" message from also firing (redundantly, alongside "is required") when the value is empty — same style FluentValidation encourages and consistent with `.When(...)` usage elsewhere in this module's validators.
- Do not reproduce the full "Available accounts: {list}" suffix from the original `ArgumentException` message verbatim unless the developer confirms the frontend does not render raw exception text anywhere for this call today — keep the message short (`Account name {name} not found ...` without enumerating all configured names) to avoid changing the amount of configuration detail exposed to a client, per Spec NFR-2. If enumerating is actually desired, that's a spec amendment to confirm with a human, not a default to silently carry over.
- Registration in `BankModule.AddBankModule`, placed directly after the existing list-validator/behavior pair:
  ```csharp
  services.AddScoped<IValidator<ImportBankStatementRequest>, ImportBankStatementRequestValidator>();
  services.AddScoped<
      IPipelineBehavior<ImportBankStatementRequest, BankStatementImportResultDto>,
      ValidationBehavior<ImportBankStatementRequest, BankStatementImportResultDto>>();
  ```
- Handler change: replace
  ```csharp
  var accountSetting = _bankSettings.Accounts.SingleOrDefault(a => a.Name == request.AccountName);
  if (accountSetting == null)
  {
      var availableAccounts = string.Join(", ", _bankSettings.Accounts.Select(a => a.Name));
      _logger.LogError(...);
      throw new ArgumentException(...);
  }
  ```
  with
  ```csharp
  var accountSetting = _bankSettings.Accounts.Single(a => a.Name == request.AccountName);
  ```
  The `_logger.LogError` call for the not-found case is removed along with the block — the validator failing means `Handle` is never invoked for that request, so this line is now unreachable dead code; there is nothing to log at this point. If the developer wants an audit trail for rejected import attempts, that belongs in `ValidationExceptionHandler` or a logging pipeline behavior, not in this handler — out of scope for this issue.

### Data Flow
1. Controller maps `BankImportRequestDto` → `ImportBankStatementRequest`, calls `IMediator.Send(...)`.
2. MediatR resolves the pipeline for `ImportBankStatementRequest` → `BankStatementImportResultDto`, which now includes `ValidationBehavior<...>` (in addition to whatever pipeline behaviors already apply globally, if any).
3. `ValidationBehavior` resolves all registered `IValidator<ImportBankStatementRequest>` (currently exactly one: the new validator) and runs them.
4. On failure: `FluentValidation.ValidationException` propagates up through MediatR, through the controller (uncaught there, by design — same as the list endpoint), to the global exception-handling middleware, where `ValidationExceptionHandler.TryHandleAsync` catches it and writes the 400 `ProblemDetails`.
5. On success: `next()` invokes `ImportBankStatementHandler.Handle`, which now looks up the account with `.Single(...)` (guaranteed to find exactly one match) and proceeds exactly as today.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Existing `ImportBankStatementHandlerTests` assert the old `ArgumentException`-for-unknown-account behavior directly against the handler (bypassing the pipeline, as unit tests typically do) — those tests will fail to compile/pass once the block is removed. | Medium | Developer must update `ImportBankStatementHandlerTests.cs` to remove/replace that specific test case (moving the assertion to the new validator's test file) as part of this same change — called out explicitly in Spec FR-4 and Directory/Module Structure above. |
| Any existing integration or controller-level test that hits `POST /api/bank-statements/import` with an unknown account name and asserts on a 500 / on `ArgumentException` message content. | Low | Grep `BankStatementsControllerTests.cs` and `BankStatementImportIntegrationTests.cs` for `ArgumentException` / unknown-account scenarios before merging; update expected status code to 400 and body shape to `ProblemDetails` if any such test exists. |
| Message-text drift: the validator's error message is not required to match the old exception message verbatim (Spec FR-1 says "not required verbatim"), but any FE code or E2E test asserting on the literal old message text would break. | Low | Grep `frontend/` and `frontend/test/e2e/` for the literal string `"not found in"` or the old exception message fragment before merging. Not expected to exist (this is a backend-only 500-vs-400 fix per issue), but cheap to check. |
| Ordering of `IPipelineBehavior` registrations: if some other global behavior (e.g. logging, transaction) is registered `Open Generic` and expected to run before/after validation for all requests, adding a per-request `ValidationBehavior` registration must not change that relative order for `ImportBankStatementRequest`. | Low | Registration pattern is copy-pasted from the already-working `GetBankStatementListRequest` registration in the same method — if that one is correctly ordered relative to any global behaviors, this one will be too, by construction. |

## Specification Amendments
- **FR-2 / suggested-fix snippet:** The spec's date-range rule should be implemented attached to `RuleFor(x => x.DateFrom)` (matching `GetBankStatementListRequestValidator`'s existing convention), not as a property-less `RuleFor(x => x)` rule as the issue's own suggested code sketch shows. This changes the `propertyName` reported in the 400 body's `errors[]` array from `""` to `"DateFrom"` — a minor, backward-compatible-in-spirit improvement in line with the module's existing convention. See Decision 3 above.
- **NFR-2 clarification:** The validator's not-found message for `AccountName` should NOT include the full "Available accounts: {list}" enumeration that the original `ArgumentException` message included, to avoid handing a client-facing error message more configuration detail than the minimum needed to explain the failure. This is a tightening relative to the issue's suggested-fix snippet (`.WithMessage(x => $"Account '{x.AccountName}' is not configured.")` in the issue is already minimal and is the recommended form — the concern is only that a developer might reflexively copy the *handler's* old, more verbose message instead).
- No other changes to the spec's functional scope.

## Prerequisites
None. No migrations, no new configuration keys, no infrastructure changes. `BankAccountSettings` configuration already exists and is already bound via `services.Configure<BankAccountSettings>(...)` in `BankModule.AddBankModule`.
