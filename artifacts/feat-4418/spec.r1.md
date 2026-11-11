# Specification: Culture-invariant decimal parsing in AmountClassificationRule

## Summary
`AmountClassificationRule.Evaluate` parses the threshold in a rule pattern with `decimal.TryParse` using the current thread culture. On a Czech-locale host a pattern such as `>=1000.50` fails to parse and the rule silently never matches. Parsing must be culture-invariant (dot decimal separator).

## Background
Rule patterns are stored in the DB and evaluated by a silent classification job. Culture-dependent parsing makes rule behaviour environment-dependent and invisible. Source: `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Rules/AmountClassificationRule.cs` (six `TryParse` calls: `>=`, `<=`, `>`, `<`, `=`, bare literal).

## Functional Requirements

### FR-1: Invariant parsing
All `decimal.TryParse` calls in the rule use `NumberStyles.Number` and `CultureInfo.InvariantCulture`.
**Acceptance criteria:**
- `>=1000.50` matches an invoice of 1000.50 regardless of `CurrentCulture` (e.g. `cs-CZ`).
- Every operator (`>=`, `<=`, `>`, `<`, `=`, bare) is covered by a test run under `cs-CZ`.
- Existing integer-pattern tests still pass.

### FR-2: Comma input behaviour
Under invariant parsing `1000,50` is not a valid decimal (NumberStyles.Number allows comma as thousands separator, so it would parse as 100050). Decision: use `NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint` (no thousands separator) so a comma pattern yields `false` rather than a silently wrong value.
**Acceptance criteria:**
- Pattern `>=1000,50` returns false.

### FR-3: UI hint
Document in the rule-editing UI that dot is the decimal separator (if an AMOUNT pattern input exists).
**Acceptance criteria:**
- Helper text/placeholder for the AMOUNT rule states dot as decimal separator.

## Non-Functional Requirements
### NFR-1: Performance
No measurable change.
### NFR-2: Security
None; no new inputs.

## Data Model
No change. Existing stored patterns with dot separators become valid on all hosts.

## API / Interface Design
No API change. `IClassificationRule.Evaluate` signature unchanged.

## Dependencies
`System.Globalization`. Process doc `docs/processes/feed-invoice-classification.md` / `module-invoice-classification.md` should be updated (bump `verified_at`, note invariant parsing) per CLAUDE.md.

## Out of Scope
Other rules, changing pattern grammar, migrating stored patterns.

## Open Questions
None.

## Status: COMPLETE
