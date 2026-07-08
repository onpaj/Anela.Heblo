# Specification: Move RuleName enrichment into InvoiceClassificationService

## Summary
`ClassifyInvoicesHandler` currently injects `IClassificationRuleRepository` solely to re-look up a rule's name when `InvoiceClassificationService` reports a classification error tied to a known rule. This is an architecture cleanup: add a `RuleName` field to `InvoiceClassificationResult`, populate it inside `InvoiceClassificationService.ClassifyInvoiceAsync` where the matched rule is already in memory, and remove the now-unnecessary `IClassificationRuleRepository` dependency (and its per-error DB round trip) from the handler.

## Background
`ClassifyInvoicesHandler` (`backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/ClassifyInvoices/ClassifyInvoicesHandler.cs`) depends on `IInvoiceClassificationService` for the actual classification logic. It additionally depends on `IClassificationRuleRepository`, used in exactly one place (lines 97–104): when `ClassifyInvoiceAsync` returns `ClassificationResult.Error` with a non-null `RuleId`, the handler calls `_ruleRepository.GetByIdAsync(result.RuleId.Value)` to fetch the rule's `Name` and build a richer error message (`"Invoice {InvoiceNumber} (Rule: {rule.Name}): {ErrorMessage}"`).

`InvoiceClassificationService.ClassifyInvoiceAsync` (`backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/InvoiceClassificationService.cs`) already loads the full `ClassificationRule` object (`matchedRule`, including `Name`) via `_ruleRepository.GetActiveRulesOrderedAsync()` and rule matching, before returning its result — including in the `Error` branch (lines 65–78) where `matchedRule` is in scope but only `matchedRule.Id` is copied onto the result via `RuleId`.

This means the handler reaches into the same data layer the service already owns, purely to re-fetch data the service had a moment ago, adding an avoidable DB round trip per errored invoice and an extra mocked dependency in handler tests. The fix threads `RuleName` through the existing result object instead of re-querying.

## Functional Requirements

### FR-1: Add `RuleName` to `InvoiceClassificationResult`
Add a `public string? RuleName { get; set; }` property to `InvoiceClassificationResult` (`backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/InvoiceClassificationResult.cs`), alongside the existing `RuleId`.

**Acceptance criteria:**
- `InvoiceClassificationResult` exposes a nullable `RuleName` string property.
- No other properties on `InvoiceClassificationResult` are changed.

### FR-2: Populate `RuleName` in `InvoiceClassificationService`
In `InvoiceClassificationService.ClassifyInvoiceAsync`, set `RuleName = matchedRule.Name` on every `InvoiceClassificationResult` constructed after a rule has matched (`matchedRule != null`) — i.e. both the `Success` branch (lines 57–63) and the `Error` branch for a failed ABRA update (lines 71–77). The `ManualReviewRequired` branch (no matched rule, lines 43–46) and the outer-exception `Error` branch (lines 88–92, no matched rule in scope) continue to leave `RuleName` (and `RuleId`) unset/null, matching current `RuleId` behavior.

**Acceptance criteria:**
- When classification succeeds, the returned result's `RuleName` equals `matchedRule.Name`.
- When classification fails because the ABRA update did not succeed (matched rule known), the returned result's `RuleName` equals `matchedRule.Name`.
- When no rule matches (manual review required), `RuleName` is null.
- When an unhandled exception occurs before/during rule matching, `RuleName` is null.
- No change to `IRuleEvaluationEngine`, `IClassificationRuleRepository`, or `ClassificationHistory` recording behavior.

### FR-3: Handler builds the enriched error message from the result directly
In `ClassifyInvoicesHandler.Handle`, replace the `_ruleRepository.GetByIdAsync(result.RuleId.Value)` lookup (lines 97–104) with a direct read of `result.RuleName`. If `result.RuleName` is non-null/non-empty, build the message as `"Invoice {invoice.InvoiceNumber} (Rule: {result.RuleName}): {result.ErrorMessage}"`; otherwise keep the existing unqualified message `"Invoice {invoice.InvoiceNumber}: {result.ErrorMessage}"`. No DB call is made from the handler.

**Acceptance criteria:**
- Given a service result with `Result = Error`, `RuleId` set, and `RuleName` set, the handler's error message includes `(Rule: {RuleName})`.
- Given a service result with `Result = Error` and `RuleName` null (e.g. exception path, no matched rule), the handler's error message has no `(Rule: ...)` segment — same as today's behavior when the repository lookup returns null.
- Behavior/format of the produced error message string is otherwise unchanged from the current implementation (only the data source changes, not the message shape).

### FR-4: Remove `IClassificationRuleRepository` from `ClassifyInvoicesHandler`
Remove the `IClassificationRuleRepository _ruleRepository` field, constructor parameter, and assignment from `ClassifyInvoicesHandler`. Remove the now-unused `using Anela.Heblo.Domain.Features.InvoiceClassification;` import only if nothing else in the file still needs it (`ReceivedInvoice`, `ClassificationResult` also live in that namespace, so the `using` itself likely stays — only the repository dependency is removed).

