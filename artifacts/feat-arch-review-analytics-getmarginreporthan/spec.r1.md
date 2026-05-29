# Specification: Remove Duplicate Validation from Analytics Margin Report Handlers

## Summary
Two analytics handlers (`GetMarginReportHandler` and `GetProductMarginAnalysisHandler`) contain manual `if`-check validation that duplicates rules already declared in their corresponding FluentValidation classes registered in the MediatR pipeline. This spec covers removing the duplicate handler-level validation, ensuring the FluentValidation classes remain the single source of truth, and migrating impacted tests to either use the pipeline or test the validators directly.

## Background
A daily architecture review (2026-05-28) flagged dead-code validation in `Anela.Heblo.Application.Features.Analytics`:

- `GetMarginReportHandler.Handle()` lines 33–52 re-checks `StartDate <= EndDate`, `TotalDays <= MAX_REPORT_PERIOD_DAYS`, and `TotalDays >= MIN_REPORT_PERIOD_DAYS`. The handler's own comment (line 33) admits the checks exist *"for backward compatibility with tests"*.
- `GetProductMarginAnalysisHandler.Handle()` lines 30–39 carries the equivalent duplication against `GetProductMarginAnalysisRequestValidator`.

Both validators are registered in `AnalyticsModule` and wired into the MediatR pipeline. For any request flowing through MediatR, validation runs *before* the handler, making the in-handler checks unreachable for invalid input.

Problems this creates:
1. **Drift risk** — if `AnalyticsConstants.MAX_REPORT_PERIOD_DAYS`/`MIN_REPORT_PERIOD_DAYS` semantics change, two code sites must move in lockstep with no compile-time link.
2. **Misleading intent** — a reader of the handler may infer the validator isn't pipelined.
3. **Test coupling** — handler unit tests bypass the pipeline and re-test validation rules instead of business logic, blocking the handler from being simplified.

The codebase convention (per `docs/architecture/development_guidelines.md` and existing handlers) is that input validation belongs exclusively in FluentValidation classes consumed by the MediatR validation behavior.

## Functional Requirements

### FR-1: Remove duplicate validation from `GetMarginReportHandler`
Delete lines 33–52 of `GetMarginReportHandler.Handle()` (the three manual `if`-checks producing `ErrorCodes.InvalidDateRange` and `ErrorCodes.InvalidReportPeriod` results, plus the `totalDays` local used only by those checks). The handler must rely solely on `GetMarginReportRequestValidator` running in the MediatR pipeline for these rules.

**Acceptance criteria:**
- The `if (request.StartDate > request.EndDate)` block is removed.
- Both `totalDays > MAX_REPORT_PERIOD_DAYS` and `totalDays < MIN_REPORT_PERIOD_DAYS` blocks are removed.
- Any local variable (e.g. `totalDays`) that becomes unused after removal is also deleted.
- The `// Basic input validation (kept here for backward compatibility with tests)` comment is removed.
- The handler still returns successfully for valid inputs with no behavior change for valid requests (verified by passing handler tests with valid fixtures).
- No reference to `ErrorCodes.InvalidDateRange` or `ErrorCodes.InvalidReportPeriod` remains in `GetMarginReportHandler`.

### FR-2: Remove duplicate validation from `GetProductMarginAnalysisHandler`
Apply the equivalent removal in `GetProductMarginAnalysisHandler.Handle()` lines 30–39.

**Acceptance criteria:**
- Manual `if`-checks that duplicate rules in `GetProductMarginAnalysisRequestValidator` are removed.
- Any locals (e.g. `totalDays`) that become unused after removal are deleted.
- Handler returns successfully for valid inputs with no behavior change for valid requests.
- No reference to validator-owned `ErrorCodes` remains in this handler.

### FR-3: Preserve external behavior through the MediatR pipeline
For any request flowing through MediatR (the production code path and integration tests), the validation behavior must continue to reject invalid input with the same `ErrorCodes` (`InvalidDateRange`, `InvalidReportPeriod`) the handler used to return.

**Acceptance criteria:**
- `GetMarginReportRequestValidator` and `GetProductMarginAnalysisRequestValidator` already produce these `ErrorCodes` (verify by reading the validators). If any code is needed to map FluentValidation failures to the existing `ErrorCodes` shape, that mapping must remain functional after the change.
- An end-to-end / handler-via-mediator test demonstrates that an invalid request (e.g. `StartDate > EndDate`) still yields the same `ErrorCodes.InvalidDateRange` response surface that callers see today.
- The HTTP-facing controller(s) consuming these requests show no behavior change for valid or invalid inputs.

### FR-4: Update unit tests that directly invoke `Handle()`
Any test that constructs the handler and calls `Handle()` directly (bypassing MediatR) and relies on the removed validation must be migrated. Two acceptable migration paths:

