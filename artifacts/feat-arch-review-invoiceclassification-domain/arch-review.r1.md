# Architecture Review: InvoiceClassification Domain DTO Separation

## Skip Design: true

This is a backend-only structural refactor. Public API JSON shape is preserved; the generated TypeScript client produces identical types. No UI/UX work.

## Architectural Fit Assessment

The spec correctly identifies a Clean Architecture violation: Application responses leak Domain-namespaced `*Dto` types. However, the spec's "Option A vs Option B" choice in FR-2 must **not** be left to the implementer — only one option is architecturally valid here, and the evidence is in the existing code.

**Concrete observation:**
- `Domain/Features/InvoiceClassification/IClassificationRule.cs:8` declares `bool Evaluate(ReceivedInvoiceDto invoice, string pattern)`. Five concrete rules in `Domain/Features/InvoiceClassification/Rules/` (`VatClassificationRule`, `DescriptionClassificationRule`, `CompanyNameClassificationRule`, `AmountClassificationRule`, `ItemDescriptionClassificationRule`) all consume `ReceivedInvoiceDto` and `ReceivedInvoiceItemDto`.
- `Application/Features/InvoiceClassification/Services/IInvoiceClassificationService`, `RuleEvaluationEngine`, and `InvoiceClassificationService.ClassifyInvoiceAsync(ReceivedInvoiceDto)` all take the same type.

`ReceivedInvoice(Item)Dto` are therefore **first-class Domain value objects** that the domain rule engine evaluates against — not pure Flexi transport. Moving them to Adapters (Option B) would force a Domain → Adapters reference and break the architecture far worse than the current `Dto` suffix.

`AccountingTemplateDto` has no Domain consumer — only the adapter produces it and the handler returns it. Either option is technically valid; Option A keeps the module symmetric and matches the fact that `IInvoiceClassificationsClient` returns it from a Domain interface.

**Verdict: Option A everywhere.** The spec's flexibility on FR-2 is an architectural red flag — this review removes it.

