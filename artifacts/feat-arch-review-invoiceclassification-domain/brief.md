## Module
InvoiceClassification

## Finding
Two Domain-layer types named with the `Dto` suffix are exposed directly as properties of Application-layer response classes, meaning the API contract is shaped by external-system data models:

- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/AccountingTemplateDto.cs` is used directly in `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/GetAccountingTemplates/GetAccountingTemplatesResponse.cs` (line 7: `public List<AccountingTemplateDto> Templates { get; set; }`)
- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ReceivedInvoiceDto.cs` is used directly in `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/GetInvoiceDetails/GetInvoiceDetailsResponse.cs` (line 7: `public ReceivedInvoiceDto? Invoice { get; set; }`)

Both types are mapped from the Flexi/ABRA external service in the Adapters layer (`FlexiInvoiceClassificationsClient`, `FlexiReceivedInvoicesClient`). Their shape is driven by what FlexiBee SDK returns, not by what the frontend needs.

## Why it matters
The guidelines state that DTO objects for API live in `contracts/` of the specific module, never in the Domain layer. Having Domain types named `*Dto` violates this naming convention and mixes concerns: Domain should contain entities and value objects, not transfer objects shaped by external APIs. Exposing these types directly through Application responses couples the API contract to the Flexi external service data shape — if Flexi adds or renames a field, the API contract changes with no opportunity to filter or rename.

## Suggested fix
1. Move `AccountingTemplateDto` and `ReceivedInvoiceDto` / `ReceivedInvoiceItemDto` to `Application/Features/InvoiceClassification/Contracts/`, or rename them to proper domain value objects (e.g. `ReceivedInvoice`) without the `Dto` suffix if they are genuinely domain types.
2. If the intent is to expose them as API DTOs, add a dedicated mapping step in each handler (AutoMapper profile already exists in `InvoiceClassificationMappingProfile.cs`) rather than passing the Domain types through.

---
_Filed by daily arch-review routine on 2026-05-25._