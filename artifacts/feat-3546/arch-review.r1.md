# Architecture Review: Move RuleName enrichment into InvoiceClassificationService

## Skip Design: true

## Architectural Fit Assessment

This is a textbook application of the module-boundary rules already codified in
`docs/architecture/development_guidelines.md`: "Direct access to another module's entities" and
"Shared repositories across modules" are listed as forbidden practices, and the guidance is explicit
that cross-cutting communication should go through a slice's own service abstraction, not by reaching
past it into the persistence layer. `ClassifyInvoicesHandler` currently violates the spirit of that
rule *within* the same module — it depends on `IClassificationRuleRepository` (owned conceptually by
`InvoiceClassificationService`) purely to re-fetch a `Name` that `InvoiceClassificationService` already
held moments earlier during `ClassifyInvoiceAsync`. The fix is the standard resolution: widen the
service's return contract (`InvoiceClassificationResult`) by one field so the data flows out through
the existing seam instead of a second, parallel path into the repository.

Verified against the real code (not assumed):
- `ClassifyInvoicesHandler.cs:13,20,26,97-104` — constructor takes 5 params including
  `IClassificationRuleRepository _ruleRepository`, used only at lines 97-104 to look up `rule.Name` for
  the error message.
- `InvoiceClassificationResult.cs` — currently `Result`, `RuleId`, `AccountingTemplateCode`,
  `Department`, `ErrorMessage`. No `RuleName`.
- `InvoiceClassificationService.ClassifyInvoiceAsync` (lines 28-94) — `matchedRule` (a full
  `ClassificationRule`, which has `.Name`) is in scope in the `Success` branch (57-63) and the
  ABRA-update-failed `Error` branch (71-77); it is *not* in scope in the no-rule `ManualReviewRequired`
  branch (43-46) or the outer-exception `Error` branch (88-92).
- `IClassificationRuleRepository` (Domain layer interface) is registered once in
  `InvoiceClassificationModule.cs` and consumed by `InvoiceClassificationService` plus five other
  `UseCases/*Handler.cs` classes that manage rules directly (Create/Update/Delete/Reorder/Get) — those
  are legitimate consumers and are explicitly out of scope here.

No new integration points, no new module, no new dependency direction — this only removes one.

## Proposed Architecture

### Component Overview

```
Before:
  ClassifyInvoicesHandler ──> IInvoiceClassificationService ──> IClassificationRuleRepository
           │
           └──────────────────────────────────────────────────> IClassificationRuleRepository  (extra edge, DB round-trip per error)

After:
  ClassifyInvoicesHandler ──> IInvoiceClassificationService ──> IClassificationRuleRepository
           (reads RuleName off the InvoiceClassificationResult the service already returns)
```

No new components. One edge removed from the handler; one field added to an existing DTO-like result
object that already crosses the handler/service seam.

### Key Design Decisions