(a) **Pass valid inputs** — rewrite the test so the handler call uses valid data, and the test focuses on the business-logic path it actually intends to cover.

(b) **Move to validator tests** — if the test's true intent was to verify the rule, move (or merge) the assertion into the corresponding validator unit-test class (`GetMarginReportRequestValidatorTests` / `GetProductMarginAnalysisRequestValidatorTests`, creating them if absent).

**Acceptance criteria:**
- After the change, no test in the solution invokes `GetMarginReportHandler.Handle()` or `GetProductMarginAnalysisHandler.Handle()` with intentionally invalid input and asserts on `ErrorCodes.InvalidDateRange` or `ErrorCodes.InvalidReportPeriod`.
- Validation-rule coverage (each of the three rules, per handler) is still asserted somewhere — either in validator unit tests or in pipeline/integration tests.
- All Analytics tests pass: `dotnet test` for the Analytics test project is green.
- No test is silently deleted: every migrated test either remains (with valid inputs / different intent) or has been re-homed to a validator test with equivalent or stronger assertions.

### FR-5: No drift in validation constants
Confirm both validators reference `AnalyticsConstants.MAX_REPORT_PERIOD_DAYS` and `AnalyticsConstants.MIN_REPORT_PERIOD_DAYS` (the same constants the handlers used). If a validator currently inlines a literal, update it to reference the constant so the single source of truth holds.

**Acceptance criteria:**
- `GetMarginReportRequestValidator` and `GetProductMarginAnalysisRequestValidator` reference `AnalyticsConstants` for both period bounds.
- A grep for the literal numeric values of those constants outside `AnalyticsConstants` returns no results inside the Analytics validators or handlers.

## Non-Functional Requirements

### NFR-1: Performance
Removing the redundant in-handler checks slightly shortens the hot path (a `TimeSpan` subtraction and three branches per request). No measurable regression expected; no performance test required.

### NFR-2: Security
No change to authn/authz, no change to data exposure. Invalid-input rejection remains gated by the FluentValidation pipeline as today, so the security posture is unchanged.

### NFR-3: Maintainability
The change reduces duplication and eliminates the documented "backward compatibility with tests" anti-pattern. After the change, a single grep for the validation rule lands in exactly one place per handler.

### NFR-4: Backwards compatibility (API contract)
Callers of the affected MediatR requests (controllers, MCP tools, internal callers) must observe no change in the response shape for either valid or invalid inputs. Error codes returned to API consumers for invalid input remain `InvalidDateRange` / `InvalidReportPeriod`.

## Data Model
No data-model changes. No EF Core migration. No DTO shape change.

Entities touched (code-only, no schema):
- `GetMarginReportRequest` / `GetMarginReportResponse` — unchanged.
- `GetProductMarginAnalysisRequest` / `GetProductMarginAnalysisResponse` — unchanged.
- `AnalyticsConstants` — unchanged, but verified as the canonical source of bounds.
- `ErrorCodes.InvalidDateRange`, `ErrorCodes.InvalidReportPeriod` — unchanged; ownership shifts wholly to the validators.

## API / Interface Design
No public-API surface changes.

Internal flow after the change:

```
HTTP request
  → Controller
    → MediatR Send(request)
      → ValidationBehavior runs IValidator<TRequest>
        → on failure: returns response with ErrorCodes.* (same as today)
        → on success: invokes Handler.Handle()
          → Handler executes business logic only (no input-validation if-blocks)
```

No new endpoints. No event/contract changes.

## Dependencies
- **FluentValidation** — already in use; this change only relies on existing infrastructure.
- **MediatR ValidationBehavior** — must already be registered in the pipeline for the affected requests. (Per the brief, it is; verify in `AnalyticsModule` / global MediatR configuration during implementation.)
- **Existing validators**: `GetMarginReportRequestValidator`, `GetProductMarginAnalysisRequestValidator`.
- **Existing constants**: `AnalyticsConstants.MAX_REPORT_PERIOD_DAYS`, `AnalyticsConstants.MIN_REPORT_PERIOD_DAYS`.

No new packages. No new infrastructure.

## Out of Scope
- Refactoring other Analytics handlers not flagged by the brief.
- Auditing the whole solution for the same pattern elsewhere (separate arch-review item).
- Changing `ErrorCodes` enum values, names, or numeric codes.
- Changing the response envelope or any DTO shape.
- Restructuring `AnalyticsModule` DI registration beyond what FR-5 requires.
- Adding new validation rules or relaxing existing ones.
- Changing how the MediatR ValidationBehavior surfaces failures to controllers.
- Updating documentation in `docs/` (unless a doc currently teaches the handler-level pattern; if so, fix it — but this is a stretch, not core scope).

## Open Questions
None.

## Status: COMPLETE