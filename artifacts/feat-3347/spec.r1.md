# Specification: Fix Recurring Jobs E2E Tests — Hard-coded Count 12 vs. Actual 24

## Summary

The recurring-jobs E2E suite fails on staging because four test assertions hard-code `12` rows/buttons while the staging database now contains 24 recurring job configurations. Root-cause investigation confirms this is **not a rendering duplication bug**: the application genuinely has 24 registered `IRecurringJob` implementations (12 that existed when the tests were written, plus 12 added since). The fix is to replace every brittle `toBe(12)` assertion with a robust count that matches the real set of jobs.

## Background

`frontend/test/e2e/core/recurring-jobs-management.spec.ts` was written when the application had exactly 12 recurring jobs. Since then, 12 additional jobs have been added to the codebase:

- **DataQuality group:** `InvoiceDqtJob`, `ProductPairingDqtJob`, `StockWriteBackDqtJob`
- **Photobank group:** `PhotobankAutoTagJob`, `PhotobankIndexJob`
- **MeetingTasks:** `PlaudPollingJob`
- **Leaflet:** `LeafletIngestionJob`
- **Packaging:** `FillTrackingNumbersJob`
- **Smartsupp:** `SmartsuppWebhookAuditCleanupJob`
- **Adapters (explicitly registered):** `FlexiAnalyticsSyncJob`, `MetaAdsInvoiceImportJob`, `GoogleAdsInvoiceImportJob`

The `GET /api/recurring-jobs` endpoint reads from the `recurring_job_configurations` database table, which is seeded on every application startup from all discovered `IRecurringJob` implementations (`SeedRecurringJobConfigurationsAsync` in `Program.cs` / `ServiceCollectionExtensions.cs`). The seeding is additive (only inserts missing rows), so the table now contains exactly 24 rows on staging — one per registered job. The UI renders one `<tr>` per row, giving 24 rows and 24 `button[role="switch"]` elements.

The `expectedJobs` array on line 223 of the test file lists the original 12 jobs and the "display correct job names" test still passes (all 12 expected names are present in the larger set). The four count assertions at lines 46, 215, 296, and 693 are what fail.

**The `bug` label should be removed.** There is no duplication; the job count legitimately grew.

## Functional Requirements

### FR-1: Replace hard-coded row count assertions

Replace `expect(rowCount).toBe(12)` on lines 46 and 215 with assertions that are resilient to future job additions while still verifying that the page loads a non-trivial, complete list.

**Strategy:** Assert `toBeGreaterThanOrEqual(24)` to document the current minimum while tolerating future growth. Do not use `toEqual` with the exact current count (24) because that would just recreate the same brittleness.

**Acceptance criteria:**
- Line 46 (`should display all 12 recurring jobs`): assertion passes when `rowCount >= 24`.
- Line 215 (`should refresh jobs list when clicking refresh button`): assertion passes when `rowCount >= 24`.
- Test name at line 37 is updated from `'should display all 12 recurring jobs'` to `'should display all recurring jobs'`.

### FR-2: Replace hard-coded toggle-button count assertion

Replace `expect(buttonCount).toBe(12)` on line 296 (`should have proper accessibility attributes on toggle buttons`) with `toBeGreaterThanOrEqual(24)`.

**Acceptance criteria:**
- The assertion passes when there are >= 24 `button[role="switch"]` elements on the page.
- The loop that checks ARIA attributes on each button is unchanged (it already iterates dynamically over `buttonCount`).

### FR-3: Replace hard-coded Run Now button count assertion

Replace `expect(buttonCount).toBe(12)` on line 693 (`should have proper accessibility attributes on Run Now buttons`) with `toBeGreaterThanOrEqual(24)`.

**Acceptance criteria:**
- The assertion passes when there are >= 24 "Run Now" text elements on the page.

### FR-4: Document root cause in issue

Add a comment block at the top of the spec file (or in the issue itself via `gh issue comment`) that records the investigation outcome: count grew from 12 to 24 due to 12 new `IRecurringJob` implementations added since the tests were first written. This prevents future engineers from re-investigating the same question.

**Acceptance criteria:**
- A code comment in the test file above the `test.describe` block explains why `>= 24` is used.

## Non-Functional Requirements

### NFR-1: Test resilience

All four count assertions must remain valid after any future `IRecurringJob` implementation is added without touching the test file, as long as the new job count remains >= 24.

### NFR-2: No regression on existing test logic

