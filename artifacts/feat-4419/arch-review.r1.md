# Architecture Review: Rename VatClassificationRule / CompanyVat to IČO naming

## Skip Design: true
Backend refactor, no UI components. Frontend only receives a regenerated API client.

## Architectural Fit Assessment
Pure rename inside the InvoiceClassification vertical slice (Domain, Application contracts, Flexi adapter, DI module, tests). No layering change. Verified by grep: touch points are `Rules/VatClassificationRule.cs`, `ReceivedInvoice.cs`, `ReceivedInvoiceDto.cs`, `FlexiReceivedInvoiceMappingProfile.cs`, `InvoiceClassificationModule.cs`, three test files, and `docs/processes/feed-invoice-classification.md`. Rule type is persisted by string identifier `"ICO"` (`ClassificationRule.RuleTypeIdentifier`), not by class name, so no migration is needed.

## Proposed Architecture

### Component Overview
Flexi `ic` -> `FlexiReceivedInvoiceMappingProfile` -> `ReceivedInvoice.CompanyIco` -> `IcoClassificationRule.Evaluate` (and AutoMapper -> `ReceivedInvoiceDto.CompanyIco` -> API -> generated TS client).

### Key Design Decisions

#### Decision 1: Naming
**Options considered:** `Ico*` vs `CompanyRegistrationNumber*`.
**Chosen approach:** `IcoClassificationRule` and `CompanyIco`.
**Rationale:** Matches the existing `Identifier = "ICO"`, the UI label and Czech domain terminology used elsewhere (`SupplierVatId` is the DIČ counterpart).

#### Decision 2: Rename the DTO property too
**Options considered:** keep `CompanyVat` on the DTO for wire compatibility vs rename.
**Chosen approach:** Rename. AutoMapper maps by convention so the Application mapping profile stays valid.
**Rationale:** Leaving the misleading name on the API surface defeats the purpose. No frontend component uses `companyVat` (grep of `frontend/src` finds only the generated client). MCP tools do not expose the DTO (grep of backend finds no other users).

## Implementation Guidance

### Directory / Module Structure
`git mv` the rule file and its test file to preserve history. No new files.

### Interfaces and Contracts
`IClassificationRule` unchanged. `IcoClassificationRule.Identifier == "ICO"`.

### Data Flow
Unchanged.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Generated TS client out of date / committed stale | Low | Regenerate via build (`docs/development/api-client-generation.md`); `npm run build` + lint |
| Missed reference (reflection, string-based AutoMapper member names) | Low | Grep for `CompanyVat`/`VatClassificationRule` across `backend/`; build + tests |
| Process-docs CI gate fails | Low | Update `feed-invoice-classification.md` and run `scripts/process-docs/check.py index` |

## Specification Amendments
None.

## Prerequisites
None. No migrations.
