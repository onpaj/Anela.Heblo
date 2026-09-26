# Architecture Review: BackgroundJobs — RecurringJobSeeder must not overwrite audit fields when nothing changed

## Skip Design: true

Backend-only correctness fix. No new/changed UI components, screens, or visual design decisions —
the Recurring Jobs page already renders `LastModifiedAt`/`LastModifiedBy` correctly; only the
seeder's write behavior changes.

## Architectural Fit Assessment

This is a narrow, well-isolated bug fix inside an existing Application-layer service
(`RecurringJobSeeder`, `Anela.Heblo.Application/Features/BackgroundJobs/Services/`). It touches
exactly one method (`SeedDefaultConfigurationsAsync`) and does not cross module boundaries: the
seeder already depends only on `IRecurringJobConfigurationRepository` (Domain interface,
Persistence implementation) and `TimeProvider`. No new dependency, no new interface, no new
cross-module call is needed.

The fix aligns with the existing Clean Architecture layering:
- **Domain** (`RecurringJobConfiguration`) already exposes the right mutator
  (`UpdateConfiguration`) — it correctly stamps audit fields as a side effect of a real change. The
  bug is not in the domain entity; it's that the **caller** (the Application-layer seeder) invokes
  that mutator unconditionally instead of only when something actually changed.
- **Application** (`RecurringJobSeeder`) is exactly where the "should I write?" decision belongs,
  because it's the only layer that has both the freshly-computed metadata and the stored entity in
  hand at the same time, before calling into the domain mutator.

This mirrors a common and already-precedented pattern in this codebase: guard a domain mutator call
with an equality check in the calling Application code when the mutator's own job is "apply this new
state and stamp audit metadata," and the caller's job is "decide whether new state was actually
supplied." No new pattern is introduced.

## Proposed Architecture

### Component Overview

```
RecurringJobDiscoveryService (or startup hook)
        │  discovers IRecurringJob[] at boot
        ▼
RecurringJobSeeder.SeedDefaultConfigurationsAsync(jobs)
        │
        │  for each job:
        │    existing = repository.GetByJobNameAsync(jobName)   [unchanged]
        │
        ├─ existing == null ──────────► repository.AddAsync(config)   [unchanged]
        │
        └─ existing != null
                │
                ▼
           *** NEW: HasSeededFieldsChanged(existing, config) ***
           (DisplayName / Description / TimeZoneId, ordinal compare)
                │
        ┌───────┴────────┐
        │ false (no diff)│ true (diff found)
        ▼                ▼
   do nothing        existing.UpdateConfiguration(...)   [unchanged call,
   (no UpdateAsync)  repository.UpdateAsync(existing)     unchanged args]
```

No new component. One new private helper method (or inlined boolean) inside `RecurringJobSeeder`.

### Key Design Decisions

#### Decision 1: Where does the change-check live?

**Options considered:**
1. Inline `bool` expression directly in the `foreach` loop (as sketched in the issue's suggested fix).
2. A small private `static bool HasSeededFieldsChanged(RecurringJobConfiguration existing, RecurringJobConfiguration config)` helper method on `RecurringJobSeeder`.
3. Push the comparison into the Domain entity itself, e.g. `existing.DiffersFrom(config)` or have `UpdateConfiguration` become a no-op internally when nothing changed (still stamping nothing).

**Chosen approach:** Option 2 — a small private static helper method on `RecurringJobSeeder`.

**Rationale:** Keeps the diff logic testable in isolation and out of the `foreach` body (improves
readability of the loop), without adding new public surface on the Domain entity for a comparison
that is specific to *this one caller's* notion of "seeded fields" (`DisplayName`, `Description`,
`TimeZoneId`). Option 3 was rejected: `RecurringJobConfiguration` should not need to know which of
its own fields count as "developer-owned for seeding purposes" — that's a policy of the seeder, not
an invariant of the entity. Silently making `UpdateConfiguration` a conditional no-op (part of
option 3) would also be surprising to its two other call-sites-adjacent methods (`Enable`, `Disable`,
`UpdateCronExpression` already always stamp — an inconsistent "sometimes stamps" `UpdateConfiguration`
would violate the principle of least surprise for any future caller). Option 1 (fully inline) was
rejected only for readability; it is functionally equivalent to option 2 and either is acceptable —
the plan should use option 2 for a clearly named, unit-testable seam.

## Implementation Guidance

### Directory / Module Structure

No new files. Modify:
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs` — add
  the guard and the private helper.
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs` — fix the one
  test that currently encodes the buggy behavior as expected, and add a test for the "nothing
  changed → audit fields preserved" case.

### Interfaces and Contracts

No public interface changes. The new helper is `private static bool`, internal to the class:

```csharp
private static bool HasSeededFieldsChanged(RecurringJobConfiguration existing, RecurringJobConfiguration config) =>
    existing.DisplayName != config.DisplayName
    || existing.Description != config.Description
    || existing.TimeZoneId != config.TimeZoneId;
```

(Plain C# `!=` on `string` is ordinal value equality — matches FR-1's "ordinal string equality"
requirement with no extra `StringComparer` ceremony needed.)

### Data Flow

Unchanged end-to-end except for the new branch:

1. Startup discovers `IRecurringJob[]` → builds `defaultConfigurations` from metadata (unchanged).
2. For each config: fetch `existing` by `JobName` (unchanged).
3. `existing == null` → `AddAsync` (unchanged).
4. `existing != null` → **NEW:** compute `HasSeededFieldsChanged(existing, config)`.
   - `false` → skip; do not call `UpdateConfiguration` or `UpdateAsync`. Row (including
     `LastModifiedAt`/`LastModifiedBy`) is left exactly as stored.
   - `true` → call `existing.UpdateConfiguration(config.DisplayName, config.Description,
     existing.CronExpression, config.TimeZoneId, "System", now)` and `repository.UpdateAsync(existing)`
     exactly as today.

`now` (`_timeProvider.GetUtcNow().UtcDateTime`) can remain computed once up front as today — it's
inexpensive and only consumed on the `true` branch; no need to defer it.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Existing test `SeedDefaultConfigurationsAsync_WhenConfigurationExists_SetsLastModifiedByToSystem` currently asserts the buggy behavior (LastModifiedBy always becomes "System") and will fail once the fix lands, because its fixture has all three seeded fields already matching metadata. | Low | Update this test as part of the same change: rename/repurpose it to assert `LastModifiedBy` stays `"Admin"` (its fixture's starting value) when nothing changed, matching FR-1. Spec already calls this out explicitly. |
| A future field is added to `IRecurringJob.Metadata` / `RecurringJobConfiguration` that should be developer-owned and seeded, but `HasSeededFieldsChanged` is not updated to include it — it would then be silently un-resynced. | Low | Not a new risk introduced by this change (the same class of omission already exists for `TimeZoneId` today, it's just that today *every* field change happens to trigger a write regardless). Out of scope to add a compile-time safeguard; note only. |
| String comparison uses `!=` (ordinal) — if any seeded field could differ only by culture-sensitive casing/normalization in a way that should NOT count as a change, ordinal comparison would over-trigger. | Negligible | These fields come from compiled-in constants (`RecurringJobMetadata`), not user/locale input, so ordinal equality is exactly correct and matches how `RecurringJobConfiguration.UpdateConfiguration`'s own internal state is compared nowhere else in the codebase (no existing precedent to contradict). |

## Specification Amendments

None. The spec's FR-1/FR-2 and the "Dependencies" section's call-out of the existing misleading test
are sufficient to implement directly.

## Prerequisites

None. No migration, no config, no infrastructure change. Pure code change plus test update.