The two main integration points are: (1) the Adapter mapping profile (`FlexiReceivedInvoiceMappingProfile` already exists and just needs its destination type renamed), and (2) the existing `InvoiceClassificationMappingProfile` in Application, which is the established seam for Domain → Contract mapping in this module.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│ Domain/Features/InvoiceClassification/                              │
│   AccountingTemplate          (renamed, was AccountingTemplateDto)  │
│   ReceivedInvoice             (renamed, was ReceivedInvoiceDto)     │
│   ReceivedInvoiceItem         (renamed, was ReceivedInvoiceItemDto) │
│   IInvoiceClassificationsClient  → returns AccountingTemplate       │
│   IReceivedInvoicesClient        → returns ReceivedInvoice          │
│   IClassificationRule.Evaluate(ReceivedInvoice, ...)                │
│   Rules/*ClassificationRule      → operate on ReceivedInvoice       │
└─────────────────────────────────────────────────────────────────────┘
                          ▲                              ▲
                          │ implements                   │ uses
                          │                              │
┌─────────────────────────┴────────┐  ┌──────────────────┴─────────────────┐
│ Adapters.Flexi/.../              │  │ Application/Features/              │
│   InvoiceClassification/         │  │   InvoiceClassification/           │
│   FlexiInvoiceClassificationsCl  │  │     Contracts/                     │
│     ─ projects Flexi → Domain    │  │       AccountingTemplateDto  (new) │
│     ─ AccountingTemplate         │  │       ReceivedInvoiceDto     (new) │
│   FlexiReceivedInvoicesClient    │  │       ReceivedInvoiceItemDto (new) │
│     ─ AutoMapper Flexi → Domain  │  │     InvoiceClassificationMapping-  │
│   FlexiReceivedInvoiceMapping-   │  │       Profile  → Domain → Contracts│
│     Profile                      │  │     UseCases/GetAccountingTempla...│
│                                  │  │     UseCases/GetInvoiceDetails/... │
└──────────────────────────────────┘  │     Services/InvoiceClassification │
                                      │       Service (uses Domain types)  │
                                      └────────────────────────────────────┘
```

Three layers, one direction of dependency. The Adapter maps external SDK → Domain value object. The Application maps Domain value object → Application contract DTO for the API surface. The Domain layer never sees a contract or a Flexi type.

### Key Design Decisions

#### Decision 1: Domain types are value objects, not transport
**Options considered:** (A) Rename in-place to Domain value objects; (B) Move to `Adapters.Flexi/.../Contracts/` as `FlexiReceivedInvoice*`.
**Chosen approach:** A — rename to `AccountingTemplate`, `ReceivedInvoice`, `ReceivedInvoiceItem` in `Domain/Features/InvoiceClassification/`.
**Rationale:** The Domain `IClassificationRule.Evaluate` signature and 5 rule implementations already operate on these types. Option B would require Domain to reference Adapters — a dependency inversion that breaks Clean Architecture far worse than the current `Dto` suffix. `AccountingTemplate` follows the same rule for symmetry, and because the Domain interface that returns it (`IInvoiceClassificationsClient`) sits in Domain.

#### Decision 2: Application contracts keep the `Dto` suffix and same simple names
**Options considered:** (A) `AccountingTemplateContract` / `ReceivedInvoiceContract`; (B) `AccountingTemplateDto` / `ReceivedInvoiceDto` in `Contracts/`.
**Chosen approach:** B — keep the `Dto` suffix in the Application contracts.
**Rationale:** Matches the existing convention in `Application/Features/InvoiceClassification/Contracts/` (`ClassificationRuleDto`, `ClassificationHistoryDto`, etc.) and across other modules. Critically, it preserves the **generated TypeScript class names** — frontend code that imports `AccountingTemplateDto`, `ReceivedInvoiceDto`, `ReceivedInvoiceItemDto` (24 grep hits in `frontend/src/api/`) continues to compile without manual edits. The C# simple name `AccountingTemplateDto` already exists in two namespaces during the transition; that is fine because the Domain version is renamed away.

#### Decision 3: Mapping in the AutoMapper profile, not inline
**Options considered:** (A) Inline `new AccountingTemplateDto { ... }` in the handler; (B) AutoMapper profile entries; (C) Hand-written mapper class.
**Chosen approach:** B — extend `InvoiceClassificationMappingProfile`.
**Rationale:** The profile is the established convention for this module (already maps `ClassificationRule`, `ClassificationHistory`, `ClassificationStatistics`, `RuleUsageStatistic`). Inline mapping in handlers breaks consistency and is the violation the spec is correcting. The `Adapters.Flexi/.../FlexiReceivedInvoiceMappingProfile` separately handles SDK → Domain — these are two distinct profiles owned by their respective layers and must not be merged.

#### Decision 4: Application contracts are plain classes (not records)
**Options considered:** (A) `record` (matches C# coding style guidelines for value-like data); (B) `class` (matches project CLAUDE.md).
**Chosen approach:** B — `class` with init-able properties.
**Rationale:** `CLAUDE.md` is explicit: "DTOs are classes, never C# records" because the OpenAPI client generator mishandles record parameter order. Project rule overrides global C# style. Existing peer contracts (`ClassificationRuleDto`, `ClassificationHistoryDto`) are classes — this maintains module consistency.

#### Decision 5: Domain value objects remain mutable classes
**Options considered:** (A) Convert to `sealed record` with `init` setters during the rename; (B) Keep as mutable classes.
**Chosen approach:** B — keep as classes.
**Rationale:** The Adapter (`FlexiReceivedInvoicesClient`) constructs them via AutoMapper, which depends on settable properties. Test setup (`ClassifyInvoicesHandlerTests.cs`) uses object initializers. The spec calls this a **surgical refactor with no behavioral change** (Out of Scope explicitly excludes "Refactoring the FlexiBee adapter implementation"). Converting to immutable records would require refactoring the Flexi mapping profile and breaking test arrangements — out of scope. A future PR can tighten immutability across the Domain.

## Implementation Guidance

### Directory / Module Structure

**Files to create** (new):
```
backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Contracts/
    AccountingTemplateDto.cs       (was Domain/.../AccountingTemplateDto.cs)
    ReceivedInvoiceDto.cs          (was Domain/.../ReceivedInvoiceDto.cs)
    ReceivedInvoiceItemDto.cs      (was Domain/.../ReceivedInvoiceItemDto.cs)
```

**Files to rename in place** (file name + type name; same Domain folder):
```
backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/
    AccountingTemplateDto.cs     → AccountingTemplate.cs
    ReceivedInvoiceDto.cs        → ReceivedInvoice.cs
    ReceivedInvoiceItemDto.cs    → ReceivedInvoiceItem.cs
```
Field shape unchanged. Same `namespace Anela.Heblo.Domain.Features.InvoiceClassification;`.

**Files to edit** (signature/type updates only):
- `Domain/Features/InvoiceClassification/IClassificationRule.cs` — `Evaluate(ReceivedInvoice, string)`
- `Domain/Features/InvoiceClassification/IInvoiceClassificationsClient.cs` — returns `List<AccountingTemplate>`
- `Domain/Features/InvoiceClassification/IReceivedInvoicesClient.cs` — returns `List<ReceivedInvoice>` / `ReceivedInvoice?`
- `Domain/Features/InvoiceClassification/Rules/{Vat,Description,CompanyName,Amount,ItemDescription}ClassificationRule.cs` — parameter type
- `Application/Features/InvoiceClassification/Services/IInvoiceClassificationService.cs` — parameter type
- `Application/Features/InvoiceClassification/Services/InvoiceClassificationService.cs` — parameter types in `ClassifyInvoiceAsync` and `RecordClassificationHistory`
- `Application/Features/InvoiceClassification/Services/IRuleEvaluationEngine.cs` and `RuleEvaluationEngine.cs` — parameter types
- `Application/Features/InvoiceClassification/UseCases/ClassifyInvoices/ClassifyInvoicesHandler.cs` — `List<ReceivedInvoice>` local
- `Application/Features/InvoiceClassification/UseCases/GetAccountingTemplates/GetAccountingTemplatesResponse.cs` — `List<AccountingTemplateDto>` now from `Contracts.`; update `using`
- `Application/Features/InvoiceClassification/UseCases/GetAccountingTemplates/GetAccountingTemplatesHandler.cs` — inject `IMapper`, map Domain → contract
- `Application/Features/InvoiceClassification/UseCases/GetInvoiceDetails/GetInvoiceDetailsResponse.cs` — `ReceivedInvoiceDto?` now from `Contracts.`; update `using`
- `Application/Features/InvoiceClassification/UseCases/GetInvoiceDetails/GetInvoiceDetailsHandler.cs` — inject `IMapper`, map Domain → contract
- `Application/Features/InvoiceClassification/InvoiceClassificationMappingProfile.cs` — add three `CreateMap` calls
- `Adapters/Anela.Heblo.Adapters.Flexi/Accounting/InvoiceClassification/FlexiInvoiceClassificationsClient.cs` — projection now creates `AccountingTemplate`
- `Adapters/Anela.Heblo.Adapters.Flexi/Accounting/InvoiceClassification/FlexiReceivedInvoiceMappingProfile.cs` — destination type names
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassifyInvoicesHandlerTests.cs` — all `new ReceivedInvoiceDto { ... }` → `new ReceivedInvoice { ... }`

**New test file:**
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/InvoiceClassificationMappingProfileTests.cs` — follow the exact pattern of `ManufactureOrderMappingProfileTests.cs:11-20` (constructor calls `AssertConfigurationIsValid()`, then per-mapping `Map_Source_To_Dto_PreservesAllFields` tests).

### Interfaces and Contracts

```csharp
// Domain (renamed, unchanged fields)
namespace Anela.Heblo.Domain.Features.InvoiceClassification;
public class AccountingTemplate    { /* same fields as today */ }
public class ReceivedInvoice       { /* same fields, Items: List<ReceivedInvoiceItem> */ }
public class ReceivedInvoiceItem   { /* same fields */ }

