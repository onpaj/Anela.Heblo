All 15 tasks completed. Here's the implementation summary:

---

## Implementation Complete: InvoiceClassification Domain DTO Separation

### What was done

**Step A (additive, no breaking change):**
- Created 3 Application contract DTOs in `Application/Features/InvoiceClassification/Contracts/`
- Extended `InvoiceClassificationMappingProfile` with 3 Domain→Contract maps
- Updated `GetAccountingTemplatesHandler` to inject `IMapper` and map before returning
- Updated `GetInvoiceDetailsHandler` with `IMapper` and explicit null guard (prevents AutoMapper from allocating an empty object when invoice not found)
- Added `InvoiceClassificationMappingProfileTests` (3 tests with `AssertConfigurationIsValid()`) and `GetInvoiceDetailsHandlerTests` (2 tests)

**Step B (atomic rename):**
- `AccountingTemplateDto` → `AccountingTemplate`, `ReceivedInvoiceDto` → `ReceivedInvoice`, `ReceivedInvoiceItemDto` → `ReceivedInvoiceItem` in Domain
- Propagated rename through Domain interfaces, 5 rule classes, Application services, ClassifyInvoicesHandler, the Flexi adapter, and all tests

### Results
- **3,968 tests pass**, 0 failures
- Frontend TypeScript client: **unchanged** (confirmed via Debug build PostBuild regeneration)
- Domain layer: **zero `*Dto` types remain** in `Anela.Heblo.Domain.Features.InvoiceClassification`

### One concern to note

**Swagger verification deferred**: The local dev environment can't start the backend (a DB migration conflict in an unrelated table). Contract correctness is verified instead via: the mapping profile tests confirming field-by-field preservation, and the TypeScript client being identical after regeneration. Manual swagger diff should be done in staging.

**Out-of-scope finding**: `Catalog/Price/SetProductPricesResultDto.cs` has a `Dto` in Domain — tracked for a separate cleanup PR.