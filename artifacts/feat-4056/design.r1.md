# Design: Fix AbraInvoiceId population in ClassificationHistory

## Component Design

### `ReceivedInvoice` (domain entity)
`backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ReceivedInvoice.cs`

Gains one new public settable property, following the existing plain-class style (no records):

```csharp
public string AbraInvoiceId { get; set; } = string.Empty;
```

Responsibility unchanged: an in-memory (non-persisted) representation of a FlexiBee received invoice. `InvoiceNumber` continues to carry FlexiBee's `Code` (human-readable document number, used for all outbound FlexiBee API calls); `AbraInvoiceId` newly carries FlexiBee's internal `Id` (used only as an audit-trail value on `ClassificationHistory`).

### `FlexiReceivedInvoiceMappingProfile` (AutoMapper profile)
`backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/InvoiceClassification/FlexiReceivedInvoiceMappingProfile.cs`

Responsibility: translate FlexiBee-specific representations (`ReceivedInvoiceFlexiDto`) into the domain's own representation (`ReceivedInvoice`). Gains one new member mapping, converting FlexiBee's `Int32 Id` to the domain's `string AbraInvoiceId`, using invariant culture per the architecture review's conversion guidance:

```csharp
.ForMember(dest => dest.AbraInvoiceId, opt => opt.MapFrom(src => src.Id.ToString(CultureInfo.InvariantCulture)))
```

This is the sole conversion point — both `FlexiReceivedInvoicesClient.GetUnclassifiedInvoicesAsync` and `GetInvoiceByIdAsync` construct `ReceivedInvoice` exclusively via `_mapper.Map<ReceivedInvoice>(...)` through this profile, so no other call site needs a change to populate the field everywhere invoices originate.

### `InvoiceClassificationService.RecordClassificationHistory`
`backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/InvoiceClassificationService.cs` (lines 101-103)

Responsibility unchanged: builds a `ClassificationHistory` audit row for each classified invoice. Corrects the first constructor argument from `invoice.InvoiceNumber` to `invoice.AbraInvoiceId`, so the two constructor parameters carry genuinely distinct values:

```csharp
var history = new ClassificationHistory(
    invoice.AbraInvoiceId, // AbraInvoiceId
    invoice.InvoiceNumber, // InvoiceNumber
    ...
);
```

No other component in the pipeline changes: `ClassificationHistory`'s constructor, `ClassificationHistoryConfiguration` (EF mapping), `ClassificationHistoryRepository`, and `InvoiceClassificationMappingProfile` (which maps `AbraInvoiceId` → `ClassificationHistoryDto.InvoiceId`) are all already correctly wired for two distinct identifier values and require no modification.

## Data Schemas

No database schema change. `ClassificationHistory` (`public.ClassificationHistory` table) keeps its existing columns:
- `AbraInvoiceId` — `character varying(100)`, indexed via `IX_ClassificationHistory_AbraInvoiceId` — from this change forward populated from FlexiBee's internal `Id` (stringified `Int32`, max 11 characters, well within the 100-char column limit) instead of a duplicate of `InvoiceNumber`.
- `InvoiceNumber` — `character varying(100)`, indexed via `IX_ClassificationHistory_InvoiceNumber` — unchanged, continues to carry FlexiBee's `Code`.

`ReceivedInvoice` (domain entity, not persisted) gains one new in-memory field:
- `AbraInvoiceId: string` — sourced from `ReceivedInvoiceFlexiDto.Id` (FlexiBee's internal record identifier), distinct from the existing `InvoiceNumber: string` field sourced from `ReceivedInvoiceFlexiDto.Code`.

No API/DTO shape changes. `ClassificationHistoryDto.InvoiceId` (mapped from `AbraInvoiceId`) and `ClassificationHistoryDto.InvoiceNumber` keep their existing names, types, and mapping in `InvoiceClassificationMappingProfile.cs`. Only the runtime *value* returned for `invoiceId` on newly-created history rows changes — it now reflects ABRA's genuine internal record ID rather than a duplicate of `invoiceNumber`. Historical rows created before this fix retain `AbraInvoiceId == InvoiceNumber` (not backfilled, per spec's Out of Scope).

### Test fixture data shape
`backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/InvoiceClassificationServiceTests.cs` (lines 76, 156, 237, 299): each affected test's `ReceivedInvoice` fixture must set a distinct `AbraInvoiceId` value (different from its `InvoiceNumber` value, e.g. `AbraInvoiceId = "ABRA-001"` alongside `InvoiceNumber = "INV-001"`), with the assertion changed from:

```csharp
capturedHistory.AbraInvoiceId.Should().Be(invoice.InvoiceNumber);
```

to:

```csharp
capturedHistory.AbraInvoiceId.Should().Be(invoice.AbraInvoiceId);
```
