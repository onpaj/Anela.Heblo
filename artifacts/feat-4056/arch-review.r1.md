# Architecture Review: Fix AbraInvoiceId population in ClassificationHistory

## Skip Design: true

## Architectural Fit Assessment
This is a small, contained data-fidelity bugfix entirely within the existing `InvoiceClassification` vertical slice. It touches three layers already established for this feature — Domain entity (`ReceivedInvoice`), Adapter mapping profile (`FlexiReceivedInvoiceMappingProfile`), and Application service (`InvoiceClassificationService`) — using patterns already in use elsewhere in the same slice. No new module, no new dependency, no cross-module boundary is crossed. It aligns cleanly with `ADR-001`/`ADR-002` (single DbContext, no schema change needed) and with the DTO rule in `development_guidelines.md` (`ReceivedInvoice` remains a plain settable-property class, not a record, consistent with the rest of the file).

I independently verified the spec's central technical claim by loading `Rem.FlexiBeeSDK.Model` v0.1.139 (the exact version this project references) via reflection: `ReceivedInvoiceFlexiDto` does expose both `Id : System.Int32` and `Code : System.String` as distinct declared properties. The spec's premise — that ABRA's internal record ID and its human-readable document code are genuinely different values — is confirmed, not merely asserted. Interpretation 1 from the original brief finding is correct; this is a real bug, not a design smell to be "fixed" by removing the field.

I also confirmed both call sites that produce a `ReceivedInvoice` (`FlexiReceivedInvoicesClient.GetUnclassifiedInvoicesAsync` and `GetInvoiceByIdAsync`) route through `_mapper.Map<...>(...)` — neither constructs `ReceivedInvoice` by hand — so a single `FlexiReceivedInvoiceMappingProfile` change is sufficient to populate `AbraInvoiceId` everywhere invoices originate from FlexiBee. The only other manual `ReceivedInvoice` construction site is a test fixture (`InvoiceClassificationFixtures.CreateInvoice`), which is out of scope per FR-4's explicit test list.

## Proposed Architecture

### Component Overview
```
FlexiBee SDK                Adapter                      Domain              Application
─────────────                ───────                      ──────              ───────────
ReceivedInvoiceFlexiDto  →  FlexiReceivedInvoiceMapping  →  ReceivedInvoice  →  InvoiceClassificationService
  .Id   (int, internal)      Profile (AutoMapper)            .AbraInvoiceId     .RecordClassificationHistory()
  .Code (string, doc no.)      NEW: Id → AbraInvoiceId         (NEW property)     invoice.AbraInvoiceId →
                                Code → InvoiceNumber                              ClassificationHistory.AbraInvoiceId
                                (unchanged)                                       invoice.InvoiceNumber →
                                                                                   ClassificationHistory.InvoiceNumber
```
No new components. This is a one-field addition threaded through an existing, already-correct pipeline (AutoMapper profile → domain entity → application service → existing `ClassificationHistory` constructor → existing EF configuration → existing DTO mapping). Every downstream consumer (`ClassificationHistoryConfiguration`, `ClassificationHistoryDto`, `InvoiceClassificationMappingProfile`) is already correctly wired for two distinct identifier values; only the source of truth was missing upstream.

### Key Design Decisions

#### Decision 1: Fix the source (add `AbraInvoiceId` to `ReceivedInvoice`) vs. collapse the two fields
**Options considered:**
1. Add `AbraInvoiceId` to `ReceivedInvoice`, populate from FlexiBee's `Id`, fix the call site (spec's approach).
2. Treat the two fields as redundant, remove `AbraInvoiceId` from `ClassificationHistory`/DTO, migrate the DB column away (brief's alternative interpretation).

**Chosen approach:** Option 1 — confirmed correct by direct inspection of the referenced SDK version. `Id` and `Code` are genuinely distinct values in FlexiBee's data model.

**Rationale:** Option 2 would require a destructive schema change (dropping an already-indexed column) and an API contract rename (`InvoiceId`), for a false premise. Option 1 is additive, non-breaking, and consistent with `ADR-001` (no urgency to touch the DbContext/migrations layer for something already provisioned).

#### Decision 2: Where the FlexiBee `Id`→`AbraInvoiceId` conversion happens
**Options considered:**
1. Convert in the AutoMapper profile (`FlexiReceivedInvoiceMappingProfile`) via `MapFrom(src => src.Id.ToString())`.
2. Convert in the application service at the point `ClassificationHistory` is constructed.

**Chosen approach:** Option 1, at the adapter boundary.

**Rationale:** The adapter layer (`Anela.Heblo.Adapters.Flexi`) is the correct place to translate FlexiBee-specific representations (`Int32` internal ID) into the domain's own representation (`string`, matching `InvoiceNumber`'s type and the existing `AbraInvoiceId` column type). This keeps `ReceivedInvoice` and `InvoiceClassificationService` free of FlexiBee-specific type-conversion concerns, matching how `InvoiceNumber` (`Code`) and every other field on `ReceivedInvoice` are already populated in this same profile — no new pattern is introduced.

## Implementation Guidance

