# Design: Move RuleName enrichment into InvoiceClassificationService

## Component Design

No new components. This is a contract-widening change on one existing seam, plus removal of a
now-unnecessary dependency edge.

### `InvoiceClassificationResult` (`Services/InvoiceClassificationResult.cs`)
- Application-layer result DTO returned by `IInvoiceClassificationService.ClassifyInvoiceAsync`.
- Gains one settable auto-property, `RuleName`, alongside the existing `RuleId`. Populated via
  object initializer at all construction sites (no constructor exists for this type today, and
  none is introduced).

### `InvoiceClassificationService` (`Services/InvoiceClassificationService.cs`)
- `ClassifyInvoiceAsync` responsibility is unchanged: resolve `matchedRule`, evaluate it, return an
  `InvoiceClassificationResult`.
- Additionally sets `RuleName = matchedRule.Name` in the two branches where `matchedRule` is
  already in scope (`Success`, and the ABRA-update-failed `Error` branch). The `ManualReviewRequired`
  branch and the outer-exception `Error` branch continue to leave `RuleName` null, exactly mirroring
  current `RuleId` behavior — no new branching logic.
- `IInvoiceClassificationService.ClassifyInvoiceAsync` method signature is unchanged
  (`Task<InvoiceClassificationResult>`).

### `ClassifyInvoicesHandler` (`UseCases/ClassifyInvoices/ClassifyInvoicesHandler.cs`)
- Loses its `IClassificationRuleRepository` dependency entirely (field, constructor parameter,
  assignment). Constructor becomes 4 params: `IReceivedInvoicesClient`,
  `IInvoiceClassificationService`, `ICurrentUserService`, `ILogger<ClassifyInvoicesHandler>`.
- Error-message construction switches from an async repository lookup
  (`await _ruleRepository.GetByIdAsync(result.RuleId.Value)`) to a synchronous read of
  `result.RuleName`. Message format is unchanged: `"Invoice {InvoiceNumber} (Rule: {RuleName}): {ErrorMessage}"`
  when `RuleName` is non-empty, else `"Invoice {InvoiceNumber}: {ErrorMessage}"`.
- No other responsibility changes; `ClassifyInvoicesRequest`/`ClassifyInvoicesResponse` contracts
  untouched.

### `IClassificationRuleRepository`
- Untouched. Remains registered in `InvoiceClassificationModule.cs` and consumed by
  `InvoiceClassificationService` and the five rule-management handlers (Create/Update/Delete/Reorder/Get).
  Only the edge from `ClassifyInvoicesHandler` is removed.

## Data Schemas

### `InvoiceClassificationResult` (in-memory application DTO, not persisted)

| Property | Type | Notes |
|---|---|---|
| `Result` | `ClassificationResult` (enum) | unchanged |
| `RuleId` | `Guid?` | unchanged |
| `RuleName` | `string?` | **new** — name of the matched `ClassificationRule`; mirrors `RuleId`, null when no rule matched (manual-review or pre-match exception paths) |
| `AccountingTemplateCode` | `string?` | unchanged |
| `Department` | `string?` | unchanged |
| `ErrorMessage` | `string?` | unchanged |

No changes to persisted entities (`ClassificationRule`, `ClassificationHistory`), to
`IClassificationRuleRepository`'s interface, or to any HTTP-facing request/response contract
(`ClassifyInvoicesRequest`/`ClassifyInvoicesResponse`). No OpenAPI/TypeScript client regeneration
is triggered.
