# Specification: Fix Recurring Jobs Test Failures — Investigate 24 vs 12 Job Count Discrepancy

## Summary
E2E test suite asserts exactly 12 recurring jobs, but staging returns 24 (2x duplication). This investigation must determine whether jobs are genuinely duplicated in the database/UI (requiring a bug fix) or whether 24 is the correct count (requiring test assertion updates). All four assertion points must align with the root cause, and the spec passes.

## Background
Nightly E2E test run 28147951139 (2026-06-25) flagged 4 failing assertions in `frontend/test/e2e/core/recurring-jobs-management.spec.ts`:
- Line 46: `expect(rowCount).toBe(12)` — Main list view row count
- Line 215: `expect(rowCount).toBe(12)` — Refresh button test  
- Line 296: `expect(buttonCount).toBe(12)` — Toggle buttons for accessibility
- Line 693: `expect(buttonCount).toBe(12)` — Run Now buttons

All four consistently fail with actual count = 24. This 2x factor suggests either:
1. **Real UI/data duplication bug** — Jobs rendered twice due to query or rendering logic
2. **Test brittleness** — Hard-coded count no longer matches staging's actual job inventory

Acceptance requires root cause documentation and either duplication fix or robust assertion replacement.

## Functional Requirements

### FR-1: Investigate Root Cause
Determine whether the 24-job count is a real issue or test data reality.

**Investigation steps:**
- Open staging recurring-jobs page in a browser
- Count distinct jobs visually and by job name uniqueness
- If duplicates are visible: same job name appears 2+ times in table → Bug exists
- If no duplicates: 24 distinct jobs → Test count outdated, data changed

**Acceptance criteria:**
- Root cause documented in issue with evidence (browser screenshots or API response JSON)
- Clear statement: "Each job is rendered twice (duplication bug)" OR "There are genuinely 24 distinct jobs"

### FR-2: Fix Duplication (If Bug Path)
If jobs are duplicated in the UI or API response, fix the root cause.

**Possible bug locations:**
- Backend: `GetRecurringJobsListHandler` or `RecurringJobConfigurationRepository.GetAllAsync()` — returning duplicates
  - Check for `Distinct()` missing on queries
  - Verify no joins creating Cartesian product
- Frontend: `RecurringJobsPage.tsx` line 218 `{jobsList.map((job) => ...)}` — mapping duplicates
  - Check if `useRecurringJobsQuery()` returns dupes
  - Verify no accidental double-fetches or stale data merging
- Database: Duplicate entries in `RecurringJobConfigurations` table
  - Check for failed migration rollback leaving orphaned records
  - Verify unique constraints on `JobName`

**Acceptance criteria:**
- Duplicate issue fixed at source
- Backend/frontend deduplication code added/corrected
- All tests pass against staging
- `bug` label remains on issue

### FR-3: Update Test Assertions (If Real Data Path)
If 24 is the correct count, replace hard-coded assertions with robust alternatives.

**Alternative assertion approaches:**
- **Option A (Count threshold)**: `expect(rowCount).toBeGreaterThanOrEqual(12)` — allows future additions
- **Option B (Distinct job names)**: Count unique `displayName` or `jobName` fields, assert minimum set is present
- **Option C (Seeded jobs by name)**: Assert all 24 seeded job names are present (robust to future changes)

**Code locations to update:**
1. Line 46: `should display all 12 recurring jobs` test
2. Line 215: `should refresh jobs list when clicking refresh button` test
3. Line 296: `should have proper accessibility attributes on toggle buttons` test
4. Line 693: `should have proper accessibility attributes on Run Now buttons` test

**Acceptance criteria:**
- All four assertions replaced with count-agnostic logic
- Assertions clearly document the expected job set (comment explaining why)
- Tests pass when run against staging
- `bug` label removed from issue (indicates no product bug found)

### FR-4: Validate Against Staging
Ensure all changes work correctly on staging environment.

**Acceptance criteria:**
- `./scripts/run-playwright-tests.sh core` passes completely
- Specifically, `recurring-jobs-management.spec.ts` runs with 0 failures
- No flakiness observed when run multiple times

## Non-Functional Requirements

### NFR-1: Test Stability
Assertions must not fail again due to brittleness or minor data changes.

- Replace magic numbers with semantic or dynamic assertions
- Avoid assuming exact job count; use relational assertions when possible
- Document the job set being tested (e.g., "12 core jobs" vs "24 imported + core jobs")

### NFR-2: Root Cause Documentation
Issue body must clearly state the investigation outcome.

- If bug found: Describe the duplication mechanism, affected component, and fix applied
- If not a bug: Explain why 24 is correct and how the test now handles scale

### NFR-3: Code Clarity
Test code must be maintainable for future developers.

- Add comments explaining why each assertion choice was made
- Link to this spec or investigation doc if count logic is non-obvious

## Data Model
No schema changes required. Investigation only reads:
- `RecurringJobConfigurations` table (backend persistence)
- API response `GetRecurringJobsListResponse.jobs` (frontend)
- DOM table rows (Playwright E2E)

## API / Interface Design
No API changes. All endpoints unchanged:
- `GET /api/recurringjobs` — Returns `GetRecurringJobsListResponse { Jobs: RecurringJobDto[] }`

If backend bug is found, fix must preserve response contract.

## Dependencies
- Hangfire (backend job metadata)
- Playwright (E2E automation)
- React Query (frontend data fetching via `useRecurringJobsQuery()`)

## Out of Scope
- Job execution behavior or performance
- Adding/removing jobs from the system
- Cron expression validation or editing
- Manual job trigger functionality
- Status toggle behavior

This investigation is **data count only**, not feature behavior.

## Open Questions
1. **Is the duplication confirmed on staging?** Needs manual verification by opening `/recurring-jobs` page in browser.
2. **Database constraint on JobName?** Does `RecurringJobConfigurations` table have a `UNIQUE` constraint on `JobName`, or can duplicates exist at DB level?
3. **What is the expected job count post-investigation?** Is 12 a stale target, or should it remain 12 with duplicates being a real bug?
4. **Data seeding vs migrations:** Are the 24 jobs from seed data (test fixtures) or from migrations? This affects whether the count is environment-specific (staging = 24, prod = 12).

## Status: HAS_QUESTIONS