### Directory / Module Structure
No new files or directories. All three touched files already exist in their correct locations per `filesystem.md`'s Domain/Adapters/Application layering:
- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ReceivedInvoice.cs` — add one property.
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/InvoiceClassification/FlexiReceivedInvoiceMappingProfile.cs` — add one `.ForMember(...)` line.
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/InvoiceClassificationService.cs` — change one argument (line 102) from `invoice.InvoiceNumber` to `invoice.AbraInvoiceId`.
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/InvoiceClassificationServiceTests.cs` — update 4 test fixtures/assertions (verified exact line numbers: 76, 156, 237, 299, each with an adjacent `ReceivedInvoice` object initializer a few lines above that needs an `AbraInvoiceId` value added).

### Interfaces and Contracts
No interface or contract changes. `ClassificationHistory`'s public constructor signature, `IClassificationHistoryRepository`, `ClassificationHistoryDto`, and `IReceivedInvoicesClient`/`IReceivedInvoiceClient` are all unchanged — confirmed by inspecting `ClassificationHistoryConfiguration.cs` (column + index already present, `HasMaxLength(100)`) and `InvoiceClassificationMappingProfile.cs` (already maps `AbraInvoiceId` → `ClassificationHistoryDto.InvoiceId`). `ReceivedInvoice.AbraInvoiceId` should be declared as:
```csharp
public string AbraInvoiceId { get; set; } = string.Empty;
```
— matching the existing plain-property, non-record style of every other field in this class (per project DTO rule: classes, never records).

### Data Flow
1. `FlexiReceivedInvoicesClient.GetUnclassifiedInvoicesAsync` / `GetInvoiceByIdAsync` call the FlexiBee SDK, get `ReceivedInvoiceFlexiDto` (with distinct `.Id` and `.Code`).
2. `_mapper.Map<ReceivedInvoice>(...)` runs `FlexiReceivedInvoiceMappingProfile`, now setting `AbraInvoiceId = src.Id.ToString()` alongside the existing `InvoiceNumber = src.Code`.
3. `InvoiceClassificationService.ClassifyInvoiceAsync` receives the fully-populated `ReceivedInvoice` and calls `RecordClassificationHistory`, which now passes `invoice.AbraInvoiceId` and `invoice.InvoiceNumber` as two genuinely distinct values into `new ClassificationHistory(...)`.
4. `ClassificationHistoryRepository.AddAsync` persists via unchanged EF configuration.
5. `InvoiceClassificationMappingProfile` maps the persisted `AbraInvoiceId` to `ClassificationHistoryDto.InvoiceId` for API consumers — unchanged mapping, now carrying a genuinely different value than `InvoiceNumber`.

### Test Impact
Confirmed by direct inspection: `InvoiceClassificationServiceTests.cs` has exactly 4 occurrences of `capturedHistory.AbraInvoiceId.Should().Be(invoice.InvoiceNumber)` at lines 76, 156, 237, 299, one per test method, each preceded by a `ReceivedInvoice` object initializer (`InvoiceNumber = "INV-00N"`, ...) that must gain a distinct `AbraInvoiceId` value (e.g. `"ABRA-00N"`) and a corresponding assertion change to `.Be(invoice.AbraInvoiceId)`. `InvoiceClassificationMappingProfileTests.cs` and `ClassificationHistoryRepositoryTests.cs` construct `ClassificationHistory` directly with explicit `abraInvoiceId:` arguments already distinct from `InvoiceNumber` (e.g. `"PF250051"` vs different values) — confirmed by inspection; these require no change, matching the spec.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Historical `ClassificationHistory` rows keep `AbraInvoiceId == InvoiceNumber` (no backfill) | Low | Explicitly out of scope per spec and acceptable — the original FlexiBee internal `Id` for past invoices isn't recoverable from stored data. No action needed; document as a known limitation if a future report/audit consumer surfaces confusion. |
| A consumer of `ClassificationHistoryDto.InvoiceId` (API/frontend) currently assumes `InvoiceId == InvoiceNumber` and has baked in that equivalence | Low-Medium | Grep the frontend/API for any code branching on `invoiceId === invoiceNumber` or using `invoiceId` to construct FlexiBee calls before merging — the spec's NFR-1 already asserts no such dependency exists, but this was not directly verified against frontend code in this review since it's out of scope for this backend slice. Worth a quick frontend `invoiceId` usage grep as a pre-merge sanity check, not a blocker. |
| `.ToString()` on `Int32` differs across locale/culture in edge environments | Very Low | `Int32.ToString()` with no format provider uses current culture only for negative signs/digit grouping, neither of which applies to a plain positive sequential ID; no explicit `CultureInfo.InvariantCulture` is strictly required, but adding it costs nothing and removes any doubt — acceptable either way. |

## Specification Amendments
None required to FR-1 through FR-4 or the NFRs — the spec's technical claims about the FlexiBee SDK (`Id` vs `Code` being distinct, both call sites using AutoMapper, the exact 4 test line numbers, the other two test files needing no change, the column already existing) were all independently verified against the actual referenced package version and source files and found accurate. One clarification for the implementer: apply `Int32.ToString(CultureInfo.InvariantCulture)` rather than bare `.ToString()` in `FlexiReceivedInvoiceMappingProfile` — negligible functional difference here, but it matches defensive-conversion style used elsewhere when crossing an external-system boundary and removes any reviewer question about locale sensitivity.

## Prerequisites
None. No migration, no config change, no infrastructure change. The `AbraInvoiceId` column and its index (`IX_ClassificationHistory_AbraInvoiceId`) already exist in `ClassificationHistoryConfiguration.cs`, confirmed by inspection. Implementation can start immediately.
