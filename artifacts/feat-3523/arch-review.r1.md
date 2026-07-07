# Architecture Review: Remove orphaned InvoiceClassification statistics dead code

## Skip Design: true

This is a pure backend/contract deletion (Domain → Application/Contracts → AutoMapper → Persistence) plus removal of one unused frontend query-key constant. No component renders, no route changes, no visual surface is touched. (See *Specification Amendments* below for a related but out-of-scope UI finding that *would* require design attention if picked up separately.)

## Architectural Fit Assessment

This is a straightforward, low-risk dead-code removal that fits a pattern already established in this repo: the "daily arch-review" routine periodically files findings about speculative code that was scaffolded but never wired up end-to-end (see the `JournalIndicator.FamilyEntries` finding referenced in `brief.md`, and ADR-005's removal of `BaseApiController.GetCurrentUserId()`). `development_guidelines.md` doesn't have an explicit "no orphaned code" rule, but the codebase's Vertical Slice convention implies every layer (Domain → Contracts → Handler → Controller) should be reachable from an actual use case; here the chain stops one hop short — Domain and Contract types exist, a repository method exists, but there is no MediatR request/handler and no controller action, so the method is unreachable through the registered DI surface.

I verified this directly:

- `IClassificationHistoryRepository` (`backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IClassificationHistoryRepository.cs`) declares only `AddAsync`, `GetHistoryAsync`, `GetHistoryByInvoiceIdAsync`, `GetPagedHistoryAsync` — **no `GetStatisticsAsync`**. Since `InvoiceClassificationModule.cs` registers the repository only as `services.AddScoped<IClassificationHistoryRepository, ClassificationHistoryRepository>()`, `GetStatisticsAsync` is unreachable through the DI-resolved interface. It could only be called via a concrete-type cast, which nothing in the codebase does.
- `InvoiceClassificationController.cs` has no statistics endpoint; no `UseCases/GetStatistics*` folder exists (contrast with the five real use cases: `GetClassificationHistory`, `GetClassificationRules`, `CreateClassificationRule`, `UpdateClassificationRule`, `DeleteClassificationRule`, `ReorderClassificationRules`, `GetClassificationRuleTypes`).
- The generated OpenAPI client has no statistics operation for this module, confirming no controller endpoint was ever shipped.
- Grep across `backend/src`, `backend/test`, and `frontend/src` for all five symbol names turns up matches **only** in the five files the spec names, plus the two `CreateMap` lines in `InvoiceClassificationMappingProfile.cs`. No test file references `GetStatisticsAsync`, `ClassificationStatistics`, or `RuleUsageStatistic*` (checked `ClassificationHistoryRepositoryTests.cs` and `InvoiceClassificationMappingProfileTests.cs` specifically — zero hits).
- Neither `ClassificationStatistics` nor `RuleUsageStatistic` is an EF-mapped entity (no `DbSet`, not referenced in `ClassificationHistoryConfiguration.cs` or any migration) — they're plain in-memory projection classes computed inside `GetStatisticsAsync`. Removing them has zero migration impact.
- The frontend `statistics` entry in `CLASSIFICATION_QUERY_KEYS` (`frontend/src/api/hooks/useInvoiceClassification.ts:32`) has exactly one occurrence in the whole repo — its own declaration. It is never read via `CLASSIFICATION_QUERY_KEYS.statistics` anywhere, confirmed by grep.

This is exactly what the spec claims: fully orphaned code with no interface exposure, no use case, no endpoint, and no real frontend consumer. The removal is safe as scoped.

**One nuance the spec doesn't surface** (see *Specification Amendments*): there **is** a rendered frontend component named `ClassificationStats.tsx` that displays a "Statistiky klasifikace" modal on `InvoiceClassificationPage.tsx`, but it renders hardcoded `mockStats` data and has no dependency on the `statistics` query key or on `ClassificationStatisticsDto`. It is a separate, still-live piece of misleading-but-not-technically-dead UI that this spec correctly does not touch (removing it is genuinely a different concern with its own design implications), but it should be flagged as a follow-up.

## Proposed Architecture

### Component Overview

Before (dead branch marked with ✗):

```
Domain/Features/InvoiceClassification/
  ClassificationHistory.cs                 (kept — real entity)
  ClassificationStatistics.cs              ✗ orphaned projection type
  RuleUsageStatistic.cs                    ✗ orphaned projection type
  IClassificationHistoryRepository.cs      (kept — does NOT declare GetStatisticsAsync)

Application/Features/InvoiceClassification/
  Contracts/ClassificationStatisticsDto.cs ✗ orphaned DTO
  Contracts/RuleUsageStatisticDto.cs       ✗ orphaned DTO
  InvoiceClassificationMappingProfile.cs   → 2 dead CreateMap<> lines reference the above

Persistence/InvoiceClassification/
  ClassificationHistoryRepository.cs
    ├─ AddAsync                 (kept, used)
    ├─ GetHistoryAsync          (kept, used)
    ├─ GetHistoryByInvoiceIdAsync (kept, used)
    ├─ GetPagedHistoryAsync     (kept, used)
    └─ GetStatisticsAsync       ✗ unreachable (not on interface, no caller)

frontend/src/api/hooks/useInvoiceClassification.ts
  CLASSIFICATION_QUERY_KEYS.statistics      ✗ unused key, no query built from it

frontend/src/pages/InvoiceClassification/components/ClassificationStats.tsx
  (renders hardcoded mockStats — separate concern, NOT in scope, see amendments)
```

After: the five files/members marked ✗ are deleted; everything else is untouched.

### Key Design Decisions

#### Decision 1: Delete rather than "finish" the feature
**Options considered:**
1. Implement the missing use case + controller endpoint + real frontend chart to make `GetStatisticsAsync` a complete vertical slice.
2. Delete the orphaned code and keep the module scoped to what it actually does today.

**Chosen approach:** Delete (option 2), exactly as scoped in the spec.

**Rationale:** Building a real statistics feature is a product decision (what metrics, what UI, what refresh cadence) that nobody has asked for — the brief is explicitly an arch-review dead-code finding, not a feature request. Per the repo's own precedent (ADR-005's removal of unused identity-resolution code, and the sibling `JournalIndicator.FamilyEntries` finding's "remove is simplest" recommendation), the default remedy for scaffolded-but-unfinished code with no consumer is removal, not completion. If statistics are wanted later, `InvoiceImportStatistics` in the Analytics module is the closest existing pattern to copy (`GetInvoiceImportStatisticsRequest/Handler`, a real MediatR use case + controller action) — a new attempt should start from a fresh design, not resurrect this abandoned branch.

#### Decision 2: No interface or DI changes required
**Options considered:**
1. Remove `GetStatisticsAsync` from the interface too (defensive, in case it's declared somewhere and this repo's copy just isn't up to date).
2. Leave the interface untouched — it never declared the method.

**Chosen approach:** Option 2, matching FR-1's "No interface change needed."

**Rationale:** Verified directly against `IClassificationHistoryRepository.cs` — the four remaining methods are the complete interface. `GetStatisticsAsync` exists only on the concrete `ClassificationHistoryRepository` class as an extra public method never exposed through the DI-registered contract. No `InvoiceClassificationModule.cs` change, no test-double/mock update is needed.

#### Decision 3: Treat the frontend `statistics` query key and `ClassificationStats.tsx` as two different problems
**Options considered:**
1. Bundle removal of the dead query key with removal/fix of the mock-data `ClassificationStats.tsx` modal, since both are "statistics" and both are arguably not real.
2. Keep this PR scoped to exactly what the spec says (query key only) and file `ClassificationStats.tsx` separately.

**Chosen approach:** Option 2 — matches the spec's stated scope, and matches `development_guidelines.md`'s vertical-slice/surgical-change ethos.

**Rationale:** The query key is genuinely dead code — zero references anywhere. `ClassificationStats.tsx` is a different defect class: it's *live and rendered* (wired into a modal on `InvoiceClassificationPage.tsx` behind a `showStatsModal` toggle), just fed by a `TODO: Replace with actual API call` mock object instead of a real endpoint. Removing dead code and fixing a misleading-UI defect are different changes with different blast radii and (for the UI fix) different design implications — conflating them in one spec risks scope creep and an implementation that quietly changes user-visible behavior beyond what was asked. See *Specification Amendments*.

## Implementation Guidance

### Directory / Module Structure

No new files or directories. Delete exactly:

- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ClassificationStatistics.cs`
- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/RuleUsageStatistic.cs`
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Contracts/ClassificationStatisticsDto.cs`
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Contracts/RuleUsageStatisticDto.cs`

Edit (remove specific members only):

- `backend/src/Anela.Heblo.Persistence/InvoiceClassification/ClassificationHistoryRepository.cs` — delete the `GetStatisticsAsync` method (verified at lines 81–121 of a 122-line file; the closing `}` on line 122 is the class's own closing brace and must stay).
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/InvoiceClassificationMappingProfile.cs` — delete lines 18–19 (`CreateMap<ClassificationStatistics, ClassificationStatisticsDto>();` and `CreateMap<RuleUsageStatistic, RuleUsageStatisticDto>();`). Leave the `AccountingTemplate`/`ReceivedInvoiceItem`/`ReceivedInvoice` mappings that follow untouched.
- `frontend/src/api/hooks/useInvoiceClassification.ts` — delete the `statistics: ['invoice-classification', 'statistics'] as const,` line from `CLASSIFICATION_QUERY_KEYS` (currently line 32).

Do **not** touch:
- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IClassificationHistoryRepository.cs`
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/InvoiceClassificationModule.cs`
- `frontend/src/pages/InvoiceClassification/components/ClassificationStats.tsx` and its usage in `InvoiceClassificationPage.tsx` (out of scope — see amendments)
- Any EF migration, `ClassificationHistoryConfiguration.cs`, or the `ClassificationHistory` entity

### Interfaces and Contracts

No interface or contract signatures change. `IClassificationHistoryRepository` keeps its existing four methods verbatim. No new DTOs, no new MediatR requests. The OpenAPI-generated TypeScript client will simply stop containing `ClassificationStatisticsDto`/`RuleUsageStatisticDto` typings on next generation — since no generated method ever referenced them (no controller endpoint existed), this is inert.

### Data Flow

N/A — no data flow exists today for this dead branch, and none is being added or changed. The real data flows (`GetPagedHistoryAsync` → `GetClassificationHistoryHandler` → `InvoiceClassificationController` → `useInvoiceClassificationHistory` hook) are unaffected.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Hidden reference missed by spec's file list, causing a compile break | Low | Independently grepped `backend/src`, `backend/test`, `frontend/src` for all 5 symbol names — confirmed matches exist only in the 5 files spec identifies plus the mapping profile's 2 lines. Re-run the same grep as a final gate before merge (FR-6 already mandates this). |
| `dotnet build`/`npm run build` catches a dangling reference only after all deletions are made, causing churn | Low | Delete in this order to catch mistakes early: (1) mapping profile lines first — if a hidden reference to the DTOs exists elsewhere, the build will fail loudly there before types are deleted; (2) then delete the DTOs and domain types; (3) then delete the repository method; (4) then the frontend query-key line. Since this is one commit, order only affects developer feedback speed, not the final diff. |
| Reviewer conflates this cleanup with the separate `ClassificationStats.tsx` mock-data UI issue and scope-creeps the PR | Low-Medium | Explicitly called out in this review and recommended as a separate follow-up (see Specification Amendments) — keep this PR to exactly the 4 file deletions + 2 edits above. |
| None of the removed types are EF-mapped, so no migration risk exists | N/A (verified, not a real risk) | Confirmed: neither `ClassificationStatistics` nor `RuleUsageStatistic` appears in `ClassificationHistoryConfiguration.cs` or any `Persistence/Migrations/*.cs` file. |

## Specification Amendments

1. **File a separate follow-up finding for `ClassificationStats.tsx`.** `frontend/src/pages/InvoiceClassification/components/ClassificationStats.tsx` renders a `mockStats` object (`// TODO: Replace with actual API call`) inside a live "Statistiky klasifikace" modal (`InvoiceClassificationPage.tsx`, `showStatsModal` state, ~line 275). Real users see fabricated numbers (150 processed, 80% success rate, etc.) that never change. This is a different defect than the one this spec fixes — it's user-facing and would need a design decision (remove the modal entirely vs. wire it to a real endpoint) — so it should **not** be folded into this PR. Recommend opening a new arch-review issue for it; that issue would need `Skip Design: false` since it involves a user-visible UI decision.
2. **No other amendments.** FR-1 through FR-6 are accurate as written and match what's in the repository; the line ranges in FR-1 (81–121) match exactly. No hidden usages, no EF/migration coupling, no test coverage of the removed members. The spec's Out of Scope section is correctly drawn — it explicitly excludes `InvoiceImportStatistics` (a real, unrelated Analytics-module feature that happens to share the word "statistics") and the four repository methods that stay.

## Prerequisites

None. This is a same-commit, no-migration, no-config, no-infrastructure change. The only gates are the ones the spec already states in FR-6: `dotnet build`, `dotnet format`, `npm run build`, `npm run lint`, the full existing test suite (no tests need to change, since none reference the removed members), and a final zero-match grep for all five symbol names across `backend/src`, `backend/test`, `frontend/src`.