**Acceptance criteria:**
- `ClassifyInvoicesHandler`'s constructor takes 4 parameters: `IReceivedInvoicesClient`, `IInvoiceClassificationService`, `ICurrentUserService`, `ILogger<ClassifyInvoicesHandler>`.
- No reference to `IClassificationRuleRepository` remains in `ClassifyInvoicesHandler.cs`.
- DI registration for `ClassifyInvoicesHandler` (MediatR auto-registration; no explicit constructor wiring expected in `InvoiceClassificationModule.cs`) continues to resolve without changes, since `IClassificationRuleRepository` is still registered for other consumers (`UpdateClassificationRuleHandler`, `ReorderClassificationRulesHandler`, `GetClassificationRulesHandler`, `DeleteClassificationRuleHandler`, `CreateClassificationRuleHandler`, and `InvoiceClassificationService` itself).

### FR-5: Update existing tests for the new constructor signature and behavior
`backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassifyInvoicesHandlerTests.cs` constructs `ClassifyInvoicesHandler` with a `Mock<IClassificationRuleRepository>` as the third constructor argument (lines 16, 25, 33–38). This must be removed to match the new constructor. Existing tests set up `InvoiceClassificationResult` without `RuleName`/`RuleId` (e.g. lines 63, 97, 126, 155, 184), which remains valid since the new field is optional and defaults to null — no behavioral change to those assertions is expected.

**Acceptance criteria:**
- `ClassifyInvoicesHandlerTests` compiles and all existing tests pass against the new 4-parameter constructor, with no `Mock<IClassificationRuleRepository>` remaining in the file.
- A new/updated test covers FR-3: given an `Error` result with `RuleId` and `RuleName` populated, `response.ErrorMessages` contains a message with the `(Rule: {RuleName})` segment, without any repository mock being invoked.
- A new/updated test (or existing one, if already covering this) confirms the unqualified message format is preserved when `RuleName` is null.
- `InvoiceClassificationServiceTests.cs` gains or updates assertions confirming `result.RuleName` is populated in the `Success` and rule-matched-`Error` cases (mirroring existing `result.RuleId.Should().Be(...)` assertions at lines 148 and 228) and remains null in the no-rule-matched and exception cases (mirroring lines 69 and 288).

## Non-Functional Requirements

### NFR-1: Performance
Eliminates one `IClassificationRuleRepository.GetByIdAsync` database round trip per invoice that fails classification with a known rule, inside the `ClassifyInvoicesHandler.Handle` loop. No new I/O is introduced — `RuleName` is sourced from data already fetched by `InvoiceClassificationService` during rule evaluation.

### NFR-2: Security
No change. No new data exposure: `RuleName` is already readable by any caller with access to `IClassificationRuleRepository` (used elsewhere in the module, e.g. `GetClassificationRulesHandler`), and is already surfaced indirectly today via the handler's repository lookup. This change does not alter authorization or data sensitivity.

## Data Model
`InvoiceClassificationResult` (application-layer DTO, not persisted) gains one field:

| Property | Type | Notes |
|---|---|---|
| `RuleName` | `string?` | Name of the matched `ClassificationRule`, mirrors `RuleId`; null when no rule matched. |

No changes to persisted entities (`ClassificationRule`, `ClassificationHistory`) or to the `IClassificationRuleRepository` interface.

## API / Interface Design
No public API (HTTP endpoint / DTO contract) changes. This is an internal application-layer refactor:
- `IInvoiceClassificationService.ClassifyInvoiceAsync` signature is unchanged; only the returned `InvoiceClassificationResult`'s shape gains a field.
- `ClassifyInvoicesHandler` constructor signature changes (drops `IClassificationRuleRepository` parameter) — this is an internal DI-resolved type, not exposed externally, so no OpenAPI/TypeScript client regeneration is triggered.
- `ClassifyInvoicesRequest` / `ClassifyInvoicesResponse` (the MediatR request/response and the controller contract) are unchanged.

## Dependencies
- No new external dependencies.
- Depends on existing `ClassificationRule.Name` (already present, immutable after construction/`Update`).
- `IClassificationRuleRepository` remains injected in `InvoiceClassificationService` and in the other `InvoiceClassification` use-case handlers (`UpdateClassificationRuleHandler`, `ReorderClassificationRulesHandler`, `GetClassificationRulesHandler`, `DeleteClassificationRuleHandler`, `CreateClassificationRuleHandler`) — only `ClassifyInvoicesHandler`'s dependency is removed.

## Out of Scope
- Any change to `ClassificationResult` enum values or overall classification logic/flow.
- Any change to `IClassificationRuleRepository` or its other consumers.
- Any change to `ClassificationHistory` persistence or the `RecordClassificationHistory` method.
- Any change to the HTTP-facing `ClassifyInvoicesRequest`/`ClassifyInvoicesResponse` contracts or frontend.
- Broader refactors of error-message formatting beyond substituting the data source for the rule name.

## Open Questions
None.

## Status: COMPLETE