public interface IInvoiceClassificationsClient
{
    Task<List<AccountingTemplate>> GetValidAccountingTemplatesAsync(CancellationToken? ct = default);
    Task<bool> UpdateInvoiceClassificationAsync(string id, string tpl, string? dept, CancellationToken? ct = default);
    Task<bool> MarkInvoiceForManualReviewAsync(string id, string reason, CancellationToken? ct = default);
}

public interface IReceivedInvoicesClient
{
    Task<List<ReceivedInvoice>> GetUnclassifiedInvoicesAsync();
    Task<ReceivedInvoice?> GetInvoiceByIdAsync(string invoiceId);
}

public interface IClassificationRule
{
    string Identifier { get; } string DisplayName { get; } string Description { get; }
    bool Evaluate(ReceivedInvoice invoice, string pattern);
}

// Application contracts (new; classes, init properties allowed but settable for AutoMapper)
namespace Anela.Heblo.Application.Features.InvoiceClassification.Contracts;
public class AccountingTemplateDto    { /* same JSON shape as today */ }
public class ReceivedInvoiceDto       { /* same JSON shape; Items: List<ReceivedInvoiceItemDto> */ }
public class ReceivedInvoiceItemDto   { /* same JSON shape */ }

// AutoMapper additions
public class InvoiceClassificationMappingProfile : Profile
{
    public InvoiceClassificationMappingProfile()
    {
        // existing maps...
        CreateMap<AccountingTemplate, Contracts.AccountingTemplateDto>();
        CreateMap<ReceivedInvoice,    Contracts.ReceivedInvoiceDto>();
        CreateMap<ReceivedInvoiceItem, Contracts.ReceivedInvoiceItemDto>();
    }
}
```

### Data Flow

**GetAccountingTemplates:**
```
HTTP GET → Controller → MediatR
  → GetAccountingTemplatesHandler
      → IInvoiceClassificationsClient.GetValidAccountingTemplatesAsync()
          → FlexiInvoiceClassificationsClient: Flexi SDK → new AccountingTemplate(...)
      → returns List<AccountingTemplate>
  → IMapper.Map<List<Contracts.AccountingTemplateDto>>(domainList)
  → GetAccountingTemplatesResponse { Templates = mapped }
