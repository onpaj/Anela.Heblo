# Specification: Rename VatClassificationRule / CompanyVat to IČO naming

## Summary
`VatClassificationRule` and `ReceivedInvoice.CompanyVat` both hold and compare the supplier IČO (Flexi `ic`), not a VAT ID/DIČ. Rename them so names match behaviour and a future DIČ rule (using `SupplierVatId`) is not confused with the IČO rule. Pure refactor, no behaviour change.

## Background
Issue #4419 (arch-review, InvoiceClassification). The rule's `Identifier` is already `"ICO"`; only the C# names mislead. `docs/processes/feed-invoice-classification.md` lists this as a known quirk (line ~151) and must be updated in the same PR (project rule).

## Functional Requirements

### FR-1: Rename the rule class
Rename `VatClassificationRule` to `IcoClassificationRule` (file `Rules/IcoClassificationRule.cs`), update DI registration in `InvoiceClassificationModule.cs`.
**Acceptance criteria:**
- No `VatClassificationRule` symbol remains in `backend/`.
- `Identifier` stays `"ICO"`, `DisplayName` and `Description` unchanged (persisted `RuleTypeIdentifier` values keep working; no DB migration).

### FR-2: Rename the domain property
Rename `ReceivedInvoice.CompanyVat` to `CompanyIco`; fix the XML doc on `SupplierVatId` to reference `CompanyIco`.
**Acceptance criteria:**
- No `CompanyVat` symbol remains in `backend/`.
- `FlexiReceivedInvoiceMappingProfile` maps `src.CompanyId` to `CompanyIco`.

### FR-3: Rename the DTO property
Rename `ReceivedInvoiceDto.CompanyVat` to `CompanyIco` (AutoMapper maps by name, so the mapping profile needs no explicit change beyond verification).
**Acceptance criteria:**
- JSON field becomes `companyIco`; the generated TypeScript client regenerates with `companyIco`; frontend build and lint pass (no hand-written frontend code references `companyVat`).

### FR-4: Tests and docs
- Rename `VatClassificationRuleTests` to `IcoClassificationRuleTests`, update test names/fixtures (`InvoiceClassificationFixtures`, `InvoiceClassificationMappingProfileTests`).
- Update `docs/processes/feed-invoice-classification.md`: replace `CompanyVat` mentions, remove the "class is called VatClassificationRule" quirk (or reword), bump `verified_at`; run `python3 scripts/process-docs/check.py index`.
**Acceptance criteria:**
- `dotnet build`, `dotnet format`, InvoiceClassification tests pass; process-docs check passes.

## Non-Functional Requirements
### NFR-1: Performance
No change.
### NFR-2: Security
No change.
### NFR-3: Compatibility
No DB schema change; stored rule identifier `"ICO"` unchanged. The DTO JSON rename is a contract change for `GetInvoiceDetails`; the only known consumer is the generated frontend client, which does not use the field in components.

## Data Model
`ReceivedInvoice.CompanyVat` becomes `CompanyIco` (string, Flexi `ic`). `SupplierVatId` (Flexi `dic`) unchanged. `ClassificationRule` entity and persistence untouched.

## API / Interface Design
`GetInvoiceDetailsResponse.Invoice.companyVat` becomes `companyIco`. No endpoint changes.

## Dependencies
AutoMapper profiles (Application and Flexi adapter), NSwag-generated TS client (regenerated at build), process doc.

## Out of Scope
- Adding a DIČ rule.
- Renaming `FlexiReceivedInvoiceDto.CompanyId` or other Flexi-side names.
- Changing rule `Identifier`/DisplayName.
- Historical docs under `docs/superpowers/` and `artifacts/` (frozen records).

## Open Questions
None.

## Status: COMPLETE
