# Specification: Fix AbraInvoiceId population in ClassificationHistory

## Summary
`InvoiceClassificationService.RecordClassificationHistory` currently passes `invoice.InvoiceNumber` for both the `abraInvoiceId` and `invoiceNumber` constructor parameters of `ClassificationHistory`, because the domain entity `ReceivedInvoice` has no field carrying FlexiBee's (ABRA's) true internal record identifier. Investigation of the FlexiBee SDK confirms ABRA/FlexiBee genuinely exposes two distinct identifiers per received invoice — an internal numeric `Id` and a human-readable document `Code` — so this is a bug, not a redundant field: `ReceivedInvoice` is missing a property. This spec adds `AbraInvoiceId` to `ReceivedInvoice`, populates it from FlexiBee's internal `Id`, and wires it correctly into `ClassificationHistory`, leaving the existing DB schema and DTO shape unchanged.

## Background
`ClassificationHistory` (`backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ClassificationHistory.cs`) has carried two separate identifier fields — `AbraInvoiceId` and `InvoiceNumber` — since the `InvoiceClassificationFeature` migration (2026-10-31). The only call site, `InvoiceClassificationService.RecordClassificationHistory` (lines 101-103), passes `invoice.InvoiceNumber` for both parameters:

```csharp
var history = new ClassificationHistory(
    invoice.InvoiceNumber, // AbraInvoiceId
    invoice.InvoiceNumber, // InvoiceNumber
    ...
);
```

`ReceivedInvoice` (`backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ReceivedInvoice.cs`) exposes only `InvoiceNumber`, populated in `FlexiReceivedInvoiceMappingProfile` from `ReceivedInvoiceFlexiDto.Code` — the human-readable document number (e.g. `PF250051`).

Reflecting over the installed `Rem.FlexiBeeSDK.Model` package (v0.1.139) shows `ReceivedInvoiceFlexiDto` has both:
- `Int32 Id` — FlexiBee's internal database record identifier (never mapped anywhere in this codebase today)
- `String Code` — the human-readable document number, mapped to `ReceivedInvoice.InvoiceNumber`

This confirms interpretation 1 from the brief: ABRA/FlexiBee **does** expose a distinct internal ID separate from the invoice number. The `AbraInvoiceId` field on `ClassificationHistory` is not redundant by design — it is simply never populated from the value it is meant to carry. Every historical row therefore has `AbraInvoiceId == InvoiceNumber`, which is a data-fidelity bug, not evidence the two concepts are the same thing.

Separately, `IReceivedInvoiceClient.GetAsync`, `IReceivedInvoiceClient.AddTagAsync`/`RemoveTagAsync`, and `IAccountingTemplateClient.UpdateInvoiceAsync` (the FlexiBee SDK calls used by `FlexiInvoiceClassificationsClient` and `FlexiReceivedInvoicesClient`) all address invoices by `Code`, not by the internal `Id` — confirmed by their parameter names (`code`, `invoiceCode`) and by SDK documentation examples (`client.GetAsync("FAP-2024-001", ...)`). This means `ReceivedInvoice.InvoiceNumber` (Code-based) correctly remains the identifier used for all external ABRA API calls; only the audit-trail field `AbraInvoiceId` needs the true internal `Id`.

## Functional Requirements

### FR-1: Add `AbraInvoiceId` to the `ReceivedInvoice` domain entity
Add a new property to `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ReceivedInvoice.cs`:

```csharp
public string AbraInvoiceId { get; set; } = string.Empty;
```

**Acceptance criteria:**
- `ReceivedInvoice` has a public settable `string AbraInvoiceId` property, defaulting to `string.Empty`, following the existing style of that class (plain settable properties, no records — see project DTO/entity conventions).
- Existing `InvoiceNumber` property and all other properties are unchanged.

### FR-2: Populate `AbraInvoiceId` from FlexiBee's internal invoice `Id`
Update `FlexiReceivedInvoiceMappingProfile` (`backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/InvoiceClassification/FlexiReceivedInvoiceMappingProfile.cs`) to map FlexiBee's internal numeric `Id` (on `ReceivedInvoiceFlexiDto`) to the new `AbraInvoiceId` field, converting to string:

```csharp
.ForMember(dest => dest.AbraInvoiceId, opt => opt.MapFrom(src => src.Id.ToString()))
```

**Acceptance criteria:**
- Every `ReceivedInvoice` produced by `FlexiReceivedInvoicesClient.GetUnclassifiedInvoicesAsync` and `GetInvoiceByIdAsync` has `AbraInvoiceId` set to the source FlexiBee record's internal `Id` (as a string), while `InvoiceNumber` continues to carry `Code`.
- `AbraInvoiceId` and `InvoiceNumber` are different values for any invoice where FlexiBee's internal `Id` differs from its `Code` (true for all real invoices, since `Id` is a sequential integer and `Code` is a formatted document number).

### FR-3: Pass the correct value into `ClassificationHistory`
Update `InvoiceClassificationService.RecordClassificationHistory` (`backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/InvoiceClassificationService.cs`, lines 101-103) to pass `invoice.AbraInvoiceId` instead of `invoice.InvoiceNumber` for the first constructor argument:

```csharp
var history = new ClassificationHistory(
    invoice.AbraInvoiceId, // AbraInvoiceId
    invoice.InvoiceNumber, // InvoiceNumber
    ...
);
```