→ JSON
```

**GetInvoiceDetails:**
```
HTTP GET → Controller → MediatR
  → GetInvoiceDetailsHandler
      → IReceivedInvoicesClient.GetInvoiceByIdAsync(id)
          → FlexiReceivedInvoicesClient + FlexiReceivedInvoiceMappingProfile
              (Flexi SDK ReceivedInvoiceFlexiDto → Domain ReceivedInvoice)
      → returns ReceivedInvoice?
  → IMapper.Map<Contracts.ReceivedInvoiceDto?>(domain)   // null-safe via AutoMapper
  → GetInvoiceDetailsResponse { Invoice = mapped, Found = domain != null }
→ JSON (byte-identical to current)
```

**ClassifyInvoices (unchanged data flow, type renames only):** Handler fetches `List<ReceivedInvoice>`, passes each to `IInvoiceClassificationService.ClassifyInvoiceAsync(ReceivedInvoice)`, which dispatches to `IClassificationRule.Evaluate(ReceivedInvoice, pattern)`. No mapping needed in this path because rules consume the Domain type directly — the entire point of Decision 1.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `GetInvoiceDetailsHandler` passes `null` Domain object to AutoMapper, causing NRE or mapping to non-null `ReceivedInvoiceDto`. | High | Add explicit `invoice == null ? null : _mapper.Map<ReceivedInvoiceDto>(invoice)` in the handler. Cover with a unit test for the not-found path. |
| OpenAPI schema for the two endpoints drifts even slightly (field casing, nullability, array element type) and breaks the frontend generated client. | High | Before the refactor, commit the current `swagger.json` slice for `/api/invoice-classification/accounting-templates` and `/api/invoice-classification/invoices/{id}` as a reference. After the refactor, diff against it (FR-4 acceptance). Run `npm run build` in `frontend/` as a final gate. |
| Two C# types named `AccountingTemplateDto` exist temporarily during the transition (Domain old + Application new), causing ambiguous-reference compile errors in handlers. | Medium | Land the Domain rename (`AccountingTemplateDto` → `AccountingTemplate`) and the new Application contract in the **same commit/PR**. No transitional state. |
| `Labels` field on `ReceivedInvoiceDto` is declared `string[]` (non-nullable) but the type has no constructor — existing test code passes `Labels = Array.Empty<string>()`. AutoMapper-mapped instances coming from Flexi are populated; instances created in tests must continue to set this. | Low | Initialize `Labels` to `Array.Empty<string>()` in the renamed Domain type. Same for the new contract DTO. |
| `InvoiceClassificationMappingProfile` is currently never validated by a test; broken maps silently ship. | Medium | Create `InvoiceClassificationMappingProfileTests` mirroring `ManufactureOrderMappingProfileTests` (constructor calls `AssertConfigurationIsValid()`). Required by NFR-4 and FR-3 acceptance. |
| `IClassificationRule` is a Domain abstraction; changing its signature touches every concrete rule + tests. Easy to miss one with a stale `using`. | Low | After renames, run `dotnet build` and grep for any remaining `ReceivedInvoiceDto` reference across the solution (`backend/`). Must return zero hits in production code; spec NFR-3 confirms. |
| The auto-generated TypeScript client (`frontend/src/api/generated/api-client.ts`) is regenerated on backend build and contains 24 references to these names; CI may treat the regenerated file as a "change" to review. | Low | Verify the regenerated diff shows no field-level changes — only namespace/comment changes if any. Commit the regenerated client. |

## Specification Amendments

1. **FR-2: Remove Option B.** Replace the "Either / Or" with a single mandate: rename in place to Domain value objects `AccountingTemplate`, `ReceivedInvoice`, `ReceivedInvoiceItem`. Rationale: `IClassificationRule.Evaluate` and 5 concrete domain rules consume these types — Adapter ownership would invert the dependency. See Decision 1 above for the evidence.

2. **FR-3 (clarify):** Acceptance also requires `GetInvoiceDetailsHandler` to handle the null-invoice case explicitly before AutoMapper (`invoice == null ? null : _mapper.Map<ReceivedInvoiceDto>(invoice)`). The current handler returns `Invoice = null, Found = false` and the new mapping must not change that contract.

3. **FR-4 (strengthen):** Replace "OR a manual diff" with: a committed pre-refactor snapshot of the relevant Swagger fragments and an asserting diff in CI/dev script. Manual diffs rot.

4. **FR-5 (add):** Update `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassifyInvoicesHandlerTests.cs` — all 8 references to `ReceivedInvoiceDto`. Explicitly listed because it's the only test file directly affected and easy to overlook.

5. **NFR-3 (scope):** Spec says zero `*Dto` types should remain in Domain "if other modules already violate this, scope is limited". The grep across `Anela.Heblo.Domain` should be run once during the PR to confirm whether other violations exist; if found, file a follow-up ticket but do not expand scope. Document the grep result in the PR description.

6. **Open Questions: removed assumption about feature flags is correct.** Confirmed: no flag wraps this code path.

## Prerequisites

None blocking. Concretely:
- No DB migration needed (no schema or persisted data touched).
- No infrastructure / config changes.
- No new NuGet packages — AutoMapper already in use, profile already registered via `Application` module's auto-scan.
- No env var or secret changes.
- The OpenAPI generation pipeline is already wired (PostBuild event in `Anela.Heblo.API`); just run a Debug build to regenerate `frontend/src/api/generated/api-client.ts` after the refactor.

Recommended before starting: capture the current swagger JSON for the two affected endpoints (`curl http://localhost:5001/swagger/v1/swagger.json | jq '.paths["..."]'`) and commit it to the PR branch as a `before.json` fragment. It becomes the reference for the FR-4 diff check.