# Architecture Review: Culture-invariant AmountClassificationRule

## Skip Design: false
A small UI hint may be needed only if the frontend has an AMOUNT pattern input; the designer will confirm and otherwise treat as backend-only.

## Architectural Fit Assessment
Isolated domain-rule fix in `Anela.Heblo.Domain/Features/InvoiceClassification/Rules`. No cross-module or contract impact. Registered in `InvoiceClassificationModule.cs` unchanged.

## Proposed Architecture

### Component Overview
`AmountClassificationRule` only. Refactor the six duplicated parses into one private helper `TryParseAmount(ReadOnlySpan<char>/string, out decimal)` using a static `NumberStyles` and `CultureInfo.InvariantCulture`.

### Key Design Decisions

#### Decision 1: NumberStyles
**Options considered:** `NumberStyles.Number` (issue suggestion; allows thousands comma, so `1000,50` parses as 100050) vs `AllowLeadingSign | AllowDecimalPoint`.
**Chosen approach:** `AllowLeadingSign | AllowDecimalPoint`; also trim whitespace from the operand to preserve prior tolerance (`>= 100`): add `AllowLeadingWhite | AllowTrailingWhite`.
**Rationale:** Avoids silently wrong thresholds from comma input.

#### Decision 2: Keep existing try/catch and operator order
Behaviour otherwise unchanged (check `>=`/`<=` before `>`/`<`).

## Implementation Guidance
### Directory / Module Structure
Edit `AmountClassificationRule.cs`; extend `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/Rules/AmountClassificationRuleTests.cs`.
### Interfaces and Contracts
`IClassificationRule` unchanged.
### Data Flow
Pattern string -> operator detection -> invariant decimal parse -> compare to `invoice.TotalAmount`.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Existing stored patterns with comma decimals that worked on cs-CZ hosts now stop matching | Low-Medium | Dot is the documented/stored form per issue; mention in PR; optionally query DB for AMOUNT patterns containing commas |
| Tests passing only because test host culture is invariant | Medium | Tests set `CultureInfo.CurrentCulture` to `cs-CZ` explicitly and restore it |

## Specification Amendments
None beyond spec FR-2 choice of NumberStyles.

## Prerequisites
None.
