## Module / File
`backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Rules/`
- `AmountClassificationRule.cs` (0%, 27 lines)
- `CompanyNameClassificationRule.cs` (0%, 12 lines)
- `DescriptionClassificationRule.cs` (0%, 12 lines)
- `ItemDescriptionClassificationRule.cs` (0%, 17 lines)

Also related: `RuleEvaluationEngine.cs` (23.5%, 17 lines)

## Coverage
All four rule files: 0% line coverage. RuleEvaluationEngine: 23.5%. Filter threshold: 60%.

## What's not tested
**AmountClassificationRule**: evaluates six comparison operators (>=, <=, >, <, =, bare literal) against `TotalAmount`. Branches for each operator are independent and none are covered. Edge: empty pattern returns false.

**CompanyNameClassificationRule** and **DescriptionClassificationRule**: regex match with fallback to plain-text `Contains` when the pattern is an invalid regex. The `ArgumentException` fallback path is never exercised.

**ItemDescriptionClassificationRule**: iterates all invoice items for any match; empty items collection or all-empty item names should return false.

**RuleEvaluationEngine.FindMatchingRule**: ordering by `rule.Order` among active rules, returning the first match — the case where no rule matches (returns null) is untested.

## Why it matters
These rules are the core of automated invoice classification. A broken operator in AmountClassificationRule silently misclassifies invoices by amount. An invalid-regex fallback that stops working would change match behavior without an error. The `null` return from RuleEvaluationEngine (no match) controls whether an invoice enters manual review — if broken, all invoices skip manual review or are wrongly classified.

## Suggested approach
Domain-layer unit tests (no mocks needed — pure logic):
1. AmountClassificationRule: one test per operator (>=, <=, >, <, =, bare), plus boundary values and empty/whitespace pattern
2. CompanyName/Description rules: valid regex hit, valid regex miss, invalid regex falls back to Contains, null/empty inputs
3. ItemDescriptionClassificationRule: match on first item, match on second item only, no items match, empty items list
4. RuleEvaluationEngine: ordered rules where first active match wins; rules ordered by `Order`; no matching rule returns null
Effort: ~1.5 hours

---
_Filed by weekly coverage-gap routine on 2026-06-14. Based on CI run #27416879267 (3a6b7f99ee715caf82fc0efa17de5a5ede7b46fb)._