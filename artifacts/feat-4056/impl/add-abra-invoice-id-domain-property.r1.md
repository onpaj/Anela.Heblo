# Implementation: add-abra-invoice-id-domain-property

## What was implemented
Added a new `AbraInvoiceId` property to the `ReceivedInvoice` domain entity to carry FlexiBee's internal `Id` value, alongside the existing `InvoiceNumber` (FlexiBee's `Code`). This is a pure additive change to a plain POCO, enabling downstream mapping-profile and service call-site work to populate/read this value.

## Files created/modified
- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ReceivedInvoice.cs` — added `public string AbraInvoiceId { get; set; } = string.Empty;` directly above the existing `InvoiceNumber` property, matching the existing plain-settable-property style.

## Tests
None needed — plain POCO, verified by compilation, as specified in the task.

## How to verify
1. `dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj` — confirms `Build succeeded.` with 0 errors (pre-existing warnings in unrelated files are expected and unchanged).
2. Inspect the diff on `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ReceivedInvoice.cs` to confirm only the `AbraInvoiceId` property was added.

## Notes
No deviations. Build succeeded with 0 errors (87 pre-existing warnings unrelated to this change, present before this edit).

## PR Summary
Adds a new `AbraInvoiceId` string property to the `ReceivedInvoice` domain entity (`backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ReceivedInvoice.cs`) to carry FlexiBee's internal `Id`, complementing the existing `InvoiceNumber` (FlexiBee's `Code`). This is a pure additive, non-breaking change to a plain POCO that unblocks a later mapping-profile task and a later service call-site task which need somewhere to store/read this value.

### Changes
- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ReceivedInvoice.cs` — added `AbraInvoiceId` property above `InvoiceNumber`.

## Status
DONE