Tests that do not depend on an exact count (toggle behaviour, dialog flows, accessibility attribute checks on each item, persistence after refresh, etc.) must be left unchanged. The dynamic loop in `should have proper accessibility attributes on toggle buttons` already handles variable counts correctly and requires no modification.

## Data Model

No changes to the data model. The `recurring_job_configurations` table is the source of truth; its row count is determined by the set of registered `IRecurringJob` implementations at startup.

Current registered jobs (24 total):

| # | Class | Assembly |
|---|-------|----------|
| 1 | `ComgateCzkImportJob` | Application |
| 2 | `ComgateEurImportJob` | Application |
| 3 | `DailyConsumptionJob` | Application |
| 4 | `DailyInvoiceImportCzkJob` | Application |
| 5 | `DailyInvoiceImportEurJob` | Application |
| 6 | `FillTrackingNumbersJob` | Application |
| 7 | `InvoiceClassificationJob` | Application |
| 8 | `InvoiceDqtJob` | Application |
| 9 | `KnowledgeBaseIngestionJob` | Application |
| 10 | `LeafletIngestionJob` | Application |
| 11 | `PhotobankAutoTagJob` | Application |
| 12 | `PhotobankIndexJob` | Application |
| 13 | `PlaudPollingJob` | Application |
| 14 | `PrintPickingListJob` | Application |
| 15 | `ProductExportDownloadJob` | Application |
| 16 | `ProductPairingDqtJob` | Application |
| 17 | `ProductWeightRecalculationJob` | Application |
| 18 | `PurchasePriceRecalculationJob` | Application |
| 19 | `ShoptetPayImportJob` | Application |
| 20 | `SmartsuppWebhookAuditCleanupJob` | Application |
| 21 | `StockWriteBackDqtJob` | Application |
| 22 | `FlexiAnalyticsSyncJob` | Adapters.Flexi (conditional on AnalyticsDatabase config) |
| 23 | `GoogleAdsInvoiceImportJob` | Adapters.GoogleAds |
| 24 | `MetaAdsInvoiceImportJob` | Adapters.MetaAds |

Note: `FlexiAnalyticsSyncJob` is only registered when `AnalyticsDatabase:ConnectionString` is set. If staging has this configured, the count is 24; if not, it would be 23. The brief states staging returns exactly 24, so the analytics connection string is set on staging.

## API / Interface Design

No API changes. The existing `GET /api/recurring-jobs` endpoint returns all rows from `recurring_job_configurations`, ordered by `job_name`. The frontend renders one table row per item. No changes are needed in the backend or frontend.

**Changes are confined to:**
- `frontend/test/e2e/core/recurring-jobs-management.spec.ts` — four assertion changes and one test-name change.

### Exact changes required

**Line 37** — rename test:
```
'should display all 12 recurring jobs'
→
'should display all recurring jobs'
```

**Line 46** — row count assertion:
```typescript
expect(rowCount).toBe(12);
→
expect(rowCount).toBeGreaterThanOrEqual(24);
```

**Line 215** — post-refresh row count assertion:
```typescript
expect(rowCount).toBe(12);
→
expect(rowCount).toBeGreaterThanOrEqual(24);
```

**Line 296** — toggle button count assertion:
```typescript
expect(buttonCount).toBe(12);
→
expect(buttonCount).toBeGreaterThanOrEqual(24);
```

**Line 693** — Run Now button count assertion:
```typescript
expect(buttonCount).toBe(12);
→
expect(buttonCount).toBeGreaterThanOrEqual(24);
```

Add a comment block immediately before line 4 (`test.describe('Recurring Jobs Management'`):
```typescript
// NOTE: The recurring jobs count grows as new IRecurringJob implementations are added.
// As of 2026-06-25, staging has 24 jobs (12 original + 12 added since initial test authoring).
// Assertions use toBeGreaterThanOrEqual(24) so tests survive future additions without modification.
// To update the minimum, check SELECT COUNT(*) FROM recurring_job_configurations on staging.
```

## Dependencies

- No backend changes required.
- No frontend production code changes required.
- The fix depends only on the E2E test file.
- Running the tests requires access to the staging environment (`./scripts/run-playwright-tests.sh`).

## Out of Scope

- Adding new jobs or removing existing jobs from the application.
- Changing the UI or the API.
- Updating the `expectedJobs` array (lines 223–235) — it asserts that the original 12 jobs are still present, which is correct and should remain as a regression guard.
- Investigating whether any of the 24 jobs should be removed or consolidated.
- Fixing the `FlexiAnalyticsSyncJob` conditional registration (it is intentional).

## Open Questions

None.

## Status: COMPLETE
