# Specification: Regex timeout in InvoiceClassification rules

## Summary
Three classification rules evaluate user-supplied regex patterns without a match timeout, so a pathological pattern can hang the hourly classification job. Add a bounded timeout and treat a timeout as a non-match.

## Background
`CompanyNameClassificationRule`, `DescriptionClassificationRule` and `ItemDescriptionClassificationRule` (in `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Rules/`) call `Regex.IsMatch(..., RegexOptions.IgnoreCase)`. Only `ArgumentException` (invalid syntax) is caught, falling back to a case-insensitive `Contains`. Catastrophic backtracking (e.g. `(a+)+$`) never throws and blocks the thread (ReDoS).

## Functional Requirements

### FR-1: Match timeout on all three regex rules
Every `Regex.IsMatch` call in the three rules passes a `matchTimeout` of 200 ms.
**Acceptance criteria:**
- A pathological pattern against a long adversarial input returns within a small multiple of the timeout.
- Normal patterns behave exactly as before (case-insensitive match).

### FR-2: Timeout is a non-match
`RegexMatchTimeoutException` is caught and `Evaluate` returns `false`; it must not fall through to the `Contains` fallback and must not propagate.
**Acceptance criteria:**
- Evaluate returns false on timeout for each of the three rules.
- For ItemDescription, a timeout on one item does not abort evaluation of remaining items (item is treated as non-match, others still checked).

### FR-3: Existing behavior preserved
Invalid-syntax patterns still fall back to `Contains(..., OrdinalIgnoreCase)`; null/whitespace guards unchanged.

### FR-4: Shared timeout constant (optional dedupe)
The timeout value is defined once rather than copy-pasted in three files.

## Non-Functional Requirements

### NFR-1: Performance
No measurable overhead for non-pathological patterns; worst case per (rule, field) evaluation bounded by 200 ms.

### NFR-2: Security
Mitigates ReDoS from user-entered patterns stored in the database.

## Data Model
None. No migration.

## API / Interface Design
No change to `IClassificationRule` or any API/DTO.

## Dependencies
`System.Text.RegularExpressions` only.

## Out of Scope
- Validating patterns at rule create/update time.
- Logging of timeouts (Domain rules have no logger; issue says "log if needed").
- Changing the AmountClassificationRule / VatClassificationRule.
- Using `RegexOptions.NonBacktracking`.

## Open Questions
None.

## Status: COMPLETE
