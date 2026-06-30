# Architecture Review: Fix Recurring DateTime Kind=Unspecified Crash in FlexiAnalyticsSyncJob

## Skip Design: true

## Architectural Fit Assessment

This is a targeted bug fix inside the `Anela.Heblo.Adapters.Flexi` project — specifically the `Analytics` namespace. No layer boundaries are crossed, no interfaces change, no schema migrations are needed, and no module contracts are touched. The fix is entirely within four `Map()` static methods, one options helper method, and their corresponding unit tests.

The root contract to respect is already established in the codebase: `UnspecifiedDateTimeConverter` and `DateTimeLocalKindConverter` both use `TimeZoneInfo.ConvertTimeToUtc(value, TimeZoneInfo.Local)` as the canonical Prague-local-to-UTC conversion. The sync services' `Map()` methods bypass these converters because they operate on `DateTime?` fields that come through the SDK directly rather than through JSON deserialization or AutoMapper. The fix brings the `Map()` methods into alignment with that canonical pattern.

The spec is accurate. I verified the actual code: `LedgerSyncService`, `ContactSyncService`, `DepartmentSyncService`, and `AccountingTemplateSyncService` all call `.ToUniversalTime()` directly on `dto.LastUpdate`. `DepartmentSyncService` and `AccountingTemplateSyncService` use a slightly different guard pattern (`dto.LastUpdate == default ? null : (DateTimeOffset?)dto.LastUpdate.ToUniversalTime()`) because `DepartmentFlexiDto.LastUpdate` and `AccountingTemplateFlexiDto.LastUpdate` are non-nullable `DateTime`, not `DateTime?`. The replacement pattern must account for this structural difference.

The existing unit tests use `DateTimeKind.Utc` test inputs in `MakeContactDto`, `MakeDepartmentDto`, and `MakeTemplateDto` helper methods — meaning the current tests do not exercise the `Kind=Unspecified` crash path at all. They pass today purely because the test inputs already carry `Kind=Utc`. The new tests must provide `Kind=Unspecified` inputs.

`GetInitialBackfillDateTime()` has the secondary bug described in FR-4: `.Date` strips `Kind` back to `Unspecified` before `.ToUniversalTime()` re-applies it, producing a one-to-two-hour shift in Prague local time. The existing `FlexiAnalyticsSyncOptionsTests` only asserts `Kind=Utc` — it does not assert that the returned value equals UTC midnight. That test must be extended.

The primary remediation step (FR-2, triggering the already-merged deployment) is a pipeline action, not a code change. The developer must manually approve the `deploy-production` environment gate in the `ci-main-branch.yml` workflow. This is outside the scope of code review but is listed as the highest-priority action.

## Proposed Architecture

### Component Overview

All changes are confined to two directories:

```
backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/
  FlexiAnalyticsSyncOptions.cs        ← FR-4: fix GetInitialBackfillDateTime()
  LedgerSyncService.cs                ← FR-3: fix Map() LastModified conversion
  ContactSyncService.cs               ← FR-3: fix Map() LastModified conversion
  DepartmentSyncService.cs            ← FR-3: fix Map() LastModified conversion
  AccountingTemplateSyncService.cs    ← FR-3: fix Map() LastModified conversion

backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/
  FlexiAnalyticsSyncOptionsTests.cs   ← FR-4/FR-5: extend existing test
  LedgerSyncServiceTests.cs           ← FR-5: add Kind=Unspecified tests
  ContactSyncServiceTests.cs          ← FR-5: add Kind=Unspecified tests
  DepartmentSyncServiceTests.cs       ← FR-5: add Kind=Unspecified tests
  AccountingTemplateSyncServiceTests.cs ← FR-5: add Kind=Unspecified tests
```

Nothing outside these two directories is touched. No new files need to be created — all tests go into existing test classes.

### Key Design Decisions

#### Decision 1: Use `TimeZoneInfo.ConvertTimeToUtc` directly in `Map()`, not a shared helper

