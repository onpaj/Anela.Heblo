## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- None

## Notes
AutoMapper scans the whole `Anela.Heblo.Application` assembly, so `InvoiceClassificationMappingProfile` (already existing, pre-dating this branch) is auto-registered; no DI change needed. Confirmed the profile's explicit mappings for `InvoiceId ← AbraInvoiceId` and null-safe `RuleName ← ClassificationRule?.Name` exactly match the removed manual projection, and all other DTO properties match domain property names 1:1 for AutoMapper's convention-based mapping. New tests cover both the rule-present and rule-null branches. Handler change mirrors the sibling `GetClassificationRulesHandler` pattern precisely.
