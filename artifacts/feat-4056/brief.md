## Module
InvoiceClassification

## Finding
`ClassificationHistory` models two distinct invoice identifiers — `AbraInvoiceId` (the ABRA system's internal ID) and `InvoiceNumber` (the human-visible number) — but the only call site always passes `invoice.InvoiceNumber` for both:

```csharp
// InvoiceClassificationService.cs:102-103
var history = new ClassificationHistory(
    invoice.InvoiceNumber, // AbraInvoiceId  ← same value
    invoice.InvoiceNumber, // InvoiceNumber  ← same value
    ...
);
```

`ReceivedInvoice` (Domain entity populated from the ABRA client) exposes only `InvoiceNumber`; it has no `AbraInvoiceId` property. As a result every row in the `ClassificationHistory` table has `AbraInvoiceId == InvoiceNumber`, making the `AbraInvoiceId` column carry no additional information.

The `InvoiceClassificationMappingProfile` then maps `AbraInvoiceId` → `ClassificationHistoryDto.InvoiceId` and `InvoiceNumber` → `ClassificationHistoryDto.InvoiceNumber`, surfacing both fields in the API response — where they are always identical.

## Why it matters
Two interpretations, both problematic:

1. **Bug (IDs actually differ in ABRA):** If ABRA's internal invoice record ID differs from the displayed invoice number, `ReceivedInvoice` is missing an `AbraInvoiceId` property and the history row stores the wrong value in `AbraInvoiceId`. API consumers relying on `InvoiceId` to call back into ABRA would use the wrong identifier.

2. **Unnecessary complexity (IDs are the same):** The entity, DB schema, and DTO carry a permanently redundant column that adds confusion to anyone reading the code or schema. The two-parameter constructor (`abraInvoiceId`, `invoiceNumber`) creates the false impression they can differ when they cannot.

Either way the domain model and the call site are inconsistent.

## Suggested fix
Determine whether ABRA has distinct internal IDs vs displayed invoice numbers:

- **If they differ:** Add `string AbraInvoiceId { get; set; }` to `ReceivedInvoice`, populate it from the ABRA client response, and pass it correctly to `ClassificationHistory`.
- **If they are always the same:** Remove `AbraInvoiceId` from `ClassificationHistory` and its constructor; rename `InvoiceId` in the DTO to `InvoiceNumber` to match. Drop the corresponding DB column in the next migration.

---
_Filed by daily arch-review routine on 2026-09-03._