**Options considered:**
- A) Extract a shared static utility method (e.g., `FlexiDateTimeHelper.ToUtc(DateTime? dt)`) in the `Analytics` namespace and call it from all four services.
- B) Apply `TimeZoneInfo.ConvertTimeToUtc(value, TimeZoneInfo.Local)` inline at each `Map()` call site.

**Chosen approach:** B — inline conversion at each call site.

**Rationale:** The codebase has no existing shared utility class for this pattern; creating one for four two-line conversions adds a file and an abstraction without meaningful reuse. The canonical pattern already exists in `UnspecifiedDateTimeConverter` and `DateTimeLocalKindConverter` — those are the template to follow, and both apply the conversion inline. A new helper would be a fourth copy of the same logic with no clear ownership. Inline application is surgical (matches the "touch only what the task requires" rule) and makes each `Map()` method independently auditable.

#### Decision 2: Return type of `Map()` remains `DateTime?`, not `DateTimeOffset?`

**Options considered:**
- A) Change entity `LastModified` properties from `DateTime?` to `DateTimeOffset?`.
- B) Keep `DateTime?` and ensure `Kind=Utc` via explicit conversion.

**Chosen approach:** B — keep `DateTime?` with `Kind=Utc`.

**Rationale:** The entity properties (`LedgerEntry.LastModified`, `Contact.LastModified`, etc.) are mapped to `timestamptz` columns via `AnalyticsDbContext`. Changing the CLR type to `DateTimeOffset?` would require an EF Core mapping change and a migration. The spec explicitly states no schema changes and no migrations. `DateTime` with `Kind=Utc` satisfies Npgsql 6+ exactly the same as `DateTimeOffset` for `timestamptz` writes. Scope-creep into entity type changes is out of bounds for a bug fix.

#### Decision 3: `GetInitialBackfillDateTime()` fix uses `DateTimeOffset` parse path (Option B from spec)

**Options considered:**
- A) Drop `.Date`: `DateTime.Parse(..., DateTimeStyles.AssumeUniversal).ToUniversalTime()`
- B) Parse via `DateTimeOffset`: `DateTimeOffset.Parse(..., DateTimeStyles.AssumeUniversal).UtcDateTime`

**Chosen approach:** B — `DateTimeOffset.Parse(...).UtcDateTime`.

**Rationale:** Option A still uses `DateTime.Parse` with `AssumeUniversal`, which produces `Kind=Utc` directly from the parse (no `.ToUniversalTime()` needed), but relies on subtle `AssumeUniversal` behaviour. Option B is semantically unambiguous: `DateTimeOffset` carries offset information by definition, and `.UtcDateTime` on a `DateTimeOffset` always produces `Kind=Utc`. It matches the pattern the codebase already uses for watermark handling (`DateTimeOffset.UtcNow`, `state.Watermark.Value.AddHours(-1).UtcDateTime`). It is the preferred option per the spec. The return type remains `DateTime` (the `ILedgerClient.GetChangedSinceAsync` call site accepts `DateTime`).

#### Decision 4: `DepartmentSyncService` and `AccountingTemplateSyncService` non-nullable `DateTime` guard

**Options considered:**
- A) Use the same nullable pattern as `LedgerSyncService`/`ContactSyncService`.
- B) Use the existing `== default` guard pattern, replacing only the conversion call.

**Chosen approach:** B — preserve the existing null-guard structure, replace only `.ToUniversalTime()` with `TimeZoneInfo.ConvertTimeToUtc`.

**Rationale:** `DepartmentFlexiDto.LastUpdate` and `AccountingTemplateFlexiDto.LastUpdate` are non-nullable `DateTime`, so the `dto.LastUpdate == default` check is the correct structural guard for these services. Changing the guard to nullable syntax (`?.`) would require changing the DTO type or introducing an unnecessary cast. The current guard pattern is correct — only the conversion is wrong.

Current (fragile):
```csharp
LastModified = dto.LastUpdate == default ? null : (DateTimeOffset?)dto.LastUpdate.ToUniversalTime(),
```

