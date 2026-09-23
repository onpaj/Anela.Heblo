# Code Review: register-import-bank-statement-validator-in-di

## Summary
The implementation adds the two DI registrations (`IValidator<ImportBankStatementRequest>` and its `ValidationBehavior<ImportBankStatementRequest, BankStatementImportResultDto>` pipeline behavior) to `BankModule.AddBankModule`, exactly matching the task-context's specified diff and the existing `GetBankStatementListRequest` pattern two lines above. Build is clean (0 errors), `dotnet format --verify-no-changes` reports no issues, and the Bank test suite's DI-container-backed HTTP integration tests (`BankStatementImportIntegrationTests`, via `WebApplicationFactory`) pass, confirming the new registrations resolve without host-startup errors.

## Review Result: PASS

### task: register-import-bank-statement-validator-in-di
**Status:** PASS

Verified against spec.r1.md:
- FR-3 acceptance criteria: both required `services.AddScoped<...>` lines are present verbatim in `BankModule.AddBankModule`, in the exact form specified (`IValidator<ImportBankStatementRequest>` → `ImportBankStatementRequestValidator`; `IPipelineBehavior<ImportBankStatementRequest, BankStatementImportResultDto>` → `ValidationBehavior<ImportBankStatementRequest, BankStatementImportResultDto>>`).
- "A request that fails validation never reaches the handler" (FR-3's third acceptance criterion): this is a property of the shared, already-tested `ValidationBehavior<TRequest,TResponse>` pipeline behavior class itself (already relied upon by `GetBankStatementListRequest`), not new logic introduced by this task — registering the generic behavior for a new `TRequest`/`TResponse` pair inherits that guarantee. Confirmed indirectly: `BankStatementImportIntegrationTests` (a `WebApplicationFactory`-based HTTP test that builds the real DI container) passed with no host-startup `AggregateException` from `IServiceProvider` validation, which is what would occur if the registration were wrong (e.g. mismatched generic parameters, missing dependency).
- Two new `using` statements added, both necessary (`Contracts` for `BankStatementImportResultDto`, `UseCases.ImportBankStatement` for `ImportBankStatementRequest`); `Validators` was already present as stated in the task context.
- Scope: changes are confined to `BankModule.cs`; the handler's redundant `ArgumentException` (FR-4, a separate task) is untouched, correctly out of scope here.
- Architecture adherence: registration order and shape are identical to the existing `GetBankStatementListRequest` block — no deviation from the established DI-registration convention in this module.
- Build: `dotnet build` on the Application project succeeds with 0 errors; `dotnet format --verify-no-changes` reports no formatting issues on the touched file.
- Correctness: no logic errors — this is a mechanical, additive DI wiring change with no behavioral branching to get wrong.

## Docs to Update
None. Internal DI wiring change with no public API shape change, no new config key, no new CLI, and no change to project layout or pipeline stages.

## Overall Notes
No cross-cutting concerns. The implementation correctly did not attempt to write new unit tests for this task (none were required by the task-context — the validator's behavior is already covered by the prior task's `ImportBankStatementRequestValidatorTests`, and this task is pure wiring). The developer additionally ran the full Bank test suite as a sanity check; the 12 failures observed there are all in `BankStatementImportRepositoryIntegrationTests` and are caused by `Testcontainers.PostgreSql` being unable to reach a Docker daemon in this sandbox (`System.ArgumentException: Docker is either not running or misconfigured`) — a pre-existing environment limitation unrelated to this change, not a regression.
