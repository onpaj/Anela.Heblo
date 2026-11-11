## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Rules/AmountClassificationRule.cs:48` -- TryParseAmount additionally allows leading/trailing whitespace (beyond the spec's styles); harmless and covered by a test, but a small deviation from FR-2's literal style list.
- FR-3 (UI hint): satisfied via the rule Description string, which the rule editor displays; no separate UI change was made.

Verified: all six TryParse calls use the invariant culture; comma pattern returns false; cs-CZ tests cover every operator; 44 AmountClassificationRule tests pass; process docs updated with verified_at bump.