Fixed:
```csharp
LastModified = dto.LastUpdate == default ? null : (DateTimeOffset?)TimeZoneInfo.ConvertTimeToUtc(dto.LastUpdate, TimeZoneInfo.Local),
```

## Implementation Guidance

### Directory / Module Structure

No structural changes. All changes are in-place edits to existing files. No new projects, namespaces, or classes are introduced.

### Interfaces and Contracts

No interface changes. `IEntitySyncService`, `IFlexiAnalyticsSyncService`, `ISyncWatermarkRepository`, `ILedgerClient` signatures are unchanged. `FlexiAnalyticsSyncOptions.GetInitialBackfillDateTime()` return type stays `DateTime`.

### Data Flow

The corrected flow for `dto.LastUpdate` through `Map()`:

```
SDK returns DateTime (Kind=Unspecified, value = Prague local time)
  → TimeZoneInfo.ConvertTimeToUtc(value, TimeZoneInfo.Local)
  → DateTime (Kind=Utc)
  → assigned to entity.LastModified (DateTime?)
  → EF Core / Npgsql writes to timestamptz column ✓
```

The corrected flow for `GetInitialBackfillDateTime()`:

```
"2020-01-01" (string)
  → DateTimeOffset.Parse(..., DateTimeStyles.AssumeUniversal)
  → DateTimeOffset(2020-01-01T00:00:00+00:00)
  → .UtcDateTime
  → DateTime(2020-01-01T00:00:00, Kind=Utc) ✓
  → passed to ILedgerClient.GetChangedSinceAsync(DateTime since, ...)
```

### Implementation order

Execute in this order to catch regressions as you go:

1. **FR-1 (investigate)** — Query Hangfire DB before writing any code. Document finding in a code comment above the fixed line in `LedgerSyncService.Map()`.
2. **FR-2 (deploy)** — Approve the `deploy-production` environment gate for the `ci-main-branch.yml` run that built commit `745dc72`. This is independent of code changes; do it before coding so the nightly job at 02:00 UTC can run clean.
3. **FR-4 (fix `GetInitialBackfillDateTime`)** — One-line change in `FlexiAnalyticsSyncOptions.cs`.
4. **FR-3 (fix all four `Map()` methods)** — Four targeted edits across the four sync service files.
5. **FR-5 (add regression tests)** — Add tests to all five existing test classes.

### Test specification (FR-5)

**`FlexiAnalyticsSyncOptionsTests`** — extend existing `GetInitialBackfillDateTime_ReturnsUtcKind`:
- Add a second assertion: `result` equals `new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)` (not `2023-12-31T23:00:00Z`).
- Add a second test `GetInitialBackfillDateTime_DateComponentMatchesConfiguredDate` with `InitialBackfillFrom = "2020-01-01"` asserting `result == new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)`.

**`LedgerSyncServiceTests`** — add two new facts:
- `Map_WhenLastUpdateIsUnspecifiedKind_ReturnsUtcDateTime`: call the `Map()` method (make it `internal` if needed, or test via `SyncAsync` with a controlled DTO) with a `LedgerItemFlexiDto` where `LastUpdate = new DateTime(2025, 6, 19, 10, 0, 0, DateTimeKind.Unspecified)`. Assert `result.LastModified` has `Kind=Utc` and equals `TimeZoneInfo.ConvertTimeToUtc(new DateTime(2025, 6, 19, 10, 0, 0), TimeZoneInfo.Local)`.
- `SyncAsync_WhenNoWatermark_PassesKindUtcToClient`: extend the existing no-watermark test to explicitly assert that the `DateTime` passed to `GetChangedSinceAsync` has `Kind=Utc`. This is already partially covered by the exact-value check in the existing test — add an explicit `Kind` assertion for clarity.

**`ContactSyncServiceTests`**, **`DepartmentSyncServiceTests`**, **`AccountingTemplateSyncServiceTests`** — add one fact each:
- `Map_WhenLastUpdateIsUnspecifiedKind_ReturnsUtcLastModified`: update the corresponding `MakeXxxDto` helper (or create a variant) to use `DateTimeKind.Unspecified` input. Run `SyncAsync` and assert all persisted entities have `LastModified` that satisfies `Kind=Utc`. This is the minimum bar; a direct `Map()` invocation test is preferred if `Map()` can be made `internal static` with `[InternalsVisibleTo]`.