#### Decision 1: Carry `RuleName` on `InvoiceClassificationResult` vs. a handler-side cache/lookup helper
**Options considered:**
1. Add `RuleName` to `InvoiceClassificationResult` (spec's approach).
2. Keep the repository dependency in the handler but cache rule lookups (e.g. a dictionary keyed by
   `RuleId`) to cut down repeated round trips within one `Handle` invocation.
3. Introduce a lightweight `IRuleNameLookupService` just for this.

**Chosen approach:** Option 1.

**Rationale:** The data the handler needs already exists, unpersisted, inside
`InvoiceClassificationService.ClassifyInvoiceAsync` at the exact point the result is constructed.
Threading it through the existing return type is strictly simpler than any caching layer and fully
eliminates the DB round trip rather than just amortizing it. Option 2 keeps the boundary violation the
finding is about. Option 3 adds a component for a one-field, one-call-site need — not warranted at this
scope.

## Implementation Guidance

### Directory / Module Structure
No new files. All changes are in-place edits within
`backend/src/Anela.Heblo.Application/Features/InvoiceClassification/`:
- `Services/InvoiceClassificationResult.cs`
- `Services/InvoiceClassificationService.cs`
- `UseCases/ClassifyInvoices/ClassifyInvoicesHandler.cs`
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassifyInvoicesHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/InvoiceClassificationServiceTests.cs`
  (path not directly verified in this pass — confirm exact file name/location before editing; spec
  references line numbers 148/228/69/288 that imply it already asserts on `RuleId` in the same
  branches).

### Interfaces and Contracts
- `InvoiceClassificationResult` gains `public string? RuleName { get; set; }`, positioned next to
  `RuleId` for readability. No constructor — this class uses object-initializer construction
  throughout the codebase (confirmed at all four `return new InvoiceClassificationResult { ... }`
  sites), so the new property must stay a settable auto-property, not a constructor parameter.
- `IInvoiceClassificationService.ClassifyInvoiceAsync` signature is unchanged (`Task<InvoiceClassificationResult>`).
- `ClassifyInvoicesHandler` constructor drops from 5 params to 4:
  `(IReceivedInvoicesClient, IInvoiceClassificationService, ICurrentUserService, ILogger<ClassifyInvoicesHandler>)`.
- `IClassificationRuleRepository` itself is untouched — it keeps its six methods and its five other
  consumers exactly as today.

### Data Flow
1. `ClassifyInvoicesHandler.Handle` calls `_classificationService.ClassifyInvoiceAsync(invoice, processedBy)` (unchanged call site).
2. Inside `InvoiceClassificationService.ClassifyInvoiceAsync`:
   - `matchedRule` is resolved (line 34) as today.
   - In the `Success` branch (57-63) and the ABRA-failure `Error` branch (71-77), set
     `RuleName = matchedRule.Name` alongside the existing `RuleId = matchedRule.Id`.
   - The `ManualReviewRequired` branch (43-46, no `matchedRule`) and the outer-exception `Error` branch
     (88-92, `matchedRule` out of scope) leave `RuleName` unset (null), mirroring current `RuleId`
     behavior exactly — no new null-handling logic needed anywhere.
3. Back in the handler's `Error` case (91-107): replace the `result.RuleId.HasValue` branch that calls
   `await _ruleRepository.GetByIdAsync(result.RuleId.Value)` with a direct
   `!string.IsNullOrEmpty(result.RuleName)` check, building the same `"Invoice {N} (Rule: {Name}): {Msg}"`
   string from `result.RuleName` — no `await`, no repository call, loop stays fully synchronous with
   respect to rule data.

This is a pure "widen the return value, shrink the constructor" change — no new async paths, no new
error states, no behavior change to `ClassificationHistory` recording (`RecordClassificationHistory`
already takes `ruleId`, not `ruleName`, and is untouched per spec's Out of Scope).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Missing one of the two `matchedRule`-in-scope branches when setting `RuleName`, leaving it null in the ABRA-failure case | Low | Spec's FR-2 acceptance criteria + a targeted unit test on `InvoiceClassificationServiceTests` for the error/matched-rule branch (mirrors existing `RuleId` assertion at line 228) catches this at compile/test time, not runtime |
| Test file compiles against old 5-arg constructor after the handler change lands out of order | Low | Handler and test changes should land in the same commit/PR; `dotnet build` will fail loudly (constructor arity mismatch) if missed, per CLAUDE.md's mandatory `dotnet build` validation step |
| Someone re-adds `IClassificationRuleRepository` to the handler later out of habit (e.g. copy-pasting another use-case handler) | Very low | No enforcement needed at this scope; if recurrence becomes a pattern, an architecture test analogous to `PersistenceModuleTests.AddPersistenceServices_RegistersNoRepositoryBindings` could assert `ClassifyInvoicesHandler`'s constructor arity, but that's over-engineering for a single current fix |

## Specification Amendments

None required — the spec's FR-1 through FR-5 already precisely match the verified shape of
`InvoiceClassificationResult`, `InvoiceClassificationService.ClassifyInvoiceAsync`, and
`ClassifyInvoicesHandler`. One clarification worth calling out for the implementer rather than a
change to the spec: `InvoiceClassificationResult` is populated exclusively via object initializers
(`new InvoiceClassificationResult { ... }`) at all four construction sites in
`InvoiceClassificationService.cs` — `RuleName` must be added as a plain auto-property (as FR-1
specifies), not routed through a constructor, or three of the four call sites will fail to compile.

## Prerequisites

None. No migration, no config, no new DI registration — `IClassificationRuleRepository` remains
registered exactly as-is in `InvoiceClassificationModule.cs` (still needed by
`InvoiceClassificationService` and five other handlers); only the handler's constructor parameter list
shrinks. Safe to implement immediately.