**Acceptance criteria:**
- New `ClassificationHistory` rows created after this change have `AbraInvoiceId` set to the true FlexiBee internal ID and `InvoiceNumber` set to the human-readable document code; the two values differ for real invoices.
- No change to `ClassificationHistory`'s constructor signature, `ClassificationHistoryConfiguration` (EF mapping), or `ClassificationHistoryDto` — the existing two-field shape was correct; only the call site was wrong.
- No database migration is required — the `AbraInvoiceId` column and its index already exist from the `InvoiceClassificationFeature` migration.

### FR-4: Update existing unit tests to reflect the corrected behavior
The following tests currently assert `capturedHistory.AbraInvoiceId.Should().Be(invoice.InvoiceNumber)` and must be updated to assert against a distinct `invoice.AbraInvoiceId` test value instead, in `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/InvoiceClassificationServiceTests.cs`:
- Line 76, 156, 237, 299 — each of these test's `ReceivedInvoice` fixture must set a distinct `AbraInvoiceId` (different from `InvoiceNumber`) and the assertion must be updated to `capturedHistory.AbraInvoiceId.Should().Be(invoice.AbraInvoiceId)`.

**Acceptance criteria:**
- Updated tests fail if `AbraInvoiceId` and `InvoiceNumber` are ever conflated again (i.e. the test fixture must use genuinely different values for the two fields, not equal ones).
- All existing assertions on `InvoiceNumber`, `ClassificationResult`, etc. remain unchanged.
- `InvoiceClassificationMappingProfileTests.cs` and `ClassificationHistoryRepositoryTests.cs` (which construct `ClassificationHistory` directly with an explicit `abraInvoiceId:` argument) require no change — they already exercise the entity/DTO mapping correctly and are independent of the `ReceivedInvoice`→`ClassificationHistory` call site.

## Non-Functional Requirements

### NFR-1: Backward compatibility of the API contract
`ClassificationHistoryDto.InvoiceId` (mapped from `AbraInvoiceId`) and `ClassificationHistoryDto.InvoiceNumber` keep their existing names and mapping (`InvoiceClassificationMappingProfile.cs` line 15). No frontend or API-consumer-facing contract changes are needed; the generated TypeScript client is unaffected in shape (only values returned for `invoiceId` on newly-created history rows will differ from `invoiceNumber` going forward).

### NFR-2: Data correctness / no silent truncation
FlexiBee's internal `Id` is an `Int32`; converting it with `.ToString()` must not silently lose information (invariant culture, no formatting applied) and must fit within the existing `AbraInvoiceId` column's `HasMaxLength(100)` constraint (a stringified 32-bit integer is at most 11 characters, well within range).

## Data Model
No schema changes. `ClassificationHistory` (`public.ClassificationHistory` table) keeps its existing columns, including `AbraInvoiceId` (`character varying(100)`, indexed via `IX_ClassificationHistory_AbraInvoiceId`) and `InvoiceNumber` (`character varying(100)`, indexed via `IX_ClassificationHistory_InvoiceNumber`).

`ReceivedInvoice` (domain entity, not persisted) gains one new in-memory field:
- `AbraInvoiceId: string` — FlexiBee's internal record ID, sourced from `ReceivedInvoiceFlexiDto.Id`.

## API / Interface Design
No new endpoints or contract changes. `GET` invoice-classification-history endpoints continue to return `ClassificationHistoryDto` with `invoiceId` and `invoiceNumber` fields; from this change forward, `invoiceId` reflects ABRA's genuine internal record ID rather than a duplicate of `invoiceNumber`.

## Dependencies
- `Rem.FlexiBeeSDK.Model` / `Rem.FlexiBeeSDK.Client` (v0.1.139, already referenced by `Anela.Heblo.Adapters.Flexi`) — no version change needed; `ReceivedInvoiceFlexiDto.Id` is already present in the currently-referenced package version.
- AutoMapper (`InvoiceClassificationMappingProfile`, `FlexiReceivedInvoiceMappingProfile`) — existing dependency, no upgrade needed.

## Out of Scope
- Backfilling `AbraInvoiceId` on historical `ClassificationHistory` rows created before this fix. Those rows have `AbraInvoiceId == InvoiceNumber` and the original FlexiBee internal `Id` for those invoices is not recoverable from stored data alone (it would require re-querying FlexiBee by invoice number/date and matching, which is out of scope for this fix). This is called out explicitly below.
- Changing how `IReceivedInvoicesClient.GetInvoiceByIdAsync`, `IInvoiceClassificationsClient.UpdateInvoiceClassificationAsync`, or tag-add/remove operations address invoices. They correctly continue to use `InvoiceNumber` (Code), matching the FlexiBee SDK's own parameter semantics (`code`, `invoiceCode`) confirmed during investigation.
- Exposing `AbraInvoiceId` on `ReceivedInvoiceDto` / the `GetInvoiceDetails` or `ClassifyInvoices` API responses. Only `ClassificationHistoryDto` currently surfaces this concept (as `InvoiceId`); no brief or existing consumer requires it elsewhere.
- Documenting this finding in `docs/integrations/flexibee-api.md`. Per repository convention (CLAUDE.md: "Shoptet API findings must be documented before use"), a follow-up documentation update noting `ReceivedInvoiceFlexiDto.Id` vs `Code` semantics is recommended but is a documentation task, not part of this code-level fix.

## Open Questions
None.

## Status: COMPLETE