Note on `Map()` accessibility: all four `Map()` methods are currently `private static`. To test them directly without going through `SyncAsync` + in-memory EF, change visibility to `internal static` and add `[assembly: InternalsVisibleTo("Anela.Heblo.Adapters.Flexi.Tests")]` to the `Anela.Heblo.Adapters.Flexi` assembly. This is the preferred approach — direct unit tests are faster and more focused than integration-style tests through `SyncAsync`. However, testing through `SyncAsync` is an acceptable alternative if the project avoids `InternalsVisibleTo` by convention. Check existing test files — `SyncWatermarkRepository` is used directly from tests (it is `public`), suggesting `InternalsVisibleTo` is not already in use. Either approach is acceptable; document the choice with a comment.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Production container still uses pre-fix image even after FR-3/FR-4 merge; nightly crash recurs | High | FR-2 (deploy approval) must be executed before or immediately after the code fix merges. Verify Docker image tag in Azure Portal matches the new build. |
| `TimeZoneInfo.Local` on the container is not `Europe/Prague` — conversion produces wrong value | Medium | The container's `TZ` environment variable is `Europe/Prague` (as documented and confirmed by the existing `UnspecifiedDateTimeConverter` comment). Verify `TZ` in Azure Web App App Settings if any doubt. The `TimeZoneInfo.Local` dependency is intentional and matches the established pattern; it is not a new risk introduced by this fix. |
| `DepartmentFlexiDto.LastUpdate == default` guard masks real null/zero values from SDK | Low | This guard predates this fix. The spec explicitly lists the `== default` guard as the correct pattern for non-nullable DTO fields. Do not change the guard logic. |
| Existing tests pass with `Kind=Utc` inputs and will not catch regressions if the fix is reverted | Medium | FR-5 tests must use `Kind=Unspecified` inputs. Code-review the test inputs in `MakeXxxDto` helpers to confirm they supply `Unspecified`. |
| `GetInitialBackfillDateTime()` change shifts the backfill window unexpectedly | Low | The FR-4 fix corrects a wrong offset shift introduced by #3243. The new value (`2020-01-01T00:00:00Z`) is the intended backfill date; the old value (`2019-12-31T23:00:00Z` in CEST) was wrong. The test extension confirms correctness. Since this is the initial backfill path (only runs when no watermark exists), no production data will be re-fetched on the next run — the watermark already exists in `flexi_raw.sync_state`. |
| Hangfire `data` JSON query in FR-1 reveals a different crash site than `LedgerSyncService.Map()` | Low | If a different crash site is found, expand the audit scope accordingly before coding FR-3. The hardening of all four `Map()` methods proceeds regardless of which one crashes first; FR-1 only determines whether additional paths exist. |

## Specification Amendments

None. The spec is complete and accurate. One clarification to document for the implementer:

The spec's FR-3 "required replacement pattern" shows `(DateTime?)null` as the nullable fallback for `LedgerSyncService` and `ContactSyncService`. This is correct for those two services where `dto.LastUpdate` is `DateTime?`. For `DepartmentSyncService` and `AccountingTemplateSyncService`, `dto.LastUpdate` is non-nullable `DateTime`, so the existing `== default ? null` guard structure is correct — do not convert it to the nullable-operator pattern. The spec's audit table covers this implicitly, but the implementation note makes it explicit.

## Prerequisites

- No migrations required.
- No new NuGet packages required (`TimeZoneInfo` is BCL).
- No DI registration changes required.
- FR-1 (Hangfire DB query) requires access to the production PostgreSQL instance. Confirm credentials are available before starting.
- FR-2 (deployment approval) requires the workflow run for commit `745dc72` or a later `main` commit to be in the approval-pending state in the `ci-main-branch.yml` Actions tab. If that run has expired or been cancelled, a new commit must be pushed to `main` to trigger a fresh pipeline run.
