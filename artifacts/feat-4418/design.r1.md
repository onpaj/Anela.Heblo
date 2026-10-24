# Design: Culture-invariant AmountClassificationRule

## Component Design
No new UI. The frontend `RuleForm.tsx` renders pattern help text and placeholder from the rule type's `description` returned by the API (`useClassificationRuleTypes`), so the "dot is the decimal separator" hint (spec FR-3) is delivered by changing `AmountClassificationRule.Description` only; no frontend change.

`AmountClassificationRule`:
- `Description` => `"Porovnání celkové částky faktury (>=, <=, >, <, =); desetinná tečka, např. >=1000.50"`.
- private static `TryParseAmount(string s, out decimal v)` => `decimal.TryParse(s, AllowLeadingSign | AllowDecimalPoint | AllowLeadingWhite | AllowTrailingWhite, CultureInfo.InvariantCulture, out v)`.
- `Evaluate` uses the helper in all six branches; operator order and try/catch unchanged.

## Data Schemas
None. No DB, API shape, or generated-client changes (Description is an existing string field).
