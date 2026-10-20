# Marketing Core C2 — Sync Orchestration, Change Detection, Metabase Views Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn every registered `IAdPlatformReadSource` into live rows in the `ads` schema (accounts, entities, daily facts, search terms, change events with *Heblo* / *out-of-band* origin), expose them to Metabase through granted `v_ads_*` views and to agents through MediatR read handlers.

**Architecture:** Two Hangfire recurring jobs in the Application assembly (`AdsDailySyncJob` 05:30, `AdsChangeSyncJob` hourly) drive small, single-purpose services in `Features/MarketingAds/Sync/` that upsert by natural key into C1's `AdsDbContext`, one platform/account/stream at a time, each isolated by a try/catch that records `sync_state`. Change events come from the platform change log when the source supports it, otherwise from a snapshot diff of entity state; a pure matcher classifies each event against Heblo's executed actions (`IAdExecutionLookup`, no-op until C3). Read handlers query `AdsDbContext` through `IAdsReadRepository`; Metabase reads idempotent SQL views granted to `metabase_ro`.

**Tech Stack:** .NET 8, EF Core 8 + Npgsql, MediatR 12, Hangfire 1.8, xUnit + FluentAssertions + Moq, EF InMemory for unit tests, Testcontainers PostgreSQL (`Category=Integration`) for SQL/persistence tests, PostgreSQL 16 SQL views.

**Spec:** `docs/superpowers/specs/2026-10-07-marketing-agents-platform-design.md` (sections 3, 4.1, 5, 12 are binding) + handoff `docs/handoff/marketing-agents-platform.md`.

---

## Before you start

1. Read the spec (whole file, especially §3, §4, §5, §12) and the handoff (§2, §3). Read `CLAUDE.md`, `docs/architecture/metabase.md`, ADR-007 in `docs/architecture/development_guidelines.md`, `docs/processes/_TEMPLATE.md` and `docs/processes/_TEMPLATE_MODULE.md`.
2. **Prerequisite: PR C1 is merged on `origin/main`.** Verify:

   ```bash
   git fetch origin
   git ls-tree -r origin/main --name-only | grep -E \
     "Persistence.Ads/AdsDbContext.cs|MarketingAds/Contracts/IAdPlatformReadSource.cs|MarketingAds/Contracts/AdSettingsGuard.cs|MarketingAds.TestKit/FakeAdPlatformReadSource.cs"
   ```

   Expected: all four paths printed. If any is missing, **stop** — C1 is not merged; report and do not start.
3. Create the branch from current main (you are in a fresh worktree):

   ```bash
   git checkout -b feature/marketing-core-c2-sync-detection origin/main
   ```
4. Podman is needed only for `Category=Integration` tests: `podman machine start` (docker is aliased to podman). CI excludes those tests; you must run them locally before the PR.

### Build / test commands used throughout

Concurrent worktrees contend for the compiler server, so always build first, then test with `--no-build`:

```bash
# from the repo root
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~Features.MarketingAds.<TestClass>"
```

"Run the test (RED)" therefore means: run the build — **expected: compile errors `CS0246`/`CS0103` naming the type you have not written yet** — that is the failing state. If a test fails with a stack trace that contradicts the source you can read, the binaries are stale after a concurrent build: `touch` the test file and rebuild.

---

## C1 reconciliation (read this before Task 1)

The spec fixes the contract types (§12.2) and table/column names (§4.1) but not the C# shape of the `ads` entities. The shape below was checked against the C1 plan (`docs/superpowers/plans/2026-10-07-marketing-core-c1-contracts.md`, Task 3) when this plan was written; C1's merged code is still the authority. C1 also ships string vocabularies `AdSyncStreams`, `AdChangeSources`, `AdChangeOrigins` and `MarketingAdsModule` (with `ConnectionStringKey`) — this plan's enums produce the same text values. Task 1 Step 1 makes you compare it with the real C1 code. **If C1 differs, rename consistently in every code block of this plan; the logic does not change.** All enum↔column conversion goes through one file (`Sync/AdsDbValues.cs`) so a different column representation is a one-file change.

| Assumed (this plan) | Table / columns (spec §4.1) |
|---|---|
| `AdsDbContext` (ns `Anela.Heblo.Persistence.Ads`), ctor `AdsDbContext(DbContextOptions<AdsDbContext>)`, `const string SchemaName = "ads"` | schema `ads` |
| `DbSet<AdAccount> Accounts` — `long Id`, `string Platform`, `string ExternalId`, `string Name`, `string Currency`, `string TimeZone`, `bool IsManaged`, `DateTimeOffset CreatedAt`, `DateTimeOffset UpdatedAt` | `ad_accounts` |
| `DbSet<AdEntity> Entities` — `long Id`, `long AccountId`, `string Level`, `string ExternalId`, `long? ParentId`, `string Name`, `string Status`, `string AttributesJson` (jsonb), `DateTimeOffset FirstSeenAt`, `DateTimeOffset LastSeenAt`, `DateTimeOffset UpdatedAt` | `ad_entities` (`attributes` jsonb) |
| `DbSet<AdDailyFact> DailyFacts` — `long EntityId`, `DateOnly Date`, `long Impressions`, `long Clicks`, `decimal Cost`, `decimal Conversions`, `decimal ConversionValue`, `string Currency`, `DateTimeOffset SyncedAt` | `ad_daily_facts` PK (`entity_id`,`date`) |
| `DbSet<AdSearchTermDaily> SearchTermsDaily` — `long AdGroupEntityId`, `DateOnly Date`, `string SearchTerm`, `string MatchType`, same metrics, `string Currency`, `DateTimeOffset SyncedAt` | `ad_search_term_daily` PK (`ad_group_entity_id`,`date`,`search_term`,`match_type`) |
| `DbSet<AdChangeEvent> ChangeEvents` — `long Id`, `long AccountId`, `string ExternalEventId`, `DateTimeOffset OccurredAt`, `string? Actor`, `string ActorKind`, `long? EntityId`, `string? EntityExternalRef`, `string ChangeType`, `string? OldValueJson`, `string? NewValueJson` (jsonb), `string Source`, `string Origin`, `string? MatchedExecutionId` (C3 execution id as text), `DateTimeOffset SyncedAt` | `ad_change_events` (`old_value`,`new_value` jsonb) |
| `DbSet<AdSyncState> SyncStates` — `string Platform`, `string AccountExternalId`, `string Stream`, `DateTimeOffset? Watermark`, `string? Status`, `DateTimeOffset? LastSuccessAt`, `string? LastError`, `DateTimeOffset UpdatedAt` | `sync_state` |
| Enum-valued columns hold the **enum member name** (`"GoogleAds"`, `"Campaign"`, `"Paused"`). Search-term `match_type` holds `"Unknown"` (`AdSearchTermDaily.UnknownMatchType`) when the platform reports none (it is part of the PK, so it cannot be NULL). | |

Stop-and-ask conditions (do not code around them):

- `backend/src/Anela.Heblo.Persistence.Ads/*.csproj` references `Anela.Heblo.Application` → the Application layer cannot use `AdsDbContext`; this plan's layering is impossible. Report.
- C1 stores `attributes`/`old_value`/`new_value` as `JsonDocument` rather than `string` → EF InMemory tests in this plan will not work; report.

---

## Global Constraints

- Contracts namespace `Anela.Heblo.Application.Features.MarketingAds.Contracts`, one type per file; core module in `backend/src/Anela.Heblo.Application/Features/MarketingAds/` (`MarketingAdsModule.cs`); persistence `backend/src/Anela.Heblo.Persistence.Ads/` (spec §12.1).
- Contract enums (C1, binding): `AdPlatform { GoogleAds = 1, MetaAds = 2, Sklik = 3 }`, `AdEntityLevel { Campaign = 1, AdGroup = 2, Keyword = 3, NegativeKeyword = 4, Ad = 5 }`, `AdEntityStatus { Unknown = 0, Enabled = 1, Paused = 2, Removed = 3 }`, `KeywordMatchType { Exact = 1, Phrase = 2, Broad = 3 }`, `AdChangeActorKind { Unknown = 0, Heblo = 1, User = 2, PlatformAutomation = 3 }`, `AdActionType { AddNegativeKeyword = 1, PauseAd = 2 }`; `AdActionValues.Paused = "Paused"`, `AdActionPayloadKeys.Text = "text"`.
- `AdsDailySyncJob`: 05:30 Europe/Prague, `[AutomaticRetry(Attempts = 0)]`; yesterday **plus a 14-day lookback** (`Ads:FactLookbackDays` = 14).
- `AdsChangeSyncJob`: hourly.
- Out-of-band: `origin = Heblo` only when the event matches an executed proposal action — same entity, same change, within **±2 h** of execution, and (when the platform reports an actor) the actor is Heblo's platform user. Everything else `OutOfBand`.
- Snapshot-diff fallback compares `status`, `name`, `attributes`; writes `source = SnapshotDiff`, `actor_kind = Unknown`.
- Blended: revenue from `shoptet_raw` orders **net of VAT, excluding cancelled** ÷ ad cost from `ad_daily_facts` **at campaign level**, all platforms.
- Metabase views `v_ads_campaign_monthly`, `v_ads_blended_monthly`, `v_ads_change_events` granted to `metabase_ro`; never `GRANT … ON ALL TABLES`. No customer PII in `ads`.
- Facts are stored at every level; **every query aggregates a single level**.
- Sources throw on transport/auth errors; the core catches **per source** (one failing platform never blocks others).
- DTOs are classes (never records); every Application `*Response` inherits `BaseResponse`. No new `ErrorCodes` (reuse `ConfigurationError`, `InvalidDateRange`, `InvalidValue`, `RequiredFieldMissing`, `ResourceNotFound`).
- DateTimeOffset values written to `timestamptz` must be UTC (offset 0) — Npgsql rejects other offsets.
- Process changes ship with `docs/processes/` docs + `python3 scripts/process-docs/check.py index` in the same PR.
- Commits: conventional (`feat:`, `test:`, `docs:`), message ends with a blank line and `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **Postgres `jsonb` normalises key order and whitespace.** Comparing the stored `attributes` text with a freshly serialised dictionary would report a change for every entity on every hourly run. Expected: no diff when only formatting differs. Pinned by `AdSnapshotDifferTests.Attribute_order_and_whitespace_do_not_count_as_a_change` (Task 2) and `AdsSyncPostgresIntegrationTests.Second_identical_run_writes_no_snapshot_diff_events` (Task 10).
2. **Platform timestamps with a non-UTC offset** (`2026-10-06T10:00:00+02:00`). Npgsql throws when writing them to `timestamptz`, which would fail the whole stream. Expected: stored as the same instant in UTC. Pinned by `AdChangeEventWriterTests.Stores_occurred_at_in_utc` (Task 6) and `AdsSyncPostgresIntegrationTests.Change_event_with_offset_timestamp_persists` (Task 10).
3. **No `AdsDatabase:ConnectionString` (local dev, CI, an environment before the KV secret exists).** `AddRecurringJobs()` registers every `IRecurringJob` in the Application assembly and `RecurringJobDiscoveryService` resolves them all in one `GetServices<IRecurringJob>()`; one unresolvable dependency would unschedule **every** job in Heblo. Expected: the two ads jobs resolve and log "skipped". Pinned by `MarketingAdsSyncWiringTests` (Task 9).
4. **Invalid `Ads` options** (e.g. `FactLookbackDays = 90`). Validating in the job constructor or with `ValidateOnStart` would break job discovery or app boot. Expected: the job run fails with a clear message; the app and other jobs are unaffected. Pinned by `AdsDailySyncServiceTests.Invalid_options_fail_the_run_not_construction` (Task 7) and `AdsDailySyncJobTests.Constructing_with_invalid_options_does_not_throw` (Task 9).
5. **Platform change values that are not valid JSON** (`PAUSED` bare). Inserting them into a `jsonb` column fails the batch. Expected: stored as a JSON string `"PAUSED"` and still matched. Pinned by `AdChangeEventWriterTests.Non_json_values_are_stored_as_json_strings` (Task 6).

---

## File structure

```
backend/src/Anela.Heblo.Application/Features/MarketingAds/
  MarketingAdsModule.cs                      (create or extend C1's)  DI wiring
  Configuration/AdsSyncOptions.cs            "Ads" section, EnsureValid()
  Configuration/AdsDailySyncOptions.cs
  Configuration/AdsChangeSyncOptions.cs
  Contracts/AdChangeOrigin.cs | AdChangeSource.cs | AdSyncStream.cs
  Contracts/IAdExecutionLookup.cs | AdExecutedAction.cs        (C3 implements)
  Contracts/AdAccountDto.cs | AdPerformanceRowDto.cs | AdSearchTermRowDto.cs
  Contracts/AdBlendedPeriodDto.cs | BlendedGranularity.cs | AdChangeEventDto.cs
  Sync/AdsDbValues.cs                        enum <-> column seam
  Sync/AdEntityKey.cs
  Sync/AdSyncDates.cs | AdChangeWindow.cs    pure date math
  Sync/AdAttributesJson.cs | AdJsonValues.cs pure JSON helpers
  Sync/AdSnapshotDiffer.cs                   pure diff (+ AdEntityState, AdFieldChange)
  Sync/AdChangeEventCandidate.cs | AdChangeValueTokens.cs
  Sync/AdChangeOriginMatcher.cs              pure matcher (+ AdOriginMatch, AdOriginMatchingRules)
  Sync/NoOpAdExecutionLookup.cs
  Sync/AdSyncStateStore.cs                   IAdSyncStateStore + impl + AdSyncStateSnapshot
  Sync/AdAccountUpserter.cs
  Sync/AdEntityUpserter.cs                   (+ AdEntityUpsertResult, AdObservedChange)
  Sync/AdDailyFactUpserter.cs | AdSearchTermUpserter.cs | AdUpsertCounts.cs
  Sync/AdChangeEventWriter.cs
  Sync/AdStreamRunner.cs                     try/catch + sync_state per stream (+ AdStreamWork, AdStreamOutcome)
  Sync/AdEntitySyncStep.cs                   (+ AdEntitySyncResult)
  Sync/AdsSyncReport.cs | IAdsDailySyncService.cs | IAdsChangeSyncService.cs
  Sync/AdsDailySyncService.cs | AdsChangeSyncService.cs | DisabledAdsSyncService.cs
  Infrastructure/Jobs/AdsDailySyncJob.cs | AdsChangeSyncJob.cs
  Reporting/IAdsReadRepository.cs | AdsReadRepository.cs | UnavailableAdsReadRepository.cs
  Reporting/AdReadQueries.cs | AdBlendedDayRow.cs | AdMetrics.cs | AdBlendedAggregator.cs | AdReadValidation.cs
  UseCases/GetAdAccounts/*  UseCases/GetAdPerformance/*  UseCases/GetAdSearchTerms/*
  UseCases/GetBlendedPerformance/*  UseCases/GetAdChangeHistory/*
backend/src/Anela.Heblo.Persistence.Ads/Sql/ads_read_views.sql
backend/test/Anela.Heblo.Tests/Features/MarketingAds/
  TestSupport/AdsTestDb.cs | ScriptedAdReadSource.cs | AdsTestData.cs | AdsSyncHarness.cs
  Sync/*Tests.cs  Jobs/*Tests.cs  Reporting/*Tests.cs  UseCases/*Tests.cs  Integration/*Tests.cs
docs/processes/sync-ads-daily.md | sync-ads-change-history.md | module-marketing-ads.md
docs/architecture/metabase.md (modify)
```

---
### Task 1: C1 reconciliation, project references, enums, options, test support

**Files:**
- Modify (if needed): `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`, `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingAds/Contracts/AdChangeOrigin.cs`, `AdChangeSource.cs`, `AdSyncStream.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/AdsDbValues.cs`, `Sync/AdEntityKey.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingAds/Configuration/AdsSyncOptions.cs`, `AdsDailySyncOptions.cs`, `AdsChangeSyncOptions.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/TestSupport/AdsTestDb.cs`, `ScriptedAdReadSource.cs`, `AdsTestData.cs`, `AdsSyncHarness.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/Sync/AdsDbValuesTests.cs`, `Sync/AdsSyncOptionsTests.cs`

**Interfaces:**
- Consumes: C1 `AdsDbContext` + entities (see reconciliation table), C1 contracts (`AdPlatform`, `AdEntityLevel`, `AdEntityStatus`, `KeywordMatchType`, `AdChangeActorKind`, `IAdPlatformReadSource`, `AdSourceCapabilities`, snapshot/row records).
- Produces: `AdChangeOrigin { Heblo = 1, OutOfBand = 2 }`, `AdChangeSource { PlatformChangeLog = 1, SnapshotDiff = 2 }`, `AdSyncStream { Entities = 1, DailyFacts = 2, SearchTerms = 3, ChangeEvents = 4 }`; `AdsDbValues.ToDb<TEnum>(TEnum) : string`, `AdsDbValues.FromDb<TEnum>(string) : TEnum`, `AdsDbValues.MatchTypeToDb(KeywordMatchType?) : string`, `AdsDbValues.MatchTypeFromDb(string) : KeywordMatchType?`, consts `NoMatchType`, `StatusSucceeded`, `StatusFailed`; `readonly record struct AdEntityKey(AdEntityLevel Level, string ExternalId)`; `AdsSyncOptions` (section `"Ads"`) with `FactLookbackDays`, `BatchSize`, `ManagedAccounts`, `OutOfBandMatchWindowMinutes`, `HebloActors`, `DailySync`, `ChangeSync`, `ManagedAccountKeys()`, `HebloActorsFor(AdPlatform)`, `EnsureValid()`; test helpers `AdsTestDb`, `ScriptedAdReadSource`, `AdsTestData`, `AdsSyncHarness`.

- [ ] **Step 1: Reconcile with C1**

```bash
cat backend/src/Anela.Heblo.Persistence.Ads/AdsDbContext.cs
ls backend/src/Anela.Heblo.Persistence.Ads/Entities/
grep -h "ProjectReference" backend/src/Anela.Heblo.Persistence.Ads/*.csproj backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
ls backend/src/Anela.Heblo.Application/Features/MarketingAds/ backend/src/Anela.Heblo.Application/Features/MarketingAds/Contracts/
cat backend/test/Anela.Heblo.MarketingAds.TestKit/FakeAdPlatformReadSource.cs
grep -rn "AdsPersistenceModule\|AddAdsPersistence" backend/src --include='*.cs'
```

Compare with the reconciliation table. Write down every difference (class, DbSet, property, column type) and apply the renames to all code in this plan as you go. Also check whether C1 already defined `AdChangeOrigin`, `AdChangeSource`, `AdSyncStream` or `MarketingAdsModule` — if so, reuse them (skip the matching files below; extend the existing module in Task 9).

`FakeAdPlatformReadSource` (C1 Task 5: `WithAccount`/`WithEntities`/`WithDailyFacts`/`WithSearchTerms`/`WithChangeEvents`, a single global `FailWith(Exception)`, a `Calls` log) cannot fail one stream of one account while the others succeed, which the isolation tests need. Keep `ScriptedAdReadSource` (Step 6) as the single seam for this plan's tests; C1's own contract suite already covers the fake. If C1's merged fake gained per-call failure injection, you may re-implement `ScriptedAdReadSource` on top of it with the same public members.

- [ ] **Step 2: Add project references if missing**

If `Anela.Heblo.Application.csproj` does not reference `Persistence.Ads`, add inside the existing `<ItemGroup>` with `ProjectReference`s:

```xml
    <ProjectReference Include="../Anela.Heblo.Persistence.Ads/Anela.Heblo.Persistence.Ads.csproj" />
```

If `Anela.Heblo.Tests.csproj` lacks them, add next to the other `ProjectReference`s:

```xml
    <ProjectReference Include="..\..\src\Anela.Heblo.Persistence.Ads\Anela.Heblo.Persistence.Ads.csproj" />
    <ProjectReference Include="..\..\src\Anela.Heblo.Persistence.ShoptetOrders\Anela.Heblo.Persistence.ShoptetOrders.csproj" />
    <ProjectReference Include="..\Anela.Heblo.MarketingAds.TestKit\Anela.Heblo.MarketingAds.TestKit.csproj" />
```

and a new `ItemGroup` (the SQL files are run by integration tests in Task 11):

```xml
  <ItemGroup>
    <None Include="..\..\src\Anela.Heblo.Persistence.Ads\Sql\ads_read_views.sql" Link="MarketingAds\Sql\ads_read_views.sql">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
    <None Include="..\..\src\Anela.Heblo.Persistence.ShoptetOrders\Sql\shoptet_raw_views.sql" Link="MarketingAds\Sql\shoptet_raw_views.sql">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
```

Create an empty placeholder SQL file now so the link resolves (Task 11 fills it):

```bash
mkdir -p backend/src/Anela.Heblo.Persistence.Ads/Sql
printf -- '-- ads read views: filled in by Task 11\n' > backend/src/Anela.Heblo.Persistence.Ads/Sql/ads_read_views.sql
```

- [ ] **Step 3: Write the failing tests**

`backend/test/Anela.Heblo.Tests/Features/MarketingAds/Sync/AdsDbValuesTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.MarketingAds.Sync;

public class AdsDbValuesTests
{
    [Theory]
    [InlineData(AdPlatform.GoogleAds, "GoogleAds")]
    [InlineData(AdPlatform.MetaAds, "MetaAds")]
    [InlineData(AdPlatform.Sklik, "Sklik")]
    public void Platform_round_trips_through_its_member_name(AdPlatform platform, string column)
    {
        // Act + Assert
        AdsDbValues.ToDb(platform).Should().Be(column);
        AdsDbValues.FromDb<AdPlatform>(column).Should().Be(platform);
    }

    [Fact]
    public void Unknown_column_value_throws_with_the_value_in_the_message()
    {
        // Act
        var act = () => AdsDbValues.FromDb<AdEntityLevel>("Banana");

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*Banana*");
    }

    [Fact]
    public void Missing_match_type_is_stored_as_Unknown_and_read_back_as_null()
    {
        AdsDbValues.MatchTypeToDb(null).Should().Be(AdsDbValues.NoMatchType);
        AdsDbValues.MatchTypeFromDb(AdsDbValues.NoMatchType).Should().BeNull();
        AdsDbValues.MatchTypeToDb(KeywordMatchType.Phrase).Should().Be("Phrase");
        AdsDbValues.MatchTypeFromDb("Exact").Should().Be(KeywordMatchType.Exact);
    }

    [Fact]
    public void New_c2_enums_have_the_agreed_values()
    {
        ((int)AdChangeOrigin.Heblo).Should().Be(1);
        ((int)AdChangeOrigin.OutOfBand).Should().Be(2);
        ((int)AdChangeSource.PlatformChangeLog).Should().Be(1);
        ((int)AdChangeSource.SnapshotDiff).Should().Be(2);
        ((int)AdSyncStream.ChangeEvents).Should().Be(4);
    }
}
```

`backend/test/Anela.Heblo.Tests/Features/MarketingAds/Sync/AdsSyncOptionsTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Configuration;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.MarketingAds.Sync;

public class AdsSyncOptionsTests
{
    [Fact]
    public void Defaults_match_the_spec()
    {
        // Act
        var options = new AdsSyncOptions();

        // Assert
        options.FactLookbackDays.Should().Be(14);
        options.OutOfBandMatchWindowMinutes.Should().Be(120);
        options.DailySync.CronExpression.Should().Be("30 5 * * *");
        options.DailySync.TimeZone.Should().Be("Europe/Prague");
        options.ChangeSync.CronExpression.Should().Be("15 * * * *");
        options.ChangeSync.InitialLookbackDays.Should().Be(7);
        options.ChangeSync.OverlapMinutes.Should().Be(60);
    }

    [Fact]
    public void Managed_account_keys_are_trimmed_and_deduplicated()
    {
        // Arrange — the configuration binder merges arrays index-wise, so duplicates are realistic.
        var options = new AdsSyncOptions { ManagedAccounts = new[] { "GoogleAds:123", " GoogleAds:123 ", "", "Sklik:9" } };

        // Act
        var keys = options.ManagedAccountKeys();

        // Assert
        keys.Should().BeEquivalentTo(new[] { "GoogleAds:123", "Sklik:9" });
    }

    [Fact]
    public void Heblo_actors_are_per_platform_and_case_insensitive()
    {
        // Arrange
        var options = new AdsSyncOptions
        {
            HebloActors = new Dictionary<string, string[]> { ["GoogleAds"] = new[] { "heblo@anela.cz", "heblo@anela.cz" } },
        };

        // Act + Assert
        options.HebloActorsFor(AdPlatform.GoogleAds).Should().Contain("HEBLO@anela.cz");
        options.HebloActorsFor(AdPlatform.Sklik).Should().BeEmpty();
    }

    [Theory]
    [InlineData(-1, 500)]
    [InlineData(31, 500)]
    [InlineData(14, 0)]
    public void EnsureValid_rejects_out_of_range_values(int lookback, int batchSize)
    {
        // Arrange
        var options = new AdsSyncOptions { FactLookbackDays = lookback, BatchSize = batchSize };

        // Act
        var act = options.EnsureValid;

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*Ads*");
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false`
Expected: FAIL — `CS0246: The type or namespace name 'AdsDbValues' could not be found` (and `AdsSyncOptions`, `AdChangeOrigin`).

- [ ] **Step 5: Implement enums, value seam, key, options**

`Contracts/AdChangeOrigin.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>Whether a change on an ad account came through an executed Heblo proposal.</summary>
public enum AdChangeOrigin
{
    Heblo = 1,
    OutOfBand = 2,
}
```

`Contracts/AdChangeSource.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>Where a change event came from: the platform's own change history, or Heblo diffing two snapshots.</summary>
public enum AdChangeSource
{
    PlatformChangeLog = 1,
    SnapshotDiff = 2,
}
```

`Contracts/AdSyncStream.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>One row of ads.sync_state exists per platform x account x stream.</summary>
public enum AdSyncStream
{
    Entities = 1,
    DailyFacts = 2,
    SearchTerms = 3,
    ChangeEvents = 4,
}
```

`Sync/AdsDbValues.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Persistence.Ads.Entities;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>
/// The single seam between the contract enums and how C1 stores them in the ads schema
/// (the enum member name as text). If C1 stores them differently, only this file changes.
/// </summary>
public static class AdsDbValues
{
    /// <summary>match_type is part of ad_search_term_daily's primary key, so "no match type" cannot be NULL.</summary>
    public const string NoMatchType = AdSearchTermDaily.UnknownMatchType;
    public const string StatusSucceeded = "Succeeded";
    public const string StatusFailed = "Failed";

    public static string ToDb<TEnum>(TEnum value) where TEnum : struct, Enum => value.ToString();

    public static TEnum FromDb<TEnum>(string value) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new InvalidOperationException($"Unknown {typeof(TEnum).Name} value '{value}' in the ads schema.");

    public static string MatchTypeToDb(KeywordMatchType? matchType) => matchType?.ToString() ?? NoMatchType;

    public static KeywordMatchType? MatchTypeFromDb(string value) =>
        value == NoMatchType ? null : FromDb<KeywordMatchType>(value);
}
```

`Sync/AdEntityKey.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>Natural key of an ad entity inside one account: platforms only guarantee id uniqueness per level.</summary>
public readonly record struct AdEntityKey(AdEntityLevel Level, string ExternalId);
```

`Configuration/AdsDailySyncOptions.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Configuration;

public sealed class AdsDailySyncOptions
{
    /// <summary>05:30 Prague — after the platforms close yesterday, before people start work.</summary>
    public string CronExpression { get; set; } = "30 5 * * *";
    public string TimeZone { get; set; } = "Europe/Prague";
    public int RunTimeoutMinutes { get; set; } = 45;
}
```

`Configuration/AdsChangeSyncOptions.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Configuration;

public sealed class AdsChangeSyncOptions
{
    /// <summary>Hourly at :15, clear of the :00 jobs; Hangfire runs one worker, so this run must stay short.</summary>
    public string CronExpression { get; set; } = "15 * * * *";
    public string TimeZone { get; set; } = "Europe/Prague";
    public int RunTimeoutMinutes { get; set; } = 15;
    /// <summary>How far back the very first change-log pull reaches (capped by the platform's ChangeLogMaxAge).</summary>
    public int InitialLookbackDays { get; set; } = 7;
    /// <summary>Each pull re-reads this much before the watermark: platforms index change history late.</summary>
    public int OverlapMinutes { get; set; } = 60;
}
```

`Configuration/AdsSyncOptions.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Application.Features.MarketingAds.Configuration;

/// <summary>
/// The "Ads" configuration section (shared with C3/C4, which bind their own classes to it).
/// Validated by <see cref="EnsureValid"/> at the start of every run — never in a job constructor
/// or with ValidateOnStart, because a throw there unschedules every recurring job or stops the app.
/// </summary>
public sealed class AdsSyncOptions
{
    public const string SectionName = "Ads";
    public const int MaxFactLookbackDays = 30;
    public const int MaxBatchSize = 5000;

    /// <summary>Days before yesterday re-pulled every day; conversions are attributed late.</summary>
    public int FactLookbackDays { get; set; } = 14;

    /// <summary>Rows per SaveChanges, keeping each statement well under the 10 s per-attempt DB timeout.</summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>"Platform:externalId" keys that are Anela's own accounts. Empty = every discovered account is managed.</summary>
    public string[] ManagedAccounts { get; set; } = Array.Empty<string>();

    public int OutOfBandMatchWindowMinutes { get; set; } = 120;

    /// <summary>Per platform name (e.g. "GoogleAds"): the platform user names/emails Heblo writes as.</summary>
    public Dictionary<string, string[]> HebloActors { get; set; } = new();

    public AdsDailySyncOptions DailySync { get; set; } = new();
    public AdsChangeSyncOptions ChangeSync { get; set; } = new();

    public IReadOnlySet<string> ManagedAccountKeys() =>
        ManagedAccounts.Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> HebloActorsFor(AdPlatform platform) =>
        HebloActors.TryGetValue(platform.ToString(), out var actors)
            ? actors.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public void EnsureValid()
    {
        var problems = new List<string>();
        if (FactLookbackDays is < 0 or > MaxFactLookbackDays)
            problems.Add($"Ads:FactLookbackDays must be 0..{MaxFactLookbackDays}, was {FactLookbackDays}");
        if (BatchSize is < 1 or > MaxBatchSize)
            problems.Add($"Ads:BatchSize must be 1..{MaxBatchSize}, was {BatchSize}");
        if (OutOfBandMatchWindowMinutes < 1)
            problems.Add($"Ads:OutOfBandMatchWindowMinutes must be positive, was {OutOfBandMatchWindowMinutes}");
        if (ChangeSync.InitialLookbackDays < 1 || ChangeSync.OverlapMinutes < 0)
            problems.Add("Ads:ChangeSync:InitialLookbackDays must be >= 1 and OverlapMinutes >= 0");
        if (problems.Count > 0)
            throw new InvalidOperationException("Invalid Ads configuration: " + string.Join("; ", problems));
    }
}
```

- [ ] **Step 6: Create test support**

`TestSupport/AdsTestDb.cs`:

```csharp
using Anela.Heblo.Persistence.Ads;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Anela.Heblo.Tests.Features.MarketingAds.TestSupport;

/// <summary>
/// Named InMemory databases. Tests assert through a SECOND context opened on the same name:
/// asserting through the context the code under test used reads the change tracker, not the store,
/// and passes even with SaveChanges removed.
/// </summary>
internal static class AdsTestDb
{
    public static string NewName() => $"ads-{Guid.NewGuid():N}";

    public static AdsDbContext Open(string name, params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<AdsDbContext>().UseInMemoryDatabase(name);
        if (interceptors.Length > 0)
            builder.AddInterceptors(interceptors);
        return new AdsDbContext(builder.Options);
    }
}
```

`TestSupport/ScriptedAdReadSource.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Tests.Features.MarketingAds.TestSupport;

/// <summary>
/// Scripted IAdPlatformReadSource used by every sync test (single seam — see Task 1 Step 1 for
/// mapping it onto C1's FakeAdPlatformReadSource). Records what it was asked for.
/// </summary>
internal sealed class ScriptedAdReadSource : IAdPlatformReadSource
{
    public ScriptedAdReadSource(AdPlatform platform, bool searchTerms = true, bool changeLog = false, TimeSpan? changeLogMaxAge = null)
    {
        Platform = platform;
        Capabilities = new AdSourceCapabilities(searchTerms, changeLog, changeLogMaxAge);
    }

    public AdPlatform Platform { get; }
    public AdSourceCapabilities Capabilities { get; }

    public List<AdAccountSnapshot> Accounts { get; } = new();
    public Dictionary<string, List<AdEntitySnapshot>> Entities { get; } = new();
    public Dictionary<string, List<AdDailyFactRow>> Facts { get; } = new();
    public Dictionary<string, List<AdSearchTermRow>> SearchTerms { get; } = new();
    public Dictionary<string, List<AdChangeEventRow>> Changes { get; } = new();

    public bool FailAccounts { get; set; }
    public HashSet<string> FailFactsFor { get; } = new();
    public HashSet<string> FailChangesFor { get; } = new();

    public List<(string Account, DateOnly Date)> FactRequests { get; } = new();
    public List<(string Account, DateOnly Date)> SearchTermRequests { get; } = new();
    public List<(string Account, DateTimeOffset Since)> ChangeRequests { get; } = new();

    public Task<IReadOnlyList<AdAccountSnapshot>> GetAccountsAsync(CancellationToken ct) =>
        FailAccounts
            ? throw new HttpRequestException($"{Platform}: accounts unavailable")
            : Task.FromResult<IReadOnlyList<AdAccountSnapshot>>(Accounts.ToList());

    public Task<IReadOnlyList<AdEntitySnapshot>> GetEntitiesAsync(string accountExternalId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AdEntitySnapshot>>(
            Entities.TryGetValue(accountExternalId, out var list) ? list.ToList() : new List<AdEntitySnapshot>());

    public Task<IReadOnlyList<AdDailyFactRow>> GetDailyFactsAsync(string accountExternalId, DateOnly date, CancellationToken ct)
    {
        FactRequests.Add((accountExternalId, date));
        if (FailFactsFor.Contains(accountExternalId))
            throw new HttpRequestException($"{Platform}: facts unavailable");
        var rows = Facts.TryGetValue(accountExternalId, out var list) ? list.Where(r => r.Date == date).ToList() : new List<AdDailyFactRow>();
        return Task.FromResult<IReadOnlyList<AdDailyFactRow>>(rows);
    }

    public Task<IReadOnlyList<AdSearchTermRow>> GetSearchTermsAsync(string accountExternalId, DateOnly date, CancellationToken ct)
    {
        SearchTermRequests.Add((accountExternalId, date));
        var rows = SearchTerms.TryGetValue(accountExternalId, out var list) ? list.Where(r => r.Date == date).ToList() : new List<AdSearchTermRow>();
        return Task.FromResult<IReadOnlyList<AdSearchTermRow>>(rows);
    }

    public Task<IReadOnlyList<AdChangeEventRow>> GetChangeEventsAsync(string accountExternalId, DateTimeOffset since, CancellationToken ct)
    {
        ChangeRequests.Add((accountExternalId, since));
        if (FailChangesFor.Contains(accountExternalId))
            throw new HttpRequestException($"{Platform}: change history unavailable");
        var rows = Changes.TryGetValue(accountExternalId, out var list) ? list.Where(r => r.OccurredAt >= since).ToList() : new List<AdChangeEventRow>();
        return Task.FromResult<IReadOnlyList<AdChangeEventRow>>(rows);
    }
}
```

`TestSupport/AdsTestData.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Tests.Features.MarketingAds.TestSupport;

internal static class AdsTestData
{
    public const string AccountId = "111-222-3333";

    /// <summary>06:00 in Prague on 2026-10-07, so "yesterday" is 2026-10-06.</summary>
    public static readonly DateTimeOffset Now = new(2026, 10, 7, 4, 0, 0, TimeSpan.Zero);
    public static readonly DateOnly Yesterday = new(2026, 10, 6);

    public static readonly IReadOnlyDictionary<string, string?> NoAttributes = new Dictionary<string, string?>();

    public static AdAccountSnapshot Account(string id = AccountId, string currency = "CZK") =>
        new(id, $"Account {id}", currency, "Europe/Prague");

    public static AdEntitySnapshot Campaign(string id, AdEntityStatus status = AdEntityStatus.Enabled,
        IReadOnlyDictionary<string, string?>? attributes = null) =>
        new(AdEntityLevel.Campaign, id, null, null, $"Campaign {id}", status, attributes ?? NoAttributes);

    public static AdEntitySnapshot AdGroup(string id, string campaignId, AdEntityStatus status = AdEntityStatus.Enabled) =>
        new(AdEntityLevel.AdGroup, id, AdEntityLevel.Campaign, campaignId, $"Ad group {id}", status, NoAttributes);

    public static AdEntitySnapshot Ad(string id, string adGroupId, AdEntityStatus status = AdEntityStatus.Enabled) =>
        new(AdEntityLevel.Ad, id, AdEntityLevel.AdGroup, adGroupId, $"Ad {id}", status, NoAttributes);

    public static AdDailyFactRow Fact(AdEntityLevel level, string id, DateOnly date, decimal cost,
        long clicks = 10, long impressions = 100, decimal conversions = 1m, decimal value = 500m, string currency = "CZK") =>
        new(level, id, date, impressions, clicks, cost, conversions, value, currency);

    public static AdSearchTermRow Term(string adGroupId, DateOnly date, string term, decimal cost,
        KeywordMatchType? matchType = KeywordMatchType.Exact) =>
        new(adGroupId, date, term, matchType, 50, 5, cost, 0m, 0m, "CZK");

    public static AdChangeEventRow Change(string eventId, DateTimeOffset at, AdEntityLevel? level, string? entityId,
        string? newValueJson, string? actor = null, AdChangeActorKind kind = AdChangeActorKind.User, string changeType = "StatusChanged") =>
        new(eventId, at, actor, kind, level, entityId, changeType, null, newValueJson);
}
```

`TestSupport/AdsSyncHarness.cs` (later tasks add members to this class — each task shows its additions):

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Configuration;
using Anela.Heblo.Persistence.Ads;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Anela.Heblo.Tests.Features.MarketingAds.TestSupport;

/// <summary>Builds the sync components by hand over one InMemory AdsDbContext.</summary>
internal sealed partial class AdsSyncHarness
{
    public AdsSyncHarness(AdsSyncOptions? options = null, params IInterceptor[] interceptors)
    {
        DbName = AdsTestDb.NewName();
        Db = AdsTestDb.Open(DbName, interceptors);
        Time = new FakeTimeProvider(AdsTestData.Now);
        Options = Microsoft.Extensions.Options.Options.Create(options ?? new AdsSyncOptions());
    }

    public string DbName { get; }
    public AdsDbContext Db { get; }
    public FakeTimeProvider Time { get; }
    public IOptions<AdsSyncOptions> Options { get; }

    /// <summary>A fresh context over the same store — assert through this one.</summary>
    public AdsDbContext Verify() => AdsTestDb.Open(DbName);
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false && dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~Features.MarketingAds.Sync.AdsDbValuesTests|FullyQualifiedName~Features.MarketingAds.Sync.AdsSyncOptionsTests"`
Expected: PASS, 10 tests.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application backend/src/Anela.Heblo.Persistence.Ads/Sql backend/test/Anela.Heblo.Tests
git commit -m "feat: add ads sync options, value seam and test support

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Pure helpers — sync dates, change window, JSON, snapshot diff

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/AdSyncDates.cs`, `AdChangeWindow.cs`, `AdAttributesJson.cs`, `AdJsonValues.cs`, `AdSnapshotDiffer.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/Sync/AdSyncDatesTests.cs`, `AdChangeWindowTests.cs`, `AdSnapshotDifferTests.cs`, `AdJsonValuesTests.cs`

**Interfaces:**
- Consumes: `AdsChangeSyncOptions` (Task 1), `AdEntityStatus` (C1).
- Produces:
  - `AdSyncDates.Yesterday(DateTimeOffset now, string? timeZoneId) : DateOnly`; `AdSyncDates.FactWindow(DateOnly yesterday, int lookbackDays) : IReadOnlyList<DateOnly>` (oldest first, `lookbackDays + 1` dates).
  - `AdChangeWindow.Since(DateTimeOffset? watermark, DateTimeOffset now, AdsChangeSyncOptions options, TimeSpan? changeLogMaxAge) : DateTimeOffset`; `AdChangeWindow.MaxAgeSafetyMargin`.
  - `AdAttributesJson.Serialize(IReadOnlyDictionary<string,string?>) : string`, `Parse(string?) : IReadOnlyDictionary<string,string?>`, `Subset(IReadOnlyDictionary<string,string?>, IEnumerable<string>) : string`.
  - `AdJsonValues.Normalize(string?) : string?` (valid JSON passes through, anything else becomes a JSON string).
  - `sealed record AdEntityState(string Name, AdEntityStatus Status, IReadOnlyDictionary<string,string?> Attributes)`; `sealed record AdFieldChange(string ChangeType, string? OldValueJson, string? NewValueJson)`; `AdSnapshotDiffer.Diff(AdEntityState before, AdEntityState after) : IReadOnlyList<AdFieldChange>`; `AdSnapshotDiffer.Created(AdEntityState) : AdFieldChange`; consts `NameChange = "Name"`, `StatusChange = "Status"`, `AttributesChange = "Attributes"`, `CreatedChange = "Created"`.

- [ ] **Step 1: Write the failing tests**

`Sync/AdSyncDatesTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.MarketingAds.Sync;

public class AdSyncDatesTests
{
    [Fact]
    public void Yesterday_is_computed_in_the_account_time_zone()
    {
        // Arrange — 22:30 UTC on 7 Oct is 00:30 on 8 Oct in Prague but 18:30 on 7 Oct in New York.
        var now = new DateTimeOffset(2026, 10, 7, 22, 30, 0, TimeSpan.Zero);

        // Act + Assert
        AdSyncDates.Yesterday(now, "Europe/Prague").Should().Be(new DateOnly(2026, 10, 7));
        AdSyncDates.Yesterday(now, "America/New_York").Should().Be(new DateOnly(2026, 10, 6));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Mars/Olympus_Mons")]
    public void Unknown_time_zone_falls_back_to_Prague(string? zone)
    {
        var now = new DateTimeOffset(2026, 10, 7, 22, 30, 0, TimeSpan.Zero);
        AdSyncDates.Yesterday(now, zone).Should().Be(new DateOnly(2026, 10, 7));
    }

    [Fact]
    public void Fact_window_is_yesterday_plus_lookback_oldest_first()
    {
        // Act
        var window = AdSyncDates.FactWindow(new DateOnly(2026, 10, 6), 14);

        // Assert
        window.Should().HaveCount(15);
        window[0].Should().Be(new DateOnly(2026, 9, 22));
        window[^1].Should().Be(new DateOnly(2026, 10, 6));
    }

    [Fact]
    public void Zero_lookback_is_just_yesterday()
    {
        AdSyncDates.FactWindow(new DateOnly(2026, 10, 6), 0).Should().Equal(new DateOnly(2026, 10, 6));
    }
}
```

`Sync/AdChangeWindowTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Configuration;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.MarketingAds.Sync;

public class AdChangeWindowTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 10, 15, 0, TimeSpan.Zero);
    private static readonly AdsChangeSyncOptions Options = new();

    [Fact]
    public void First_pull_reaches_back_the_initial_lookback()
    {
        AdChangeWindow.Since(null, Now, Options, null).Should().Be(Now.AddDays(-7));
    }

    [Fact]
    public void Later_pulls_start_one_overlap_before_the_watermark()
    {
        var watermark = Now.AddHours(-1);
        AdChangeWindow.Since(watermark, Now, Options, null).Should().Be(watermark.AddMinutes(-60));
    }

    [Fact]
    public void Never_asks_further_back_than_the_platform_allows()
    {
        // Arrange — Google change_event only reaches 30 days back; a 40-day-old watermark must be clamped.
        var watermark = Now.AddDays(-40);

        // Act
        var since = AdChangeWindow.Since(watermark, Now, Options, TimeSpan.FromDays(30));

        // Assert
        since.Should().Be(Now.AddDays(-30) + AdChangeWindow.MaxAgeSafetyMargin);
    }

    [Fact]
    public void Max_age_shorter_than_initial_lookback_clamps_the_first_pull()
    {
        AdChangeWindow.Since(null, Now, Options, TimeSpan.FromDays(3))
            .Should().Be(Now.AddDays(-3) + AdChangeWindow.MaxAgeSafetyMargin);
    }
}
```

`Sync/AdJsonValuesTests.cs`:

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.MarketingAds.Sync;

public class AdJsonValuesTests
{
    [Theory]
    [InlineData("{\"status\":\"PAUSED\"}")]
    [InlineData("\"Paused\"")]
    [InlineData("42")]
    public void Valid_json_passes_through(string json) => AdJsonValues.Normalize(json).Should().Be(json);

    [Fact]
    public void Bare_text_becomes_a_json_string()
    {
        // Act
        var normalized = AdJsonValues.Normalize("PAUSED");

        // Assert — parse, never substring-match JSON
        JsonDocument.Parse(normalized!).RootElement.GetString().Should().Be("PAUSED");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Blank_is_null(string? value) => AdJsonValues.Normalize(value).Should().BeNull();
}
```

`Sync/AdSnapshotDifferTests.cs`:

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.MarketingAds.Sync;

public class AdSnapshotDifferTests
{
    private static AdEntityState State(string name = "Brand", AdEntityStatus status = AdEntityStatus.Enabled,
        string attributesJson = "{\"budget\":\"500\",\"bidding\":\"MaxConversions\"}") =>
        new(name, status, AdAttributesJson.Parse(attributesJson));

    [Fact]
    public void Identical_states_produce_no_change()
    {
        AdSnapshotDiffer.Diff(State(), State()).Should().BeEmpty();
    }

    [Fact]
    public void Attribute_order_and_whitespace_do_not_count_as_a_change()
    {
        // Arrange — jsonb hands back keys reordered and re-spaced compared with what was written.
        var stored = State(attributesJson: "{\"bidding\": \"MaxConversions\",   \"budget\": \"500\"}");
        var fresh = new AdEntityState("Brand", AdEntityStatus.Enabled,
            new Dictionary<string, string?> { ["budget"] = "500", ["bidding"] = "MaxConversions" });

        // Act + Assert
        AdSnapshotDiffer.Diff(stored, fresh).Should().BeEmpty();
    }

    [Fact]
    public void Status_change_carries_old_and_new_status_as_json_strings()
    {
        // Act
        var changes = AdSnapshotDiffer.Diff(State(), State(status: AdEntityStatus.Paused));

        // Assert
        var change = changes.Should().ContainSingle().Subject;
        change.ChangeType.Should().Be(AdSnapshotDiffer.StatusChange);
        JsonDocument.Parse(change.OldValueJson!).RootElement.GetString().Should().Be("Enabled");
        JsonDocument.Parse(change.NewValueJson!).RootElement.GetString().Should().Be("Paused");
    }

    [Fact]
    public void Name_and_attribute_changes_are_separate_events_with_only_changed_keys()
    {
        // Act
        var changes = AdSnapshotDiffer.Diff(State(), State(name: "Brand CZ", attributesJson: "{\"budget\":\"800\",\"bidding\":\"MaxConversions\"}"));

        // Assert
        changes.Select(c => c.ChangeType).Should().BeEquivalentTo(new[] { AdSnapshotDiffer.NameChange, AdSnapshotDiffer.AttributesChange });
        var attributes = changes.Single(c => c.ChangeType == AdSnapshotDiffer.AttributesChange);
        var after = JsonDocument.Parse(attributes.NewValueJson!).RootElement;
        after.EnumerateObject().Select(p => p.Name).Should().Equal("budget");
        after.GetProperty("budget").GetString().Should().Be("800");
    }

    [Fact]
    public void Removed_attribute_is_reported_with_a_null_new_value()
    {
        var changes = AdSnapshotDiffer.Diff(State(), State(attributesJson: "{\"budget\":\"500\"}"));

        var after = JsonDocument.Parse(changes.Single().NewValueJson!).RootElement;
        after.GetProperty("bidding").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void Created_describes_the_new_entity()
    {
        var created = AdSnapshotDiffer.Created(State());

        created.ChangeType.Should().Be(AdSnapshotDiffer.CreatedChange);
        created.OldValueJson.Should().BeNull();
        JsonDocument.Parse(created.NewValueJson!).RootElement.GetProperty("status").GetString().Should().Be("Enabled");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false`
Expected: FAIL — `CS0103: The name 'AdSyncDates' does not exist` (and `AdChangeWindow`, `AdJsonValues`, `AdSnapshotDiffer`, `AdAttributesJson`).

- [ ] **Step 3: Implement**

`Sync/AdSyncDates.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>Calendar dates are the ad account's local dates — that is how every platform reports a day.</summary>
public static class AdSyncDates
{
    public const string FallbackTimeZoneId = "Europe/Prague";

    public static DateOnly Yesterday(DateTimeOffset now, string? timeZoneId)
    {
        var local = TimeZoneInfo.ConvertTime(now, Resolve(timeZoneId));
        return DateOnly.FromDateTime(local.Date).AddDays(-1);
    }

    public static IReadOnlyList<DateOnly> FactWindow(DateOnly yesterday, int lookbackDays) =>
        Enumerable.Range(0, lookbackDays + 1).Select(i => yesterday.AddDays(i - lookbackDays)).ToList();

    private static TimeZoneInfo Resolve(string? timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone))
            return zone;
        return TimeZoneInfo.FindSystemTimeZoneById(FallbackTimeZoneId);
    }
}
```

`Sync/AdChangeWindow.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Configuration;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

public static class AdChangeWindow
{
    /// <summary>Stay this far inside a platform's maximum history age so a request is never rejected at the boundary.</summary>
    public static readonly TimeSpan MaxAgeSafetyMargin = TimeSpan.FromHours(1);

    public static DateTimeOffset Since(DateTimeOffset? watermark, DateTimeOffset now, AdsChangeSyncOptions options, TimeSpan? changeLogMaxAge)
    {
        var desired = watermark is { } w
            ? w - TimeSpan.FromMinutes(options.OverlapMinutes)
            : now - TimeSpan.FromDays(options.InitialLookbackDays);

        if (changeLogMaxAge is { } maxAge)
        {
            var floor = now - maxAge + MaxAgeSafetyMargin;
            if (desired < floor)
                desired = floor;
        }

        return desired;
    }
}
```

`Sync/AdAttributesJson.cs`:

```csharp
using System.Text.Json;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>
/// ad_entities.attributes is jsonb, which normalises key order and whitespace. Attributes are
/// therefore always compared as parsed dictionaries, never as text.
/// </summary>
public static class AdAttributesJson
{
    private static readonly IReadOnlyDictionary<string, string?> Empty = new Dictionary<string, string?>();

    public static string Serialize(IReadOnlyDictionary<string, string?> attributes) =>
        JsonSerializer.Serialize(new SortedDictionary<string, string?>(
            attributes.ToDictionary(p => p.Key, p => p.Value), StringComparer.Ordinal));

    public static IReadOnlyDictionary<string, string?> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Empty;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return Empty;

            var result = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                result[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Null => null,
                    _ => property.Value.GetRawText(),
                };
            }
            return result;
        }
        catch (JsonException)
        {
            // A corrupt stored value is treated as "no attributes"; the next diff rewrites it once.
            return Empty;
        }
    }

    public static string Subset(IReadOnlyDictionary<string, string?> attributes, IEnumerable<string> keys) =>
        JsonSerializer.Serialize(new SortedDictionary<string, string?>(
            keys.ToDictionary(k => k, k => attributes.TryGetValue(k, out var v) ? v : null), StringComparer.Ordinal));
}
```

`Sync/AdJsonValues.cs`:

```csharp
using System.Text.Json;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>old_value / new_value are jsonb: a platform value that is not valid JSON would fail the insert.</summary>
public static class AdJsonValues
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        try
        {
            using var _ = JsonDocument.Parse(value);
            return value;
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(value);
        }
    }
}
```

`Sync/AdSnapshotDiffer.cs`:

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

public sealed record AdEntityState(string Name, AdEntityStatus Status, IReadOnlyDictionary<string, string?> Attributes);

public sealed record AdFieldChange(string ChangeType, string? OldValueJson, string? NewValueJson);

/// <summary>Snapshot-diff fallback for sources without a change log (spec section 5): name, status, attributes.</summary>
public static class AdSnapshotDiffer
{
    public const string NameChange = "Name";
    public const string StatusChange = "Status";
    public const string AttributesChange = "Attributes";
    public const string CreatedChange = "Created";

    public static IReadOnlyList<AdFieldChange> Diff(AdEntityState before, AdEntityState after)
    {
        var changes = new List<AdFieldChange>();

        if (!string.Equals(before.Name, after.Name, StringComparison.Ordinal))
            changes.Add(new AdFieldChange(NameChange, JsonSerializer.Serialize(before.Name), JsonSerializer.Serialize(after.Name)));

        if (before.Status != after.Status)
            changes.Add(new AdFieldChange(StatusChange,
                JsonSerializer.Serialize(before.Status.ToString()), JsonSerializer.Serialize(after.Status.ToString())));

        var changedKeys = before.Attributes.Keys.Union(after.Attributes.Keys)
            .Where(key => !string.Equals(ValueOf(before.Attributes, key), ValueOf(after.Attributes, key), StringComparison.Ordinal))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();
        if (changedKeys.Count > 0)
            changes.Add(new AdFieldChange(AttributesChange,
                AdAttributesJson.Subset(before.Attributes, changedKeys), AdAttributesJson.Subset(after.Attributes, changedKeys)));

        return changes;
    }

    public static AdFieldChange Created(AdEntityState state) =>
        new(CreatedChange, null, JsonSerializer.Serialize(new { name = state.Name, status = state.Status.ToString() }));

    private static string? ValueOf(IReadOnlyDictionary<string, string?> attributes, string key) =>
        attributes.TryGetValue(key, out var value) ? value : null;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false && dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~Features.MarketingAds.Sync.AdSyncDatesTests|FullyQualifiedName~Features.MarketingAds.Sync.AdChangeWindowTests|FullyQualifiedName~Features.MarketingAds.Sync.AdJsonValuesTests|FullyQualifiedName~Features.MarketingAds.Sync.AdSnapshotDifferTests"`
Expected: PASS, 21 tests.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync backend/test/Anela.Heblo.Tests/Features/MarketingAds/Sync
git commit -m "feat: add ads sync date window and snapshot diff helpers

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Out-of-band matcher and the `IAdExecutionLookup` contract

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingAds/Contracts/IAdExecutionLookup.cs`, `Contracts/AdExecutedAction.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/AdChangeEventCandidate.cs`, `AdChangeValueTokens.cs`, `AdChangeOriginMatcher.cs`, `NoOpAdExecutionLookup.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/Sync/AdChangeOriginMatcherTests.cs`

**Interfaces:**
- Consumes: `AdActionType`, `AdActionValues.Paused`, `AdActionPayloadKeys.Text`, `AdChangeEventRow`, `AdChangeActorKind` (C1); `AdChangeOrigin` (Task 1); `AdJsonValues` (Task 2).
- Produces (C3 implements `IAdExecutionLookup` and replaces `NoOpAdExecutionLookup` in DI):

```csharp
public interface IAdExecutionLookup
{
    Task<IReadOnlyList<AdExecutedAction>> GetExecutedActionsAsync(
        AdPlatform platform, string accountExternalId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
public sealed record AdExecutedAction(string ExecutionId, AdActionType Type, AdPlatform Platform, string AccountExternalId,
    AdEntityLevel TargetLevel, string TargetExternalId, string? PlatformResourceId, DateTimeOffset ExecutedAt,
    IReadOnlyDictionary<string, string> Payload, bool IsRevert = false);
```

  - `sealed record AdChangeEventCandidate(string ExternalEventId, DateTimeOffset OccurredAt, string? Actor, AdChangeActorKind ActorKind, AdEntityLevel? EntityLevel, string? EntityExternalId, string ChangeType, string? OldValueJson, string? NewValueJson)` with `static FromRow(AdChangeEventRow)` (UTC + normalised JSON).
  - `AdChangeValueTokens.Extract(string? json) : IReadOnlyList<string>`.
  - `sealed record AdOriginMatchingRules(TimeSpan Window, IReadOnlySet<string> HebloActors)`; `sealed record AdOriginMatch(AdChangeOrigin Origin, string? MatchedExecutionId)` with `static OutOfBand`; `sealed class AdChangeOriginMatcher { AdOriginMatch Match(AdChangeEventCandidate, IReadOnlyList<AdExecutedAction>, AdOriginMatchingRules) }`.

- [ ] **Step 1: Write the failing tests**

`Sync/AdChangeOriginMatcherTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.MarketingAds.Sync;

public class AdChangeOriginMatcherTests
{
    private static readonly DateTimeOffset ExecutedAt = new(2026, 10, 6, 9, 0, 0, TimeSpan.Zero);
    private static readonly AdChangeOriginMatcher Matcher = new();
    private static readonly AdOriginMatchingRules Rules =
        new(TimeSpan.FromHours(2), new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private static AdExecutedAction PauseAd(string id = "7", string adId = "ad-1", DateTimeOffset? at = null) =>
        new(id, AdActionType.PauseAd, AdPlatform.GoogleAds, "acc", AdEntityLevel.Ad, adId, null,
            at ?? ExecutedAt, new Dictionary<string, string>());

    private static AdExecutedAction AddNegative(string? resourceId, string text = "zdarma") =>
        new("9", AdActionType.AddNegativeKeyword, AdPlatform.GoogleAds, "acc", AdEntityLevel.Campaign, "camp-1", resourceId,
            ExecutedAt, new Dictionary<string, string> { [AdActionPayloadKeys.Text] = text, [AdActionPayloadKeys.MatchType] = "Exact" });

    private static AdChangeEventCandidate Change(string? entityId, string? newValueJson, DateTimeOffset? at = null,
        string? actor = null, AdEntityLevel? level = AdEntityLevel.Ad) =>
        new("evt", at ?? ExecutedAt.AddMinutes(30), actor, AdChangeActorKind.Unknown, level, entityId, "Status", null, newValueJson);

    [Fact]
    public void Without_executions_everything_is_out_of_band()
    {
        Matcher.Match(Change("ad-1", "\"Paused\""), Array.Empty<AdExecutedAction>(), Rules)
            .Should().Be(AdOriginMatch.OutOfBand);
    }

    [Fact]
    public void Snapshot_status_change_to_paused_matches_a_pause_executed_half_an_hour_earlier()
    {
        var match = Matcher.Match(Change("ad-1", "\"Paused\""), new[] { PauseAd() }, Rules);

        match.Should().Be(new AdOriginMatch(AdChangeOrigin.Heblo, "7"));
    }

    [Fact]
    public void Platform_object_values_match_case_insensitively()
    {
        var match = Matcher.Match(Change("ad-1", "{\"resource\":{\"status\":\"PAUSED\"}}"), new[] { PauseAd() }, Rules);

        match.Origin.Should().Be(AdChangeOrigin.Heblo);
    }

    [Fact]
    public void Change_outside_the_two_hour_window_is_out_of_band()
    {
        var match = Matcher.Match(Change("ad-1", "\"Paused\"", at: ExecutedAt.AddHours(3)), new[] { PauseAd() }, Rules);

        match.Should().Be(AdOriginMatch.OutOfBand);
    }

    [Fact]
    public void Change_on_another_entity_is_out_of_band()
    {
        Matcher.Match(Change("ad-2", "\"Paused\""), new[] { PauseAd() }, Rules).Should().Be(AdOriginMatch.OutOfBand);
    }

    [Fact]
    public void Re_enabling_the_ad_matches_a_heblo_revert_of_the_pause()
    {
        var revert = PauseAd(id: "8") with { IsRevert = true };

        var match = Matcher.Match(Change("ad-1", "\"Enabled\""), new[] { revert }, Rules);

        match.Should().Be(new AdOriginMatch(AdChangeOrigin.Heblo, "8"));
    }

    [Fact]
    public void Re_enabling_the_ad_is_not_the_same_change()
    {
        Matcher.Match(Change("ad-1", "\"Enabled\""), new[] { PauseAd() }, Rules).Should().Be(AdOriginMatch.OutOfBand);
    }

    [Fact]
    public void Negative_keyword_matches_by_created_criterion_id()
    {
        var match = Matcher.Match(Change("crit-55", "{\"text\":\"anything\"}", level: AdEntityLevel.NegativeKeyword),
            new[] { AddNegative("crit-55") }, Rules);

        match.Should().Be(new AdOriginMatch(AdChangeOrigin.Heblo, "9"));
    }

    [Fact]
    public void Negative_keyword_matches_by_text_on_the_target_campaign()
    {
        var match = Matcher.Match(Change("camp-1", "{\"keyword\":{\"text\":\"Zdarma\"}}", level: AdEntityLevel.Campaign),
            new[] { AddNegative(null) }, Rules);

        match.Origin.Should().Be(AdChangeOrigin.Heblo);
    }

    [Fact]
    public void Reported_actor_that_is_not_heblo_is_out_of_band_even_if_everything_else_matches()
    {
        var rules = Rules with { HebloActors = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "heblo@anela.cz" } };

        Matcher.Match(Change("ad-1", "\"Paused\"", actor: "specialist@agency.cz"), new[] { PauseAd() }, rules)
            .Should().Be(AdOriginMatch.OutOfBand);
        Matcher.Match(Change("ad-1", "\"Paused\"", actor: "HEBLO@anela.cz"), new[] { PauseAd() }, rules)
            .Origin.Should().Be(AdChangeOrigin.Heblo);
    }

    [Fact]
    public void Missing_actor_does_not_block_a_match()
    {
        var rules = Rules with { HebloActors = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "heblo@anela.cz" } };

        Matcher.Match(Change("ad-1", "\"Paused\"", actor: null), new[] { PauseAd() }, rules)
            .Origin.Should().Be(AdChangeOrigin.Heblo);
    }

    [Fact]
    public void Nearest_execution_in_time_wins()
    {
        var far = PauseAd(id: "1", at: ExecutedAt.AddMinutes(-60));
        var near = PauseAd(id: "2", at: ExecutedAt.AddMinutes(25));

        Matcher.Match(Change("ad-1", "\"Paused\""), new[] { far, near }, Rules).MatchedExecutionId.Should().Be("2");
    }

    [Fact]
    public void Candidate_from_row_is_utc_and_json_normalised()
    {
        // Arrange
        var row = new AdChangeEventRow("e1", new DateTimeOffset(2026, 10, 6, 11, 0, 0, TimeSpan.FromHours(2)), "x",
            AdChangeActorKind.User, AdEntityLevel.Ad, "ad-1", "StatusChanged", "ENABLED", "PAUSED");

        // Act
        var candidate = AdChangeEventCandidate.FromRow(row);

        // Assert
        candidate.OccurredAt.Offset.Should().Be(TimeSpan.Zero);
        candidate.OccurredAt.Should().Be(row.OccurredAt);
        candidate.NewValueJson.Should().Be("\"PAUSED\"");
        Matcher.Match(candidate, new[] { PauseAd(at: candidate.OccurredAt) }, Rules).Origin.Should().Be(AdChangeOrigin.Heblo);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false`
Expected: FAIL — `CS0246: The type or namespace name 'AdChangeOriginMatcher' could not be found`.

- [ ] **Step 3: Implement**

`Contracts/AdExecutedAction.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>
/// One action Heblo executed successfully on an ad platform (produced by C3's execution log).
/// PlatformResourceId is what the platform created (e.g. a negative keyword criterion id).
/// </summary>
public sealed record AdExecutedAction(
    string ExecutionId,
    AdActionType Type,
    AdPlatform Platform,
    string AccountExternalId,
    AdEntityLevel TargetLevel,
    string TargetExternalId,
    string? PlatformResourceId,
    DateTimeOffset ExecutedAt,
    IReadOnlyDictionary<string, string> Payload,
    bool IsRevert = false);   // true = Heblo reverted this action at ExecutedAt (C3 emits one row per revert)
```

`Contracts/IAdExecutionLookup.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>
/// Read port the change sync uses to tell Heblo's own writes from out-of-band ones.
/// C2 registers a no-op (everything is out-of-band); C3 registers the real implementation over
/// its execution log. Returns successfully executed actions whose execution time is in [from, to].
/// </summary>
public interface IAdExecutionLookup
{
    Task<IReadOnlyList<AdExecutedAction>> GetExecutedActionsAsync(
        AdPlatform platform,
        string accountExternalId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct);
}
```

`Sync/NoOpAdExecutionLookup.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>Until C3 ships the proposal layer nothing is executed by Heblo, so every change is out-of-band.</summary>
public sealed class NoOpAdExecutionLookup : IAdExecutionLookup
{
    public Task<IReadOnlyList<AdExecutedAction>> GetExecutedActionsAsync(
        AdPlatform platform, string accountExternalId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AdExecutedAction>>(Array.Empty<AdExecutedAction>());
}
```

`Sync/AdChangeEventCandidate.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>A change event ready to be matched and stored: UTC time, JSON-safe values.</summary>
public sealed record AdChangeEventCandidate(
    string ExternalEventId,
    DateTimeOffset OccurredAt,
    string? Actor,
    AdChangeActorKind ActorKind,
    AdEntityLevel? EntityLevel,
    string? EntityExternalId,
    string ChangeType,
    string? OldValueJson,
    string? NewValueJson)
{
    public static AdChangeEventCandidate FromRow(AdChangeEventRow row) => new(
        row.ExternalEventId,
        row.OccurredAt.ToUniversalTime(),
        row.Actor,
        row.ActorKind,
        row.EntityLevel,
        row.EntityExternalId,
        row.ChangeType,
        AdJsonValues.Normalize(row.OldValueJson),
        AdJsonValues.Normalize(row.NewValueJson));
}
```

`Sync/AdChangeValueTokens.cs`:

```csharp
using System.Text.Json;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>
/// Every string value inside a change value, however a platform nests it ("\"PAUSED\"",
/// {"status":"PAUSED"}, {"keyword":{"text":"zdarma"}}). Lets the matcher compare platform
/// change logs and snapshot diffs without per-platform parsing.
/// </summary>
public static class AdChangeValueTokens
{
    private const int MaxDepth = 4;

    public static IReadOnlyList<string> Extract(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<string>();
        try
        {
            using var document = JsonDocument.Parse(json);
            var tokens = new List<string>();
            Collect(document.RootElement, 0, tokens);
            return tokens;
        }
        catch (JsonException)
        {
            return new[] { json.Trim() };
        }
    }

    private static void Collect(JsonElement element, int depth, List<string> tokens)
    {
        if (depth > MaxDepth)
            return;
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                tokens.Add(element.GetString()!);
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    Collect(property.Value, depth + 1, tokens);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    Collect(item, depth + 1, tokens);
                break;
        }
    }
}
```

`Sync/AdChangeOriginMatcher.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

public sealed record AdOriginMatchingRules(TimeSpan Window, IReadOnlySet<string> HebloActors);

public sealed record AdOriginMatch(AdChangeOrigin Origin, string? MatchedExecutionId)
{
    public static readonly AdOriginMatch OutOfBand = new(AdChangeOrigin.OutOfBand, null);
}

/// <summary>
/// Spec section 5: a change is Heblo's when it matches an executed action — same entity, same
/// change, within the window, and (when the platform reports an actor) made by Heblo's platform user.
/// Pure: no I/O, so every rule is unit-tested.
/// </summary>
public sealed class AdChangeOriginMatcher
{
    public AdOriginMatch Match(AdChangeEventCandidate change, IReadOnlyList<AdExecutedAction> executed, AdOriginMatchingRules rules)
    {
        if (change.EntityExternalId is null || executed.Count == 0)
            return AdOriginMatch.OutOfBand;
        if (IsForeignActor(change.Actor, rules.HebloActors))
            return AdOriginMatch.OutOfBand;

        var tokens = AdChangeValueTokens.Extract(change.NewValueJson);
        var best = executed
            .Where(action => (action.ExecutedAt - change.OccurredAt).Duration() <= rules.Window)
            .Where(action => TargetsEntity(action, change.EntityExternalId))
            .Where(action => IsSameChange(action, change.EntityExternalId, tokens))
            .OrderBy(action => (action.ExecutedAt - change.OccurredAt).Duration())
            .ThenBy(action => action.ExecutionId, StringComparer.Ordinal)
            .FirstOrDefault();

        return best is null ? AdOriginMatch.OutOfBand : new AdOriginMatch(AdChangeOrigin.Heblo, best.ExecutionId);
    }

    private static bool IsForeignActor(string? actor, IReadOnlySet<string> hebloActors) =>
        !string.IsNullOrWhiteSpace(actor) && hebloActors.Count > 0 && !hebloActors.Contains(actor.Trim());

    private static bool TargetsEntity(AdExecutedAction action, string entityExternalId) =>
        string.Equals(action.TargetExternalId, entityExternalId, StringComparison.Ordinal)
        || string.Equals(action.PlatformResourceId, entityExternalId, StringComparison.Ordinal);

    private static bool IsSameChange(AdExecutedAction action, string entityExternalId, IReadOnlyList<string> tokens) =>
        action.Type switch
        {
            AdActionType.PauseAd => tokens.Contains(
                action.IsRevert ? AdActionValues.Enabled : AdActionValues.Paused, StringComparer.OrdinalIgnoreCase),
            AdActionType.AddNegativeKeyword =>
                string.Equals(action.PlatformResourceId, entityExternalId, StringComparison.Ordinal)
                || (action.Payload.TryGetValue(AdActionPayloadKeys.Text, out var text)
                    && tokens.Contains(text, StringComparer.OrdinalIgnoreCase)),
            _ => false,
        };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false && dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~Features.MarketingAds.Sync.AdChangeOriginMatcherTests"`
Expected: PASS, 13 tests.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingAds backend/test/Anela.Heblo.Tests/Features/MarketingAds/Sync
git commit -m "feat: add out-of-band change matcher and execution lookup port

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Sync state store, account upserter, entity upserter

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/AdSyncStateStore.cs`, `AdAccountUpserter.cs`, `AdEntityUpserter.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/TestSupport/AdsSyncHarness.cs` (new partial file `AdsSyncHarness.Upserters.cs`)
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/Sync/AdSyncStateStoreTests.cs`, `AdAccountUpserterTests.cs`, `AdEntityUpserterTests.cs`

**Interfaces:**
- Consumes: Task 1 (`AdsDbValues`, `AdEntityKey`, `AdsSyncOptions`, `AdSyncStream`), Task 2 (`AdAttributesJson`, `AdSnapshotDiffer`, `AdEntityState`, `AdFieldChange`), C1 entities.
- Produces:
  - `interface IAdSyncStateStore { Task<AdSyncStateSnapshot?> GetAsync(AdPlatform, string accountExternalId, AdSyncStream, CancellationToken); Task RecordSuccessAsync(AdPlatform, string accountExternalId, AdSyncStream, DateTimeOffset at, DateTimeOffset? watermark, CancellationToken); Task RecordFailureAsync(AdPlatform, string accountExternalId, AdSyncStream, string error, CancellationToken); }`; `sealed record AdSyncStateSnapshot(DateTimeOffset? Watermark, string Status, DateTimeOffset? LastSuccessAt, string? LastError)`; `AdSyncStateStore(AdsDbContext, TimeProvider)`, `const int MaxErrorLength = 2000`.
  - `AdAccountUpserter(AdsDbContext, TimeProvider, IOptions<AdsSyncOptions>, ILogger<AdAccountUpserter>)`: `Task<IReadOnlyList<AdAccount>> UpsertAsync(AdPlatform, IReadOnlyList<AdAccountSnapshot>, CancellationToken)`; `Task<AdManagedAccounts> SyncManagedAsync(IAdPlatformReadSource, CancellationToken)`; `sealed record AdManagedAccounts(IReadOnlyList<AdAccount> Accounts, string? Error)`.
  - `AdEntityUpserter(AdsDbContext, TimeProvider, IOptions<AdsSyncOptions>)`: `Task<IReadOnlyDictionary<AdEntityKey,long>> LoadIdsAsync(long accountId, CancellationToken)`; `Task<AdEntityUpsertResult> UpsertAsync(long accountId, IReadOnlyList<AdEntitySnapshot>, bool reportCreations, CancellationToken)`; `sealed record AdEntityUpsertResult(IReadOnlyDictionary<AdEntityKey,long> EntityIds, IReadOnlyList<AdObservedChange> Changes, int RowsWritten)`; `sealed record AdObservedChange(AdEntityKey Key, long EntityId, AdFieldChange Change)`.
  - Every upserter calls `ChangeTracker.Clear()` after saving, so entities it returns are detached.

- [ ] **Step 1: Write the failing tests**

`TestSupport/AdsSyncHarness.Upserters.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anela.Heblo.Tests.Features.MarketingAds.TestSupport;

internal sealed partial class AdsSyncHarness
{
    public AdSyncStateStore State => new(Db, Time);
    public AdAccountUpserter Accounts => new(Db, Time, Options, NullLogger<AdAccountUpserter>.Instance);
    public AdEntityUpserter Entities => new(Db, Time, Options);
}
```

`Sync/AdSyncStateStoreTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Tests.Features.MarketingAds.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Tests.Features.MarketingAds.Sync;

public class AdSyncStateStoreTests
{
    [Fact]
    public async Task Failure_after_success_keeps_watermark_and_last_success()
    {
        // Arrange
        var harness = new AdsSyncHarness();
        var watermark = AdsTestData.Now.AddHours(-1);
        await harness.State.RecordSuccessAsync(AdPlatform.Sklik, "acc", AdSyncStream.ChangeEvents, AdsTestData.Now, watermark, default);

        // Act
        await harness.State.RecordFailureAsync(AdPlatform.Sklik, "acc", AdSyncStream.ChangeEvents, "boom", default);

        // Assert — through a second context
        await using var verify = harness.Verify();
        var row = await verify.SyncStates.SingleAsync();
        row.Status.Should().Be(AdsDbValues.StatusFailed);
        row.LastError.Should().Be("boom");
        row.Watermark.Should().Be(watermark);
        row.LastSuccessAt.Should().Be(AdsTestData.Now);
    }

    [Fact]
    public async Task Success_without_watermark_keeps_the_previous_one_and_clears_the_error()
    {
        var harness = new AdsSyncHarness();
        var watermark = AdsTestData.Now.AddDays(-1);
        await harness.State.RecordSuccessAsync(AdPlatform.Sklik, "acc", AdSyncStream.DailyFacts, AdsTestData.Now, watermark, default);
        await harness.State.RecordFailureAsync(AdPlatform.Sklik, "acc", AdSyncStream.DailyFacts, "boom", default);

        await harness.State.RecordSuccessAsync(AdPlatform.Sklik, "acc", AdSyncStream.DailyFacts, AdsTestData.Now, null, default);

        var state = await harness.State.GetAsync(AdPlatform.Sklik, "acc", AdSyncStream.DailyFacts, default);
        state!.Watermark.Should().Be(watermark);
        state.LastError.Should().BeNull();
        state.Status.Should().Be(AdsDbValues.StatusSucceeded);
    }

    [Fact]
    public async Task Long_errors_are_truncated()
    {
        var harness = new AdsSyncHarness();

        await harness.State.RecordFailureAsync(AdPlatform.MetaAds, "acc", AdSyncStream.Entities, new string('x', 5000), default);

        await using var verify = harness.Verify();
        (await verify.SyncStates.SingleAsync()).LastError!.Length.Should().Be(AdSyncStateStore.MaxErrorLength);
    }

    [Fact]
    public async Task Missing_state_is_null()
    {
        (await new AdsSyncHarness().State.GetAsync(AdPlatform.MetaAds, "acc", AdSyncStream.Entities, default)).Should().BeNull();
    }
}
```

`Sync/AdAccountUpserterTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Configuration;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Tests.Features.MarketingAds.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Tests.Features.MarketingAds.Sync;

public class AdAccountUpserterTests
{
    [Fact]
    public async Task New_accounts_are_managed_when_no_list_is_configured()
    {
        var harness = new AdsSyncHarness();

        var rows = await harness.Accounts.UpsertAsync(AdPlatform.GoogleAds, new[] { AdsTestData.Account("1"), AdsTestData.Account("1") }, default);

        rows.Should().ContainSingle().Which.IsManaged.Should().BeTrue();
        await using var verify = harness.Verify();
        (await verify.Accounts.SingleAsync()).Platform.Should().Be("GoogleAds");
    }

    [Fact]
    public async Task Configured_list_decides_management_of_new_accounts()
    {
        var harness = new AdsSyncHarness(new AdsSyncOptions { ManagedAccounts = new[] { "GoogleAds:1" } });

        var rows = await harness.Accounts.UpsertAsync(AdPlatform.GoogleAds, new[] { AdsTestData.Account("1"), AdsTestData.Account("2") }, default);

        rows.Single(a => a.ExternalId == "1").IsManaged.Should().BeTrue();
        rows.Single(a => a.ExternalId == "2").IsManaged.Should().BeFalse();
    }

    [Fact]
    public async Task Existing_account_keeps_its_management_flag_and_gets_new_name()
    {
        // Arrange — an admin unmanaged the account in the database
        var harness = new AdsSyncHarness();
        await harness.Accounts.UpsertAsync(AdPlatform.Sklik, new[] { AdsTestData.Account("9") }, default);
        await using (var edit = harness.Verify())
        {
            (await edit.Accounts.SingleAsync()).IsManaged = false;
            await edit.SaveChangesAsync();
        }

        // Act
        await harness.Accounts.UpsertAsync(AdPlatform.Sklik, new[] { AdsTestData.Account("9") with { Name = "Renamed" } }, default);

        // Assert
        await using var verify = harness.Verify();
        var row = await verify.Accounts.SingleAsync();
        row.IsManaged.Should().BeFalse();
        row.Name.Should().Be("Renamed");
    }

    [Fact]
    public async Task SyncManaged_isolates_a_failing_source_and_returns_only_managed_accounts()
    {
        var harness = new AdsSyncHarness(new AdsSyncOptions { ManagedAccounts = new[] { "MetaAds:1" } });
        var ok = new ScriptedAdReadSource(AdPlatform.MetaAds) { Accounts = { AdsTestData.Account("1"), AdsTestData.Account("2") } };
        var broken = new ScriptedAdReadSource(AdPlatform.GoogleAds) { FailAccounts = true };

        var good = await harness.Accounts.SyncManagedAsync(ok, default);
        var bad = await harness.Accounts.SyncManagedAsync(broken, default);

        good.Error.Should().BeNull();
        good.Accounts.Select(a => a.ExternalId).Should().Equal("1");
        bad.Accounts.Should().BeEmpty();
        bad.Error.Should().Contain("GoogleAds").And.Contain("accounts unavailable");
    }
}
```

`Sync/AdEntityUpserterTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Persistence.Ads.Entities;
using Anela.Heblo.Tests.Features.MarketingAds.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Tests.Features.MarketingAds.Sync;

public class AdEntityUpserterTests
{
    private static async Task<(AdsSyncHarness Harness, long AccountId)> ArrangeAsync()
    {
        var harness = new AdsSyncHarness();
        var account = (await harness.Accounts.UpsertAsync(AdPlatform.GoogleAds, new[] { AdsTestData.Account() }, default)).Single();
        return (harness, account.Id);
    }

    private static readonly AdEntitySnapshot[] Tree =
    {
        AdsTestData.Ad("ad-1", "g-1"),          // child before parent on purpose
        AdsTestData.AdGroup("g-1", "c-1"),
        AdsTestData.Campaign("c-1"),
    };

    [Fact]
    public async Task Inserts_entities_and_resolves_parents_regardless_of_order()
    {
        var (harness, accountId) = await ArrangeAsync();

        var result = await harness.Entities.UpsertAsync(accountId, Tree, reportCreations: false, default);

        result.EntityIds.Should().HaveCount(3);
        result.Changes.Should().BeEmpty();
        await using var verify = harness.Verify();
        var rows = await verify.Entities.ToListAsync();
        var campaign = rows.Single(e => e.ExternalId == "c-1");
        var adGroup = rows.Single(e => e.ExternalId == "g-1");
        adGroup.ParentId.Should().Be(campaign.Id);
        rows.Single(e => e.ExternalId == "ad-1").ParentId.Should().Be(adGroup.Id);
        campaign.Level.Should().Be("Campaign");
    }

    [Fact]
    public async Task Identical_second_run_writes_nothing_and_reports_nothing()
    {
        var (harness, accountId) = await ArrangeAsync();
        await harness.Entities.UpsertAsync(accountId, Tree, false, default);

        var second = await harness.Entities.UpsertAsync(accountId, Tree, reportCreations: true, default);

        second.RowsWritten.Should().Be(0);
        second.Changes.Should().BeEmpty();
    }

    [Fact]
    public async Task Status_change_updates_the_row_and_is_reported()
    {
        var (harness, accountId) = await ArrangeAsync();
        await harness.Entities.UpsertAsync(accountId, Tree, false, default);
        harness.Time.Advance(TimeSpan.FromHours(1));

        var result = await harness.Entities.UpsertAsync(accountId,
            new[] { AdsTestData.Ad("ad-1", "g-1", AdEntityStatus.Paused), Tree[1], Tree[2] }, false, default);

        var change = result.Changes.Should().ContainSingle().Subject;
        change.Key.Should().Be(new AdEntityKey(AdEntityLevel.Ad, "ad-1"));
        change.Change.ChangeType.Should().Be(AdSnapshotDiffer.StatusChange);
        await using var verify = harness.Verify();
        var row = await verify.Entities.SingleAsync(e => e.ExternalId == "ad-1");
        row.Status.Should().Be("Paused");
        row.UpdatedAt.Should().Be(harness.Time.GetUtcNow());
    }

    [Fact]
    public async Task Creations_are_reported_only_when_asked()
    {
        var (harness, accountId) = await ArrangeAsync();
        await harness.Entities.UpsertAsync(accountId, new[] { Tree[2] }, false, default);

        var result = await harness.Entities.UpsertAsync(accountId, Tree, reportCreations: true, default);

        result.Changes.Select(c => (c.Key.ExternalId, c.Change.ChangeType))
            .Should().BeEquivalentTo(new[] { ("ad-1", "Created"), ("g-1", "Created") });
        result.Changes.Should().OnlyContain(c => c.EntityId > 0);
    }

    [Fact]
    public async Task Stored_attributes_with_different_formatting_are_not_a_change()
    {
        // Arrange — write the row the way jsonb hands it back
        var (harness, accountId) = await ArrangeAsync();
        await using (var seed = harness.Verify())
        {
            seed.Entities.Add(new AdEntity
            {
                AccountId = accountId, Level = "Campaign", ExternalId = "c-9", Name = "Campaign c-9", Status = "Enabled",
                AttributesJson = "{\"bidding\": \"MaxConversions\", \"budget\": \"500\"}",
                FirstSeenAt = AdsTestData.Now, LastSeenAt = AdsTestData.Now, UpdatedAt = AdsTestData.Now,
            });
            await seed.SaveChangesAsync();
        }
        var snapshot = AdsTestData.Campaign("c-9", attributes: new Dictionary<string, string?> { ["budget"] = "500", ["bidding"] = "MaxConversions" });

        // Act
        var result = await harness.Entities.UpsertAsync(accountId, new[] { snapshot }, true, default);

        // Assert
        result.Changes.Should().BeEmpty();
        result.RowsWritten.Should().Be(0);
    }

    [Fact]
    public async Task LoadIds_returns_every_entity_of_the_account_only()
    {
        var (harness, accountId) = await ArrangeAsync();
        await harness.Entities.UpsertAsync(accountId, Tree, false, default);

        var ids = await harness.Entities.LoadIdsAsync(accountId, default);
        var other = await harness.Entities.LoadIdsAsync(accountId + 1000, default);

        ids.Keys.Should().Contain(new AdEntityKey(AdEntityLevel.AdGroup, "g-1"));
        ids.Should().HaveCount(3);
        other.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false`
Expected: FAIL — `CS0246: The type or namespace name 'AdSyncStateStore' could not be found` (and `AdAccountUpserter`, `AdEntityUpserter`).

- [ ] **Step 3: Implement**

`Sync/AdSyncStateStore.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Persistence.Ads.Entities;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

public sealed record AdSyncStateSnapshot(DateTimeOffset? Watermark, string Status, DateTimeOffset? LastSuccessAt, string? LastError);

public interface IAdSyncStateStore
{
    Task<AdSyncStateSnapshot?> GetAsync(AdPlatform platform, string accountExternalId, AdSyncStream stream, CancellationToken ct);
    Task RecordSuccessAsync(AdPlatform platform, string accountExternalId, AdSyncStream stream, DateTimeOffset at, DateTimeOffset? watermark, CancellationToken ct);
    Task RecordFailureAsync(AdPlatform platform, string accountExternalId, AdSyncStream stream, string error, CancellationToken ct);
}

/// <summary>ads.sync_state, one row per platform x account x stream. Never uses FindAsync (it aliases tracked rows).</summary>
public sealed class AdSyncStateStore : IAdSyncStateStore
{
    public const int MaxErrorLength = 2000;
    private readonly AdsDbContext _db;

    private readonly TimeProvider _time;

    public AdSyncStateStore(AdsDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<AdSyncStateSnapshot?> GetAsync(AdPlatform platform, string accountExternalId, AdSyncStream stream, CancellationToken ct)
    {
        var (p, s) = (AdsDbValues.ToDb(platform), AdsDbValues.ToDb(stream));
        var row = await _db.SyncStates.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Platform == p && x.AccountExternalId == accountExternalId && x.Stream == s, ct);
        return row is null ? null : new AdSyncStateSnapshot(row.Watermark, row.Status ?? "", row.LastSuccessAt, row.LastError);
    }

    public async Task RecordSuccessAsync(AdPlatform platform, string accountExternalId, AdSyncStream stream, DateTimeOffset at, DateTimeOffset? watermark, CancellationToken ct)
    {
        var row = await LoadOrAddAsync(platform, accountExternalId, stream, ct);
        row.Status = AdsDbValues.StatusSucceeded;
        row.LastSuccessAt = at.ToUniversalTime();
        row.LastError = null;
        if (watermark is { } w)
            row.Watermark = w.ToUniversalTime();
        await SaveAsync(ct);
    }

    public async Task RecordFailureAsync(AdPlatform platform, string accountExternalId, AdSyncStream stream, string error, CancellationToken ct)
    {
        var row = await LoadOrAddAsync(platform, accountExternalId, stream, ct);
        row.Status = AdsDbValues.StatusFailed;
        row.LastError = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
        await SaveAsync(ct);
    }

    private async Task<AdSyncState> LoadOrAddAsync(AdPlatform platform, string accountExternalId, AdSyncStream stream, CancellationToken ct)
    {
        // A previous failed save may have left entities tracked; never let them ride along.
        _db.ChangeTracker.Clear();
        var (p, s) = (AdsDbValues.ToDb(platform), AdsDbValues.ToDb(stream));
        var row = await _db.SyncStates.FirstOrDefaultAsync(x => x.Platform == p && x.AccountExternalId == accountExternalId && x.Stream == s, ct);
        if (row is null)
        {
            row = new AdSyncState { Platform = p, AccountExternalId = accountExternalId, Stream = s };
            _db.SyncStates.Add(row);
        }
        row.UpdatedAt = _time.GetUtcNow();
        return row;
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
    }
}
```

`Sync/AdAccountUpserter.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Configuration;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Persistence.Ads.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

public sealed record AdManagedAccounts(IReadOnlyList<AdAccount> Accounts, string? Error);

public sealed class AdAccountUpserter
{
    private readonly AdsDbContext _db;
    private readonly TimeProvider _time;
    private readonly AdsSyncOptions _options;
    private readonly ILogger<AdAccountUpserter> _logger;

    public AdAccountUpserter(AdsDbContext db, TimeProvider time, IOptions<AdsSyncOptions> options, ILogger<AdAccountUpserter> logger)
    {
        _db = db;
        _time = time;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Lists the source's accounts, upserts them, returns the managed ones. Never throws for a source failure.</summary>
    public async Task<AdManagedAccounts> SyncManagedAsync(IAdPlatformReadSource source, CancellationToken ct)
    {
        try
        {
            var snapshots = await source.GetAccountsAsync(ct);
            var rows = await UpsertAsync(source.Platform, snapshots, ct);
            return new AdManagedAccounts(rows.Where(a => a.IsManaged).ToList(), null);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _db.ChangeTracker.Clear();
            _logger.LogError(ex, "AdsSync.AccountsFailed {Platform}", source.Platform);
            return new AdManagedAccounts(Array.Empty<AdAccount>(), $"{source.Platform}: accounts: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<AdAccount>> UpsertAsync(AdPlatform platform, IReadOnlyList<AdAccountSnapshot> snapshots, CancellationToken ct)
    {
        var platformValue = AdsDbValues.ToDb(platform);
        var unique = snapshots.Where(s => !string.IsNullOrWhiteSpace(s.ExternalId)).DistinctBy(s => s.ExternalId).ToList();
        var ids = unique.Select(s => s.ExternalId).ToList();
        var existing = await _db.Accounts
            .Where(a => a.Platform == platformValue && ids.Contains(a.ExternalId))
            .ToDictionaryAsync(a => a.ExternalId, ct);
        var managedKeys = _options.ManagedAccountKeys();
        var now = _time.GetUtcNow();

        foreach (var snapshot in unique)
        {
            if (existing.TryGetValue(snapshot.ExternalId, out var row))
            {
                // is_managed is never overwritten for a known account: an admin may have changed it.
                row.Name = snapshot.Name;
                row.Currency = snapshot.Currency;
                row.TimeZone = snapshot.TimeZone;
                row.UpdatedAt = now;
                continue;
            }

            row = new AdAccount
            {
                Platform = platformValue,
                ExternalId = snapshot.ExternalId,
                Name = snapshot.Name,
                Currency = snapshot.Currency,
                TimeZone = snapshot.TimeZone,
                IsManaged = managedKeys.Count == 0 || managedKeys.Contains($"{platform}:{snapshot.ExternalId}"),
                CreatedAt = now,
                UpdatedAt = now,
            };
            _db.Accounts.Add(row);
            existing[snapshot.ExternalId] = row;
        }

        await _db.SaveChangesAsync(ct);
        var result = existing.Values.ToList();
        _db.ChangeTracker.Clear();
        return result;
    }
}
```

`Sync/AdEntityUpserter.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Configuration;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Persistence.Ads.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

public sealed record AdObservedChange(AdEntityKey Key, long EntityId, AdFieldChange Change);

public sealed record AdEntityUpsertResult(
    IReadOnlyDictionary<AdEntityKey, long> EntityIds,
    IReadOnlyList<AdObservedChange> Changes,
    int RowsWritten);

/// <summary>
/// Upserts ad_entities by (account, level, external id) and diffs name/status/attributes against
/// the stored row — the snapshot-diff fallback's source of truth.
/// </summary>
public sealed class AdEntityUpserter
{
    /// <summary>last_seen_at is only bumped this often for unchanged rows, so an hourly run does not rewrite every row.</summary>
    public static readonly TimeSpan LastSeenRefreshInterval = TimeSpan.FromHours(12);

    private readonly AdsDbContext _db;
    private readonly TimeProvider _time;
    private readonly AdsSyncOptions _options;

    public AdEntityUpserter(AdsDbContext db, TimeProvider time, IOptions<AdsSyncOptions> options)
    {
        _db = db;
        _time = time;
        _options = options.Value;
    }

    public async Task<IReadOnlyDictionary<AdEntityKey, long>> LoadIdsAsync(long accountId, CancellationToken ct)
    {
        var rows = await _db.Entities.AsNoTracking()
            .Where(e => e.AccountId == accountId)
            .Select(e => new { e.Level, e.ExternalId, e.Id })
            .ToListAsync(ct);
        return rows.ToDictionary(r => new AdEntityKey(AdsDbValues.FromDb<AdEntityLevel>(r.Level), r.ExternalId), r => r.Id);
    }

    public async Task<AdEntityUpsertResult> UpsertAsync(long accountId, IReadOnlyList<AdEntitySnapshot> snapshots, bool reportCreations, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var existing = (await _db.Entities.Where(e => e.AccountId == accountId).ToListAsync(ct))
            .ToDictionary(e => new AdEntityKey(AdsDbValues.FromDb<AdEntityLevel>(e.Level), e.ExternalId));
        var unique = snapshots
            .Where(s => !string.IsNullOrWhiteSpace(s.ExternalId))
            .DistinctBy(s => new AdEntityKey(s.Level, s.ExternalId))
            .ToList();
        var pending = new List<(AdEntityKey Key, AdEntity Row, AdFieldChange Change)>();
        var written = 0;

        foreach (var snapshot in unique)
        {
            var key = new AdEntityKey(snapshot.Level, snapshot.ExternalId);
            var after = new AdEntityState(snapshot.Name, snapshot.Status, snapshot.Attributes);

            if (existing.TryGetValue(key, out var row))
            {
                var before = new AdEntityState(row.Name, AdsDbValues.FromDb<AdEntityStatus>(row.Status), AdAttributesJson.Parse(row.AttributesJson));
                var diff = AdSnapshotDiffer.Diff(before, after);
                if (diff.Count > 0)
                {
                    Apply(row, snapshot, now);
                    pending.AddRange(diff.Select(change => (key, row, change)));
                    written++;
                }
                else if (now - row.LastSeenAt >= LastSeenRefreshInterval)
                {
                    row.LastSeenAt = now;
                }
            }
            else
            {
                row = new AdEntity
                {
                    AccountId = accountId,
                    Level = AdsDbValues.ToDb(snapshot.Level),
                    ExternalId = snapshot.ExternalId,
                    FirstSeenAt = now,
                };
                Apply(row, snapshot, now);
                _db.Entities.Add(row);
                existing[key] = row;
                written++;
                if (reportCreations)
                    pending.Add((key, row, AdSnapshotDiffer.Created(after)));
            }

            if (written > 0 && written % _options.BatchSize == 0)
                await _db.SaveChangesAsync(ct);
        }

        await _db.SaveChangesAsync(ct); // assigns ids to new rows
        written += AssignParents(unique, existing);
        await _db.SaveChangesAsync(ct);

        var ids = existing.ToDictionary(p => p.Key, p => p.Value.Id);
        var changes = pending.Select(p => new AdObservedChange(p.Key, p.Row.Id, p.Change)).ToList();
        _db.ChangeTracker.Clear();
        return new AdEntityUpsertResult(ids, changes, written);
    }

    private static void Apply(AdEntity row, AdEntitySnapshot snapshot, DateTimeOffset now)
    {
        row.Name = snapshot.Name;
        row.Status = AdsDbValues.ToDb(snapshot.Status);
        row.AttributesJson = AdAttributesJson.Serialize(snapshot.Attributes);
        row.LastSeenAt = now;
        row.UpdatedAt = now;
    }

    private static int AssignParents(IEnumerable<AdEntitySnapshot> snapshots, IReadOnlyDictionary<AdEntityKey, AdEntity> rows)
    {
        var changed = 0;
        foreach (var snapshot in snapshots)
        {
            var row = rows[new AdEntityKey(snapshot.Level, snapshot.ExternalId)];
            long? parentId = snapshot.ParentLevel is { } level && snapshot.ParentExternalId is { } parent
                && rows.TryGetValue(new AdEntityKey(level, parent), out var parentRow)
                    ? parentRow.Id
                    : null;
            if (row.ParentId != parentId)
            {
                row.ParentId = parentId;
                changed++;
            }
        }
        return changed;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false && dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~Features.MarketingAds.Sync.AdSyncStateStoreTests|FullyQualifiedName~Features.MarketingAds.Sync.AdAccountUpserterTests|FullyQualifiedName~Features.MarketingAds.Sync.AdEntityUpserterTests"`
Expected: PASS, 14 tests.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync backend/test/Anela.Heblo.Tests/Features/MarketingAds
git commit -m "feat: upsert ad accounts and entities with sync state

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Daily fact and search-term upserters (replace per account × day)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/AdUpsertCounts.cs`, `AdDailyFactUpserter.cs`, `AdSearchTermUpserter.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/TestSupport/AdsSyncHarness.Facts.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/Sync/AdDailyFactUpserterTests.cs`, `AdSearchTermUpserterTests.cs`

**Interfaces:**
- Consumes: Task 1 (`AdEntityKey`, `AdsDbValues`), Task 4 (`AdAccountUpserter`, `AdEntityUpserter` for arranging), C1 `AdDailyFactRow`, `AdSearchTermRow`.
- Produces: `sealed record AdUpsertCounts(int Written, int Unmapped)`; `AdDailyFactUpserter(AdsDbContext, TimeProvider)`.`ReplaceDayAsync(long accountId, DateOnly date, IReadOnlyList<AdDailyFactRow> rows, IReadOnlyDictionary<AdEntityKey,long> entityIds, CancellationToken) : Task<AdUpsertCounts>`; `AdSearchTermUpserter(AdsDbContext, TimeProvider)`.`ReplaceDayAsync(long accountId, DateOnly date, IReadOnlyList<AdSearchTermRow> rows, IReadOnlyDictionary<AdEntityKey,long> entityIds, CancellationToken) : Task<AdUpsertCounts>`. Semantics: after the call, the stored rows of that account and date are exactly the incoming rows (updated / inserted / stale rows deleted); duplicates of one key are summed; rows whose entity is unknown are counted in `Unmapped` and skipped; rows for another date are ignored.

- [ ] **Step 1: Write the failing tests**

`TestSupport/AdsSyncHarness.Facts.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;

namespace Anela.Heblo.Tests.Features.MarketingAds.TestSupport;

internal sealed partial class AdsSyncHarness
{
    public AdDailyFactUpserter Facts => new(Db, Time);
    public AdSearchTermUpserter SearchTerms => new(Db, Time);

    /// <summary>Creates a managed account with campaign c-1 > ad group g-1 > ad ad-1 and returns its id and entity ids.</summary>
    public async Task<(long AccountId, IReadOnlyDictionary<AdEntityKey, long> Ids)> SeedTreeAsync(string accountExternalId = AdsTestData.AccountId)
    {
        var account = (await Accounts.UpsertAsync(AdPlatform.GoogleAds, new[] { AdsTestData.Account(accountExternalId) }, default)).Single();
        var result = await Entities.UpsertAsync(account.Id,
            new[] { AdsTestData.Campaign("c-1"), AdsTestData.AdGroup("g-1", "c-1"), AdsTestData.Ad("ad-1", "g-1") }, false, default);
        return (account.Id, result.EntityIds);
    }
}
```

`Sync/AdDailyFactUpserterTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Tests.Features.MarketingAds.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Tests.Features.MarketingAds.Sync;

public class AdDailyFactUpserterTests
{
    private static readonly DateOnly Day = AdsTestData.Yesterday;

    [Fact]
    public async Task Stores_facts_at_every_reported_level()
    {
        var harness = new AdsSyncHarness();
        var (accountId, ids) = await harness.SeedTreeAsync();

        var counts = await harness.Facts.ReplaceDayAsync(accountId, Day, new[]
        {
            AdsTestData.Fact(AdEntityLevel.Campaign, "c-1", Day, 100m),
            AdsTestData.Fact(AdEntityLevel.AdGroup, "g-1", Day, 100m),
        }, ids, default);

        counts.Should().Be(new AdUpsertCounts(2, 0));
        await using var verify = harness.Verify();
        (await verify.DailyFacts.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Rerun_updates_instead_of_duplicating()
    {
        var harness = new AdsSyncHarness();
        var (accountId, ids) = await harness.SeedTreeAsync();
        await harness.Facts.ReplaceDayAsync(accountId, Day, new[] { AdsTestData.Fact(AdEntityLevel.Campaign, "c-1", Day, 100m) }, ids, default);

        await harness.Facts.ReplaceDayAsync(accountId, Day, new[] { AdsTestData.Fact(AdEntityLevel.Campaign, "c-1", Day, 130m, conversions: 3m) }, ids, default);

        await using var verify = harness.Verify();
        var row = await verify.DailyFacts.SingleAsync();
        row.Cost.Should().Be(130m);
        row.Conversions.Should().Be(3m);
    }

    [Fact]
    public async Task Entity_no_longer_reported_for_the_day_is_removed_other_days_untouched()
    {
        var harness = new AdsSyncHarness();
        var (accountId, ids) = await harness.SeedTreeAsync();
        var earlier = Day.AddDays(-1);
        await harness.Facts.ReplaceDayAsync(accountId, earlier, new[] { AdsTestData.Fact(AdEntityLevel.Ad, "ad-1", earlier, 5m) }, ids, default);
        await harness.Facts.ReplaceDayAsync(accountId, Day, new[]
        {
            AdsTestData.Fact(AdEntityLevel.Campaign, "c-1", Day, 100m),
            AdsTestData.Fact(AdEntityLevel.Ad, "ad-1", Day, 5m),
        }, ids, default);

        var counts = await harness.Facts.ReplaceDayAsync(accountId, Day, new[] { AdsTestData.Fact(AdEntityLevel.Campaign, "c-1", Day, 100m) }, ids, default);

        counts.Written.Should().Be(2); // one update + one delete
        await using var verify = harness.Verify();
        var rows = await verify.DailyFacts.ToListAsync();
        rows.Should().HaveCount(2);
        rows.Should().ContainSingle(f => f.Date == earlier);
    }

    [Fact]
    public async Task Unknown_entities_and_other_dates_are_skipped()
    {
        var harness = new AdsSyncHarness();
        var (accountId, ids) = await harness.SeedTreeAsync();

        var counts = await harness.Facts.ReplaceDayAsync(accountId, Day, new[]
        {
            AdsTestData.Fact(AdEntityLevel.Campaign, "removed-campaign", Day, 7m),
            AdsTestData.Fact(AdEntityLevel.Campaign, "c-1", Day.AddDays(-3), 7m),
        }, ids, default);

        counts.Should().Be(new AdUpsertCounts(0, 1));
        await using var verify = harness.Verify();
        (await verify.DailyFacts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Duplicate_rows_for_one_entity_are_summed()
    {
        var harness = new AdsSyncHarness();
        var (accountId, ids) = await harness.SeedTreeAsync();

        await harness.Facts.ReplaceDayAsync(accountId, Day, new[]
        {
            AdsTestData.Fact(AdEntityLevel.Ad, "ad-1", Day, 5m, clicks: 2),
            AdsTestData.Fact(AdEntityLevel.Ad, "ad-1", Day, 7m, clicks: 3),
        }, ids, default);

        await using var verify = harness.Verify();
        var row = await verify.DailyFacts.SingleAsync();
        row.Cost.Should().Be(12m);
        row.Clicks.Should().Be(5);
    }

    [Fact]
    public async Task Another_accounts_facts_for_the_same_day_are_untouched()
    {
        var harness = new AdsSyncHarness();
        var (accountA, idsA) = await harness.SeedTreeAsync("A");
        var (accountB, idsB) = await harness.SeedTreeAsync("B");
        await harness.Facts.ReplaceDayAsync(accountB, Day, new[] { AdsTestData.Fact(AdEntityLevel.Campaign, "c-1", Day, 9m) }, idsB, default);

        await harness.Facts.ReplaceDayAsync(accountA, Day, Array.Empty<AdDailyFactRow>(), idsA, default);

        await using var verify = harness.Verify();
        (await verify.DailyFacts.SingleAsync()).EntityId.Should().Be(idsB[new AdEntityKey(AdEntityLevel.Campaign, "c-1")]);
    }
}
```

`Sync/AdSearchTermUpserterTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Tests.Features.MarketingAds.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Tests.Features.MarketingAds.Sync;

public class AdSearchTermUpserterTests
{
    private static readonly DateOnly Day = AdsTestData.Yesterday;

    [Fact]
    public async Task Inserts_updates_and_prunes_by_term_and_match_type()
    {
        var harness = new AdsSyncHarness();
        var (accountId, ids) = await harness.SeedTreeAsync();
        await harness.SearchTerms.ReplaceDayAsync(accountId, Day, new[]
        {
            AdsTestData.Term("g-1", Day, "kosmetika", 10m),
            AdsTestData.Term("g-1", Day, "zdarma", 3m),
        }, ids, default);

        var counts = await harness.SearchTerms.ReplaceDayAsync(accountId, Day, new[] { AdsTestData.Term("g-1", Day, "kosmetika", 12m) }, ids, default);

        counts.Should().Be(new AdUpsertCounts(2, 0));
        await using var verify = harness.Verify();
        var row = await verify.SearchTermsDaily.SingleAsync();
        row.SearchTerm.Should().Be("kosmetika");
        row.Cost.Should().Be(12m);
        row.MatchType.Should().Be("Exact");
    }

    [Fact]
    public async Task Missing_match_type_is_stored_as_Unknown()
    {
        var harness = new AdsSyncHarness();
        var (accountId, ids) = await harness.SeedTreeAsync();

        await harness.SearchTerms.ReplaceDayAsync(accountId, Day, new[] { AdsTestData.Term("g-1", Day, "krém", 1m, matchType: null) }, ids, default);

        await using var verify = harness.Verify();
        (await verify.SearchTermsDaily.SingleAsync()).MatchType.Should().Be(AdsDbValues.NoMatchType);
    }

    [Fact]
    public async Task Unknown_ad_group_is_counted_not_written()
    {
        var harness = new AdsSyncHarness();
        var (accountId, ids) = await harness.SeedTreeAsync();

        var counts = await harness.SearchTerms.ReplaceDayAsync(accountId, Day, new[] { AdsTestData.Term("gone", Day, "krém", 1m) }, ids, default);

        counts.Should().Be(new AdUpsertCounts(0, 1));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false`
Expected: FAIL — `CS0246: The type or namespace name 'AdDailyFactUpserter' could not be found`.

- [ ] **Step 3: Implement**

`Sync/AdUpsertCounts.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>Written = inserted + updated + deleted rows. Unmapped = rows whose entity Heblo does not know.</summary>
public sealed record AdUpsertCounts(int Written, int Unmapped);
```

`Sync/AdDailyFactUpserter.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Persistence.Ads.Entities;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>
/// Makes the stored facts of one account and day equal to what the platform reports now —
/// late conversions update rows, and a row the platform no longer reports is deleted rather
/// than left stale. Load + RemoveRange (not ExecuteDelete) so EF InMemory tests exercise it.
/// </summary>
public sealed class AdDailyFactUpserter
{
    private readonly AdsDbContext _db;
    private readonly TimeProvider _time;

    public AdDailyFactUpserter(AdsDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<AdUpsertCounts> ReplaceDayAsync(long accountId, DateOnly date, IReadOnlyList<AdDailyFactRow> rows,
        IReadOnlyDictionary<AdEntityKey, long> entityIds, CancellationToken ct)
    {
        var unmapped = 0;
        var incoming = new Dictionary<long, AdDailyFactRow>();
        foreach (var row in rows.Where(r => r.Date == date))
        {
            if (!entityIds.TryGetValue(new AdEntityKey(row.Level, row.EntityExternalId), out var entityId))
            {
                unmapped++;
                continue;
            }
            incoming[entityId] = incoming.TryGetValue(entityId, out var previous) ? Sum(previous, row) : row;
        }

        var existing = await (
            from fact in _db.DailyFacts
            join entity in _db.Entities on fact.EntityId equals entity.Id
            where entity.AccountId == accountId && fact.Date == date
            select fact).ToListAsync(ct);
        var existingByEntity = existing.ToDictionary(f => f.EntityId);

        foreach (var (entityId, row) in incoming)
        {
            if (!existingByEntity.TryGetValue(entityId, out var fact))
            {
                fact = new AdDailyFact { EntityId = entityId, Date = date };
                _db.DailyFacts.Add(fact);
            }
            fact.Impressions = row.Impressions;
            fact.Clicks = row.Clicks;
            fact.Cost = row.Cost;
            fact.Conversions = row.Conversions;
            fact.ConversionValue = row.ConversionValue;
            fact.Currency = row.Currency;
            fact.SyncedAt = _time.GetUtcNow();
        }

        var stale = existing.Where(f => !incoming.ContainsKey(f.EntityId)).ToList();
        _db.DailyFacts.RemoveRange(stale);

        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
        return new AdUpsertCounts(incoming.Count + stale.Count, unmapped);
    }

    private static AdDailyFactRow Sum(AdDailyFactRow a, AdDailyFactRow b) => a with
    {
        Impressions = a.Impressions + b.Impressions,
        Clicks = a.Clicks + b.Clicks,
        Cost = a.Cost + b.Cost,
        Conversions = a.Conversions + b.Conversions,
        ConversionValue = a.ConversionValue + b.ConversionValue,
    };
}
```

`Sync/AdSearchTermUpserter.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Persistence.Ads.Entities;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>Same replace-per-account-and-day semantics as <see cref="AdDailyFactUpserter"/>, keyed by ad group + term + match type.</summary>
public sealed class AdSearchTermUpserter
{
    private readonly AdsDbContext _db;
    private readonly TimeProvider _time;

    public AdSearchTermUpserter(AdsDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<AdUpsertCounts> ReplaceDayAsync(long accountId, DateOnly date, IReadOnlyList<AdSearchTermRow> rows,
        IReadOnlyDictionary<AdEntityKey, long> entityIds, CancellationToken ct)
    {
        var unmapped = 0;
        var incoming = new Dictionary<(long AdGroupId, string Term, string MatchType), AdSearchTermRow>();
        foreach (var row in rows.Where(r => r.Date == date && !string.IsNullOrEmpty(r.SearchTerm)))
        {
            if (!entityIds.TryGetValue(new AdEntityKey(AdEntityLevel.AdGroup, row.AdGroupExternalId), out var adGroupId))
            {
                unmapped++;
                continue;
            }
            var key = (adGroupId, row.SearchTerm, AdsDbValues.MatchTypeToDb(row.MatchType));
            incoming[key] = incoming.TryGetValue(key, out var previous) ? Sum(previous, row) : row;
        }

        var existing = await (
            from term in _db.SearchTermsDaily
            join entity in _db.Entities on term.AdGroupEntityId equals entity.Id
            where entity.AccountId == accountId && term.Date == date
            select term).ToListAsync(ct);
        var existingByKey = existing.ToDictionary(t => (t.AdGroupEntityId, t.SearchTerm, t.MatchType));

        foreach (var (key, row) in incoming)
        {
            if (!existingByKey.TryGetValue(key, out var term))
            {
                term = new AdSearchTermDaily { AdGroupEntityId = key.AdGroupId, Date = date, SearchTerm = key.Term, MatchType = key.MatchType };
                _db.SearchTermsDaily.Add(term);
            }
            term.Impressions = row.Impressions;
            term.Clicks = row.Clicks;
            term.Cost = row.Cost;
            term.Conversions = row.Conversions;
            term.ConversionValue = row.ConversionValue;
            term.Currency = row.Currency;
            term.SyncedAt = _time.GetUtcNow();
        }

        var stale = existing.Where(t => !incoming.ContainsKey((t.AdGroupEntityId, t.SearchTerm, t.MatchType))).ToList();
        _db.SearchTermsDaily.RemoveRange(stale);

        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
        return new AdUpsertCounts(incoming.Count + stale.Count, unmapped);
    }

    private static AdSearchTermRow Sum(AdSearchTermRow a, AdSearchTermRow b) => a with
    {
        Impressions = a.Impressions + b.Impressions,
        Clicks = a.Clicks + b.Clicks,
        Cost = a.Cost + b.Cost,
        Conversions = a.Conversions + b.Conversions,
        ConversionValue = a.ConversionValue + b.ConversionValue,
    };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false && dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~Features.MarketingAds.Sync.AdDailyFactUpserterTests|FullyQualifiedName~Features.MarketingAds.Sync.AdSearchTermUpserterTests"`
Expected: PASS, 9 tests.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync backend/test/Anela.Heblo.Tests/Features/MarketingAds
git commit -m "feat: replace ad daily facts and search terms per account and day

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Change event writer (dedup, entity mapping, origin)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/AdChangeEventCandidate.cs` (add `FromSnapshot`)
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/AdChangeEventWriter.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/TestSupport/AdsSyncHarness.Changes.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/Sync/AdChangeEventWriterTests.cs`

**Interfaces:**
- Consumes: Task 3 (`AdChangeEventCandidate`, `AdChangeOriginMatcher`, `IAdExecutionLookup`, `NoOpAdExecutionLookup`), Task 4 (`AdObservedChange`), Task 1 options.
- Produces: `AdChangeEventCandidate.FromSnapshot(AdObservedChange change, DateTimeOffset observedAt)` (external id `snapshot:{Level}:{ExternalId}:{ChangeType}:{unixSeconds}`, actor kind `Unknown`); `AdChangeEventWriter(AdsDbContext, IAdExecutionLookup, AdChangeOriginMatcher, TimeProvider, IOptions<AdsSyncOptions>)`.`WriteAsync(AdPlatform platform, AdAccount account, AdChangeSource source, IReadOnlyList<AdChangeEventCandidate> candidates, IReadOnlyDictionary<AdEntityKey,long> entityIds, CancellationToken) : Task<int>` (returns rows inserted; skips ids already stored for the same account + source; `occurred_at` stored in UTC; values JSON-normalised).

- [ ] **Step 1: Write the failing tests**

`TestSupport/AdsSyncHarness.Changes.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;

namespace Anela.Heblo.Tests.Features.MarketingAds.TestSupport;

internal sealed partial class AdsSyncHarness
{
    /// <summary>Replace to simulate C3's execution log.</summary>
    public IAdExecutionLookup Lookup { get; set; } = new NoOpAdExecutionLookup();

    public AdChangeEventWriter Writer => new(Db, Lookup, new AdChangeOriginMatcher(), Time, Options);
}
```

`Sync/AdChangeEventWriterTests.cs`:

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Persistence.Ads.Entities;
using Anela.Heblo.Tests.Features.MarketingAds.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Anela.Heblo.Tests.Features.MarketingAds.Sync;

public class AdChangeEventWriterTests
{
    private static async Task<(AdsSyncHarness Harness, AdAccount Account, IReadOnlyDictionary<AdEntityKey, long> Ids)> ArrangeAsync()
    {
        var harness = new AdsSyncHarness();
        var (accountId, ids) = await harness.SeedTreeAsync();
        await using var read = harness.Verify();
        var account = await read.Accounts.AsNoTracking().SingleAsync(a => a.Id == accountId);
        return (harness, account, ids);
    }

    private static AdChangeEventCandidate Candidate(string id, string? newValue = "\"Paused\"", AdEntityLevel? level = AdEntityLevel.Ad,
        string? entity = "ad-1", DateTimeOffset? at = null) =>
        AdChangeEventCandidate.FromRow(AdsTestData.Change(id, at ?? AdsTestData.Now.AddHours(-2), level, entity, newValue));

    [Fact]
    public async Task Writes_events_out_of_band_with_entity_resolved()
    {
        var (harness, account, ids) = await ArrangeAsync();

        var written = await harness.Writer.WriteAsync(AdPlatform.GoogleAds, account, AdChangeSource.PlatformChangeLog, new[] { Candidate("e1") }, ids, default);

        written.Should().Be(1);
        await using var verify = harness.Verify();
        var row = await verify.ChangeEvents.SingleAsync();
        row.Origin.Should().Be("OutOfBand");
        row.Source.Should().Be("PlatformChangeLog");
        row.EntityId.Should().Be(ids[new AdEntityKey(AdEntityLevel.Ad, "ad-1")]);
        row.EntityExternalRef.Should().Be("Ad:ad-1");
        row.MatchedExecutionId.Should().BeNull();
    }

    [Fact]
    public async Task Already_stored_and_repeated_ids_are_written_once()
    {
        var (harness, account, ids) = await ArrangeAsync();
        await harness.Writer.WriteAsync(AdPlatform.GoogleAds, account, AdChangeSource.PlatformChangeLog, new[] { Candidate("e1") }, ids, default);

        var written = await harness.Writer.WriteAsync(AdPlatform.GoogleAds, account, AdChangeSource.PlatformChangeLog,
            new[] { Candidate("e1"), Candidate("e2"), Candidate("e2") }, ids, default);

        written.Should().Be(1);
        await using var verify = harness.Verify();
        (await verify.ChangeEvents.Select(e => e.ExternalEventId).ToListAsync()).Should().BeEquivalentTo(new[] { "e1", "e2" });
    }

    [Fact]
    public async Task Stores_occurred_at_in_utc()
    {
        var (harness, account, ids) = await ArrangeAsync();
        var local = new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.FromHours(2));

        await harness.Writer.WriteAsync(AdPlatform.GoogleAds, account, AdChangeSource.PlatformChangeLog, new[] { Candidate("e1", at: local) }, ids, default);

        await using var verify = harness.Verify();
        var stored = (await verify.ChangeEvents.SingleAsync()).OccurredAt;
        stored.Offset.Should().Be(TimeSpan.Zero);
        stored.Should().Be(local);
    }

    [Fact]
    public async Task Non_json_values_are_stored_as_json_strings()
    {
        var (harness, account, ids) = await ArrangeAsync();

        await harness.Writer.WriteAsync(AdPlatform.Sklik, account, AdChangeSource.PlatformChangeLog, new[] { Candidate("e1", newValue: "PAUSED") }, ids, default);

        await using var verify = harness.Verify();
        JsonDocument.Parse((await verify.ChangeEvents.SingleAsync()).NewValueJson!).RootElement.GetString().Should().Be("PAUSED");
    }

    [Fact]
    public async Task Ambiguous_external_id_without_level_keeps_only_the_reference()
    {
        // Arrange — two entities share external id "x" on different levels
        var (harness, account, _) = await ArrangeAsync();
        var ids = (await harness.Entities.UpsertAsync(account.Id,
            new[] { AdsTestData.Campaign("x"), AdsTestData.AdGroup("x", "x") }, false, default)).EntityIds;

        // Act
        await harness.Writer.WriteAsync(AdPlatform.Sklik, account, AdChangeSource.PlatformChangeLog,
            new[] { Candidate("e1", level: null, entity: "x") }, ids, default);

        // Assert
        await using var verify = harness.Verify();
        var row = await verify.ChangeEvents.SingleAsync();
        row.EntityId.Should().BeNull();
        row.EntityExternalRef.Should().Be("x");
    }

    [Fact]
    public async Task Executed_action_in_window_marks_the_event_as_heblo()
    {
        // Arrange
        var (harness, account, ids) = await ArrangeAsync();
        var occurredAt = AdsTestData.Now.AddHours(-2);
        var lookup = new Mock<IAdExecutionLookup>();
        lookup.Setup(l => l.GetExecutedActionsAsync(AdPlatform.GoogleAds, account.ExternalId,
                occurredAt.AddMinutes(-120), occurredAt.AddMinutes(120), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new AdExecutedAction("42", AdActionType.PauseAd, AdPlatform.GoogleAds, account.ExternalId, AdEntityLevel.Ad, "ad-1",
                    null, occurredAt.AddMinutes(-10), new Dictionary<string, string>()),
            });
        harness.Lookup = lookup.Object;

        // Act
        await harness.Writer.WriteAsync(AdPlatform.GoogleAds, account, AdChangeSource.SnapshotDiff, new[] { Candidate("e1", at: occurredAt) }, ids, default);

        // Assert
        await using var verify = harness.Verify();
        var row = await verify.ChangeEvents.SingleAsync();
        row.Origin.Should().Be("Heblo");
        row.MatchedExecutionId.Should().Be("42");
    }

    [Fact]
    public async Task Snapshot_candidate_has_a_deterministic_id_and_unknown_actor()
    {
        var change = new AdObservedChange(new AdEntityKey(AdEntityLevel.Ad, "ad-1"), 5,
            new AdFieldChange(AdSnapshotDiffer.StatusChange, "\"Enabled\"", "\"Paused\""));

        var candidate = AdChangeEventCandidate.FromSnapshot(change, AdsTestData.Now);

        candidate.ExternalEventId.Should().Be($"snapshot:Ad:ad-1:Status:{AdsTestData.Now.ToUnixTimeSeconds()}");
        candidate.ActorKind.Should().Be(AdChangeActorKind.Unknown);
        candidate.Actor.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false`
Expected: FAIL — `CS0246: The type or namespace name 'AdChangeEventWriter' could not be found`; `CS0117: 'AdChangeEventCandidate' does not contain a definition for 'FromSnapshot'`.

- [ ] **Step 3: Implement**

Add to `Sync/AdChangeEventCandidate.cs` inside the record body (after `FromRow`):

```csharp
    public static AdChangeEventCandidate FromSnapshot(AdObservedChange change, DateTimeOffset observedAt) => new(
        $"snapshot:{change.Key.Level}:{change.Key.ExternalId}:{change.Change.ChangeType}:{observedAt.ToUnixTimeSeconds()}",
        observedAt.ToUniversalTime(),
        null,
        AdChangeActorKind.Unknown,
        change.Key.Level,
        change.Key.ExternalId,
        change.Change.ChangeType,
        AdJsonValues.Normalize(change.Change.OldValueJson),
        AdJsonValues.Normalize(change.Change.NewValueJson));
```

`Sync/AdChangeEventWriter.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Configuration;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Persistence.Ads.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>
/// Inserts change events once per (account, source, external id) — the table's unique key —
/// and stamps each with its origin from <see cref="AdChangeOriginMatcher"/>.
/// </summary>
public sealed class AdChangeEventWriter
{
    private readonly AdsDbContext _db;
    private readonly IAdExecutionLookup _lookup;
    private readonly AdChangeOriginMatcher _matcher;
    private readonly TimeProvider _time;
    private readonly AdsSyncOptions _options;

    public AdChangeEventWriter(AdsDbContext db, IAdExecutionLookup lookup, AdChangeOriginMatcher matcher, TimeProvider time, IOptions<AdsSyncOptions> options)
    {
        _db = db;
        _lookup = lookup;
        _matcher = matcher;
        _time = time;
        _options = options.Value;
    }

    public async Task<int> WriteAsync(AdPlatform platform, AdAccount account, AdChangeSource source,
        IReadOnlyList<AdChangeEventCandidate> candidates, IReadOnlyDictionary<AdEntityKey, long> entityIds, CancellationToken ct)
    {
        var unique = candidates
            .Where(c => !string.IsNullOrWhiteSpace(c.ExternalEventId))
            .DistinctBy(c => c.ExternalEventId)
            .ToList();
        if (unique.Count == 0)
            return 0;

        var sourceValue = AdsDbValues.ToDb(source);
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in unique.Select(c => c.ExternalEventId).Chunk(_options.BatchSize))
        {
            known.UnionWith(await _db.ChangeEvents.AsNoTracking()
                .Where(e => e.AccountId == account.Id && e.Source == sourceValue && chunk.Contains(e.ExternalEventId))
                .Select(e => e.ExternalEventId)
                .ToListAsync(ct));
        }

        var fresh = unique.Where(c => !known.Contains(c.ExternalEventId)).ToList();
        if (fresh.Count == 0)
            return 0;

        var window = TimeSpan.FromMinutes(_options.OutOfBandMatchWindowMinutes);
        var executed = await _lookup.GetExecutedActionsAsync(platform, account.ExternalId,
            fresh.Min(c => c.OccurredAt) - window, fresh.Max(c => c.OccurredAt) + window, ct);
        var rules = new AdOriginMatchingRules(window, _options.HebloActorsFor(platform));
        var uniqueByExternalId = entityIds
            .GroupBy(p => p.Key.ExternalId, StringComparer.Ordinal)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single().Value, StringComparer.Ordinal);

        var written = 0;
        foreach (var chunk in fresh.Chunk(_options.BatchSize))
        {
            foreach (var candidate in chunk)
            {
                var match = _matcher.Match(candidate, executed, rules);
                _db.ChangeEvents.Add(new AdChangeEvent
                {
                    AccountId = account.Id,
                    ExternalEventId = candidate.ExternalEventId,
                    OccurredAt = candidate.OccurredAt.ToUniversalTime(),
                    Actor = candidate.Actor,
                    ActorKind = AdsDbValues.ToDb(candidate.ActorKind),
                    EntityId = ResolveEntityId(candidate, entityIds, uniqueByExternalId),
                    EntityExternalRef = candidate.EntityExternalId is null ? null
                        : candidate.EntityLevel is { } level ? $"{level}:{candidate.EntityExternalId}" : candidate.EntityExternalId,
                    ChangeType = candidate.ChangeType,
                    OldValueJson = AdJsonValues.Normalize(candidate.OldValueJson),
                    NewValueJson = AdJsonValues.Normalize(candidate.NewValueJson),
                    Source = sourceValue,
                    Origin = AdsDbValues.ToDb(match.Origin),
                    MatchedExecutionId = match.MatchedExecutionId,
                    SyncedAt = _time.GetUtcNow(),
                });
            }
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
            written += chunk.Length;
        }

        return written;
    }

    private static long? ResolveEntityId(AdChangeEventCandidate candidate, IReadOnlyDictionary<AdEntityKey, long> entityIds,
        IReadOnlyDictionary<string, long> uniqueByExternalId)
    {
        if (candidate.EntityExternalId is null)
            return null;
        if (candidate.EntityLevel is { } level)
            return entityIds.TryGetValue(new AdEntityKey(level, candidate.EntityExternalId), out var id) ? id : null;
        return uniqueByExternalId.TryGetValue(candidate.EntityExternalId, out var only) ? only : null;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false && dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~Features.MarketingAds.Sync.AdChangeEventWriterTests"`
Expected: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync backend/test/Anela.Heblo.Tests/Features/MarketingAds
git commit -m "feat: write ad change events with dedup and out-of-band origin

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Stream runner, entity sync step, daily sync service

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/AdStreamRunner.cs`, `AdEntitySyncStep.cs`, `AdsSyncReport.cs`, `IAdsDailySyncService.cs`, `AdsDailySyncService.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/TestSupport/AdsSyncHarness.Services.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/Sync/AdsDailySyncServiceTests.cs`

**Interfaces:**
- Consumes: Tasks 1–6.
- Produces:
  - `sealed record AdStreamWork(int RowsWritten, DateTimeOffset? Watermark)`; `sealed record AdStreamOutcome(bool Succeeded, int RowsWritten, string? Error)` with `static Ok(int)`, `static Failed(string)`; `AdStreamRunner(AdsDbContext, IAdSyncStateStore, TimeProvider, ILogger<AdStreamRunner>)`.`RunAsync(AdPlatform, string accountExternalId, AdSyncStream, Func<CancellationToken, Task<AdStreamWork>> work, CancellationToken) : Task<AdStreamOutcome>` (never throws for work failures unless `ct` is cancelled).
  - `sealed record AdEntitySyncResult(AdStreamOutcome Outcome, IReadOnlyDictionary<AdEntityKey,long> EntityIds)`; `AdEntitySyncStep(AdEntityUpserter, AdChangeEventWriter, IAdSyncStateStore, AdStreamRunner, TimeProvider)`.`RunAsync(IAdPlatformReadSource, AdAccount, bool recordSnapshotDiffs, CancellationToken) : Task<AdEntitySyncResult>`.
  - `sealed record AdsSyncReport(bool Skipped, string? SkipReason, int SourcesAttempted, int RowsWritten, IReadOnlyList<string> Errors)` with `bool HasFailures`, `static Skip(string reason)`.
  - `interface IAdsDailySyncService { Task<AdsSyncReport> RunAsync(CancellationToken ct); }`; `AdsDailySyncService(IEnumerable<IAdPlatformReadSource>, AdAccountUpserter, AdEntitySyncStep, AdDailyFactUpserter, AdSearchTermUpserter, AdStreamRunner, TimeProvider, IOptions<AdsSyncOptions>, ILogger<AdsDailySyncService>)`.

- [ ] **Step 1: Write the failing tests**

`TestSupport/AdsSyncHarness.Services.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anela.Heblo.Tests.Features.MarketingAds.TestSupport;

internal sealed partial class AdsSyncHarness
{
    public AdStreamRunner Runner => new(Db, State, Time, NullLogger<AdStreamRunner>.Instance);
    public AdEntitySyncStep EntityStep => new(Entities, Writer, State, Runner, Time);

    public AdsDailySyncService Daily(params IAdPlatformReadSource[] sources) =>
        new(sources, Accounts, EntityStep, Facts, SearchTerms, Runner, Time, Options, NullLogger<AdsDailySyncService>.Instance);
}
```

`Sync/AdsDailySyncServiceTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Configuration;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Tests.Features.MarketingAds.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Tests.Features.MarketingAds.Sync;

public class AdsDailySyncServiceTests
{
    private static readonly DateOnly Day = AdsTestData.Yesterday;

    private static ScriptedAdReadSource Google(string account = AdsTestData.AccountId, bool searchTerms = true, bool changeLog = true)
    {
        var source = new ScriptedAdReadSource(AdPlatform.GoogleAds, searchTerms, changeLog) { Accounts = { AdsTestData.Account(account) } };
        source.Entities[account] = new List<AdEntitySnapshot>
        {
            AdsTestData.Campaign("c-1"), AdsTestData.AdGroup("g-1", "c-1"), AdsTestData.Ad("ad-1", "g-1"),
        };
        source.Facts[account] = new List<AdDailyFactRow>
        {
            AdsTestData.Fact(AdEntityLevel.Campaign, "c-1", Day, 100m),
            AdsTestData.Fact(AdEntityLevel.Campaign, "c-1", Day.AddDays(-14), 80m),
        };
        source.SearchTerms[account] = new List<AdSearchTermRow> { AdsTestData.Term("g-1", Day, "kosmetika", 10m) };
        return source;
    }

    [Fact]
    public async Task No_registered_source_skips_the_run()
    {
        var report = await new AdsSyncHarness().Daily().RunAsync(default);

        report.Skipped.Should().BeTrue();
        report.HasFailures.Should().BeFalse();
    }

    [Fact]
    public async Task Syncs_entities_facts_and_search_terms_for_yesterday_plus_lookback()
    {
        // Arrange
        var harness = new AdsSyncHarness();
        var source = Google();

        // Act
        var report = await harness.Daily(source).RunAsync(default);

        // Assert
        report.HasFailures.Should().BeFalse();
        source.FactRequests.Should().HaveCount(15);
        source.FactRequests.Select(r => r.Date).Should().Contain(new[] { Day, Day.AddDays(-14) });
        source.SearchTermRequests.Should().HaveCount(15);
        await using var verify = harness.Verify();
        (await verify.Entities.CountAsync()).Should().Be(3);
        (await verify.DailyFacts.CountAsync()).Should().Be(2);
        (await verify.SearchTermsDaily.CountAsync()).Should().Be(1);
        var states = await verify.SyncStates.ToListAsync();
        states.Select(s => s.Stream).Should().BeEquivalentTo(new[] { "Entities", "DailyFacts", "SearchTerms" });
        states.Should().OnlyContain(s => s.Status == AdsDbValues.StatusSucceeded);
        states.Single(s => s.Stream == "DailyFacts").Watermark.Should().Be(new DateTimeOffset(Day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
    }

    [Fact]
    public async Task Unmanaged_accounts_are_not_synced()
    {
        var harness = new AdsSyncHarness(new AdsSyncOptions { ManagedAccounts = new[] { "GoogleAds:someone-else" } });
        var source = Google();

        await harness.Daily(source).RunAsync(default);

        source.FactRequests.Should().BeEmpty();
        await using var verify = harness.Verify();
        (await verify.Accounts.SingleAsync()).IsManaged.Should().BeFalse();
    }

    [Fact]
    public async Task A_failing_source_does_not_block_the_others()
    {
        var harness = new AdsSyncHarness();
        var broken = new ScriptedAdReadSource(AdPlatform.MetaAds) { FailAccounts = true };
        var google = Google();

        var report = await harness.Daily(broken, google).RunAsync(default);

        report.Errors.Should().ContainSingle().Which.Should().Contain("MetaAds");
        await using var verify = harness.Verify();
        (await verify.DailyFacts.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task A_failing_stream_is_recorded_and_the_next_stream_still_runs()
    {
        var harness = new AdsSyncHarness();
        var source = Google();
        source.FailFactsFor.Add(AdsTestData.AccountId);

        var report = await harness.Daily(source).RunAsync(default);

        report.Errors.Should().ContainSingle().Which.Should().Contain("DailyFacts");
        await using var verify = harness.Verify();
        var states = await verify.SyncStates.ToDictionaryAsync(s => s.Stream);
        states["DailyFacts"].Status.Should().Be(AdsDbValues.StatusFailed);
        states["DailyFacts"].LastError.Should().Contain("facts unavailable");
        states["SearchTerms"].Status.Should().Be(AdsDbValues.StatusSucceeded);
    }

    [Fact]
    public async Task Sources_without_search_terms_are_never_asked_for_them()
    {
        var harness = new AdsSyncHarness();
        var meta = Google(searchTerms: false);

        await harness.Daily(meta).RunAsync(default);

        meta.SearchTermRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Source_without_change_log_gets_snapshot_diff_events_from_the_daily_run()
    {
        // Arrange — first run stores the entities, then the ad is paused outside Heblo
        var harness = new AdsSyncHarness();
        var sklik = Google(changeLog: false);
        await harness.Daily(sklik).RunAsync(default);
        sklik.Entities[AdsTestData.AccountId][2] = AdsTestData.Ad("ad-1", "g-1", AdEntityStatus.Paused);
        harness.Time.Advance(TimeSpan.FromDays(1));

        // Act
        await harness.Daily(sklik).RunAsync(default);

        // Assert
        await using var verify = harness.Verify();
        var change = await verify.ChangeEvents.SingleAsync();
        change.Source.Should().Be("SnapshotDiff");
        change.ChangeType.Should().Be("Status");
        change.ActorKind.Should().Be("Unknown");
        change.Origin.Should().Be("OutOfBand");
    }

    [Fact]
    public async Task Source_with_change_log_gets_no_snapshot_events()
    {
        var harness = new AdsSyncHarness();
        var google = Google(changeLog: true);
        await harness.Daily(google).RunAsync(default);
        google.Entities[AdsTestData.AccountId][2] = AdsTestData.Ad("ad-1", "g-1", AdEntityStatus.Paused);

        await harness.Daily(google).RunAsync(default);

        await using var verify = harness.Verify();
        (await verify.ChangeEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Invalid_options_fail_the_run_not_construction()
    {
        // Arrange — constructing must not throw (job discovery constructs every job)
        var harness = new AdsSyncHarness(new AdsSyncOptions { FactLookbackDays = 90 });
        var service = harness.Daily(Google());

        // Act
        var act = () => service.RunAsync(default);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*FactLookbackDays*");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false`
Expected: FAIL — `CS0246: The type or namespace name 'AdsDailySyncService' could not be found` (and `AdStreamRunner`, `AdEntitySyncStep`).

- [ ] **Step 3: Implement**

`Sync/AdStreamRunner.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Persistence.Ads;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>Result of one stream's work. A null watermark keeps the stored one.</summary>
public sealed record AdStreamWork(int RowsWritten, DateTimeOffset? Watermark);

public sealed record AdStreamOutcome(bool Succeeded, int RowsWritten, string? Error)
{
    public static AdStreamOutcome Ok(int rowsWritten) => new(true, rowsWritten, null);
    public static AdStreamOutcome Failed(string error) => new(false, 0, error);
}

/// <summary>
/// Runs one platform x account x stream unit: isolates its failure, clears the shared
/// DbContext's change tracker so a failed save cannot poison the next stream, and writes sync_state.
/// Cancellation is checked on the token, not by exception type: an HTTP timeout is also an
/// OperationCanceledException and must count as a stream failure.
/// </summary>
public sealed class AdStreamRunner
{
    private readonly AdsDbContext _db;
    private readonly IAdSyncStateStore _state;
    private readonly TimeProvider _time;
    private readonly ILogger<AdStreamRunner> _logger;

    public AdStreamRunner(AdsDbContext db, IAdSyncStateStore state, TimeProvider time, ILogger<AdStreamRunner> logger)
    {
        _db = db;
        _state = state;
        _time = time;
        _logger = logger;
    }

    public async Task<AdStreamOutcome> RunAsync(AdPlatform platform, string accountExternalId, AdSyncStream stream,
        Func<CancellationToken, Task<AdStreamWork>> work, CancellationToken ct)
    {
        try
        {
            var result = await work(ct);
            await _state.RecordSuccessAsync(platform, accountExternalId, stream, _time.GetUtcNow(), result.Watermark, ct);
            _logger.LogInformation("AdsSync.StreamCompleted {Platform} {Account} {Stream} rows={Rows}",
                platform, accountExternalId, stream, result.RowsWritten);
            return AdStreamOutcome.Ok(result.RowsWritten);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _db.ChangeTracker.Clear();
            _logger.LogError(ex, "AdsSync.StreamFailed {Platform} {Account} {Stream}", platform, accountExternalId, stream);
            await TryRecordFailureAsync(platform, accountExternalId, stream, ex.Message, ct);
            return AdStreamOutcome.Failed($"{platform}/{accountExternalId}/{stream}: {ex.Message}");
        }
    }

    private async Task TryRecordFailureAsync(AdPlatform platform, string accountExternalId, AdSyncStream stream, string error, CancellationToken ct)
    {
        try
        {
            await _state.RecordFailureAsync(platform, accountExternalId, stream, error, ct);
        }
        catch (Exception stateEx) when (!ct.IsCancellationRequested)
        {
            _db.ChangeTracker.Clear();
            _logger.LogError(stateEx, "AdsSync.StateWriteFailed {Platform} {Account} {Stream}", platform, accountExternalId, stream);
        }
    }
}
```

`Sync/AdEntitySyncStep.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Persistence.Ads.Entities;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

public sealed record AdEntitySyncResult(AdStreamOutcome Outcome, IReadOnlyDictionary<AdEntityKey, long> EntityIds);

/// <summary>
/// Entities stream for one account, shared by both jobs. For sources without a change log it
/// also writes snapshot-diff events — both jobs must, or the daily upsert would silently absorb
/// a change the hourly run had not seen yet. "Created" events are written only once the account
/// has had a successful entity sync, so the first run does not flood the table.
/// </summary>
public sealed class AdEntitySyncStep
{
    private static readonly IReadOnlyDictionary<AdEntityKey, long> NoIds = new Dictionary<AdEntityKey, long>();

    private readonly AdEntityUpserter _upserter;
    private readonly AdChangeEventWriter _writer;
    private readonly IAdSyncStateStore _state;
    private readonly AdStreamRunner _runner;
    private readonly TimeProvider _time;

    public AdEntitySyncStep(AdEntityUpserter upserter, AdChangeEventWriter writer, IAdSyncStateStore state, AdStreamRunner runner, TimeProvider time)
    {
        _upserter = upserter;
        _writer = writer;
        _state = state;
        _runner = runner;
        _time = time;
    }

    public async Task<AdEntitySyncResult> RunAsync(IAdPlatformReadSource source, AdAccount account, bool recordSnapshotDiffs, CancellationToken ct)
    {
        var previous = await _state.GetAsync(source.Platform, account.ExternalId, AdSyncStream.Entities, ct);
        var reportCreations = recordSnapshotDiffs && previous?.LastSuccessAt is not null;
        var ids = NoIds;

        var outcome = await _runner.RunAsync(source.Platform, account.ExternalId, AdSyncStream.Entities, async token =>
        {
            var observedAt = _time.GetUtcNow();
            var snapshots = await source.GetEntitiesAsync(account.ExternalId, token);
            var result = await _upserter.UpsertAsync(account.Id, snapshots, reportCreations, token);
            ids = result.EntityIds;

            var written = result.RowsWritten;
            if (recordSnapshotDiffs && result.Changes.Count > 0)
            {
                var candidates = result.Changes.Select(c => AdChangeEventCandidate.FromSnapshot(c, observedAt)).ToList();
                written += await _writer.WriteAsync(source.Platform, account, AdChangeSource.SnapshotDiff, candidates, ids, token);
            }
            return new AdStreamWork(written, null);
        }, ct);

        return new AdEntitySyncResult(outcome, outcome.Succeeded ? ids : NoIds);
    }
}
```

`Sync/AdsSyncReport.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

public sealed record AdsSyncReport(bool Skipped, string? SkipReason, int SourcesAttempted, int RowsWritten, IReadOnlyList<string> Errors)
{
    public bool HasFailures => Errors.Count > 0;

    public static AdsSyncReport Skip(string reason) => new(true, reason, 0, 0, Array.Empty<string>());
}
```

`Sync/IAdsDailySyncService.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

public interface IAdsDailySyncService
{
    Task<AdsSyncReport> RunAsync(CancellationToken ct);
}
```

`Sync/AdsDailySyncService.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Configuration;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Persistence.Ads.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>
/// Daily backbone run (spec section 5): for every registered source and managed account —
/// entities, then daily facts and search terms for yesterday plus FactLookbackDays.
/// Sequential on purpose: the destination is a single-vCore burstable Postgres.
/// </summary>
public sealed class AdsDailySyncService : IAdsDailySyncService
{
    private readonly IEnumerable<IAdPlatformReadSource> _sources;
    private readonly AdAccountUpserter _accounts;
    private readonly AdEntitySyncStep _entities;
    private readonly AdDailyFactUpserter _facts;
    private readonly AdSearchTermUpserter _searchTerms;
    private readonly AdStreamRunner _runner;
    private readonly TimeProvider _time;
    private readonly AdsSyncOptions _options;
    private readonly ILogger<AdsDailySyncService> _logger;

    public AdsDailySyncService(IEnumerable<IAdPlatformReadSource> sources, AdAccountUpserter accounts, AdEntitySyncStep entities,
        AdDailyFactUpserter facts, AdSearchTermUpserter searchTerms, AdStreamRunner runner, TimeProvider time,
        IOptions<AdsSyncOptions> options, ILogger<AdsDailySyncService> logger)
    {
        _sources = sources;
        _accounts = accounts;
        _entities = entities;
        _facts = facts;
        _searchTerms = searchTerms;
        _runner = runner;
        _time = time;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AdsSyncReport> RunAsync(CancellationToken ct)
    {
        _options.EnsureValid();
        var sources = _sources.ToList();
        if (sources.Count == 0)
            return AdsSyncReport.Skip("No ad platform read source is registered.");

        var errors = new List<string>();
        var rows = 0;
        foreach (var source in sources)
        {
            ct.ThrowIfCancellationRequested();
            var managed = await _accounts.SyncManagedAsync(source, ct);
            if (managed.Error is not null)
            {
                errors.Add(managed.Error);
                continue;
            }

            foreach (var account in managed.Accounts)
            {
                var outcomes = await SyncAccountAsync(source, account, ct);
                rows += outcomes.Sum(o => o.RowsWritten);
                errors.AddRange(outcomes.Where(o => !o.Succeeded).Select(o => o.Error!));
            }
        }

        _logger.LogInformation("AdsDailySync.Completed sources={Sources} rows={Rows} errors={Errors}", sources.Count, rows, errors.Count);
        return new AdsSyncReport(false, null, sources.Count, rows, errors);
    }

    private async Task<IReadOnlyList<AdStreamOutcome>> SyncAccountAsync(IAdPlatformReadSource source, AdAccount account, CancellationToken ct)
    {
        var entities = await _entities.RunAsync(source, account, recordSnapshotDiffs: !source.Capabilities.ChangeLog, ct);
        if (!entities.Outcome.Succeeded)
            return new[] { entities.Outcome };

        var yesterday = AdSyncDates.Yesterday(_time.GetUtcNow(), account.TimeZone);
        var dates = AdSyncDates.FactWindow(yesterday, _options.FactLookbackDays);
        var watermark = new DateTimeOffset(yesterday.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var outcomes = new List<AdStreamOutcome> { entities.Outcome };

        outcomes.Add(await _runner.RunAsync(source.Platform, account.ExternalId, AdSyncStream.DailyFacts, async token =>
        {
            var written = 0;
            var unmapped = 0;
            foreach (var date in dates)
            {
                var counts = await _facts.ReplaceDayAsync(account.Id, date,
                    await source.GetDailyFactsAsync(account.ExternalId, date, token), entities.EntityIds, token);
                written += counts.Written;
                unmapped += counts.Unmapped;
            }
            LogUnmapped(source.Platform, account.ExternalId, AdSyncStream.DailyFacts, unmapped);
            return new AdStreamWork(written, watermark);
        }, ct));

        if (!source.Capabilities.SearchTerms)
            return outcomes;

        outcomes.Add(await _runner.RunAsync(source.Platform, account.ExternalId, AdSyncStream.SearchTerms, async token =>
        {
            var written = 0;
            var unmapped = 0;
            foreach (var date in dates)
            {
                var counts = await _searchTerms.ReplaceDayAsync(account.Id, date,
                    await source.GetSearchTermsAsync(account.ExternalId, date, token), entities.EntityIds, token);
                written += counts.Written;
                unmapped += counts.Unmapped;
            }
            LogUnmapped(source.Platform, account.ExternalId, AdSyncStream.SearchTerms, unmapped);
            return new AdStreamWork(written, watermark);
        }, ct));

        return outcomes;
    }

    private void LogUnmapped(AdPlatform platform, string account, AdSyncStream stream, int unmapped)
    {
        if (unmapped > 0)
            _logger.LogWarning("AdsDailySync.UnmappedRows {Platform} {Account} {Stream} count={Count} — rows for entities the entity listing did not return",
                platform, account, stream, unmapped);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false && dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~Features.MarketingAds.Sync.AdsDailySyncServiceTests"`
Expected: PASS, 9 tests.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync backend/test/Anela.Heblo.Tests/Features/MarketingAds
git commit -m "feat: add daily ads sync over all registered read sources

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Hourly change sync service (change log + snapshot-diff fallback)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/IAdsChangeSyncService.cs`, `AdsChangeSyncService.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/TestSupport/AdsSyncHarness.Services.cs` (add `Change(...)`)
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/Sync/AdsChangeSyncServiceTests.cs`

**Interfaces:**
- Consumes: Tasks 1–7 (`AdChangeWindow.Since`, `AdChangeEventWriter`, `AdEntitySyncStep`, `AdStreamRunner`, `AdAccountUpserter.SyncManagedAsync`, `AdEntityUpserter.LoadIdsAsync`, `IAdSyncStateStore`).
- Produces: `interface IAdsChangeSyncService { Task<AdsSyncReport> RunAsync(CancellationToken ct); }`; `AdsChangeSyncService(IEnumerable<IAdPlatformReadSource>, AdAccountUpserter, AdEntitySyncStep, AdEntityUpserter, AdChangeEventWriter, IAdSyncStateStore, AdStreamRunner, TimeProvider, IOptions<AdsSyncOptions>, ILogger<AdsChangeSyncService>)`. Change-log watermark = the time the successful pull **started** (not the newest event time), so the next pull asks from `watermark − OverlapMinutes`.

- [ ] **Step 1: Write the failing tests**

Add to `TestSupport/AdsSyncHarness.Services.cs` inside the class:

```csharp
    public AdsChangeSyncService Change(params IAdPlatformReadSource[] sources) =>
        new(sources, Accounts, EntityStep, Entities, Writer, State, Runner, Time, Options, NullLogger<AdsChangeSyncService>.Instance);
```

`Sync/AdsChangeSyncServiceTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Tests.Features.MarketingAds.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Anela.Heblo.Tests.Features.MarketingAds.Sync;

public class AdsChangeSyncServiceTests
{
    private static ScriptedAdReadSource Source(AdPlatform platform, bool changeLog, TimeSpan? maxAge = null)
    {
        var source = new ScriptedAdReadSource(platform, searchTerms: true, changeLog: changeLog, changeLogMaxAge: maxAge)
        {
            Accounts = { AdsTestData.Account() },
        };
        source.Entities[AdsTestData.AccountId] = new List<AdEntitySnapshot>
        {
            AdsTestData.Campaign("c-1"), AdsTestData.AdGroup("g-1", "c-1"), AdsTestData.Ad("ad-1", "g-1"),
        };
        source.Changes[AdsTestData.AccountId] = new List<AdChangeEventRow>
        {
            AdsTestData.Change("g-evt-1", AdsTestData.Now.AddHours(-3), AdEntityLevel.Ad, "ad-1", "{\"status\":\"PAUSED\"}", actor: "agency@x.cz"),
        };
        return source;
    }

    [Fact]
    public async Task First_change_log_pull_reaches_back_the_initial_lookback_and_sets_the_watermark()
    {
        // Arrange
        var harness = new AdsSyncHarness();
        var google = Source(AdPlatform.GoogleAds, changeLog: true);

        // Act
        var report = await harness.Change(google).RunAsync(default);

        // Assert
        report.HasFailures.Should().BeFalse();
        google.ChangeRequests.Should().ContainSingle().Which.Since.Should().Be(AdsTestData.Now.AddDays(-7));
        await using var verify = harness.Verify();
        var change = await verify.ChangeEvents.SingleAsync();
        change.Source.Should().Be("PlatformChangeLog");
        change.Origin.Should().Be("OutOfBand");
        change.Actor.Should().Be("agency@x.cz");
        var state = await verify.SyncStates.SingleAsync(s => s.Stream == "ChangeEvents");
        state.Watermark.Should().Be(AdsTestData.Now);
    }

    [Fact]
    public async Task Next_pull_overlaps_the_watermark_and_does_not_duplicate()
    {
        var harness = new AdsSyncHarness();
        var google = Source(AdPlatform.GoogleAds, changeLog: true);
        await harness.Change(google).RunAsync(default);
        harness.Time.Advance(TimeSpan.FromHours(1));

        await harness.Change(google).RunAsync(default);

        google.ChangeRequests[1].Since.Should().Be(AdsTestData.Now.AddMinutes(-60));
        await using var verify = harness.Verify();
        (await verify.ChangeEvents.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Old_watermark_is_clamped_to_the_platform_history_limit()
    {
        // Arrange — job was off for 40 days
        var harness = new AdsSyncHarness();
        await harness.State.RecordSuccessAsync(AdPlatform.GoogleAds, AdsTestData.AccountId, AdSyncStream.ChangeEvents,
            AdsTestData.Now.AddDays(-40), AdsTestData.Now.AddDays(-40), default);
        var google = Source(AdPlatform.GoogleAds, changeLog: true, maxAge: TimeSpan.FromDays(30));

        // Act
        await harness.Change(google).RunAsync(default);

        // Assert
        google.ChangeRequests.Single().Since.Should().Be(AdsTestData.Now.AddDays(-30).AddHours(1));
    }

    [Fact]
    public async Task Source_without_change_log_falls_back_to_snapshot_diff()
    {
        var harness = new AdsSyncHarness();
        var sklik = Source(AdPlatform.Sklik, changeLog: false);
        await harness.Change(sklik).RunAsync(default);
        sklik.Entities[AdsTestData.AccountId][2] = AdsTestData.Ad("ad-1", "g-1", AdEntityStatus.Paused);
        harness.Time.Advance(TimeSpan.FromHours(1));

        await harness.Change(sklik).RunAsync(default);

        sklik.ChangeRequests.Should().BeEmpty();
        await using var verify = harness.Verify();
        var change = await verify.ChangeEvents.SingleAsync();
        change.Source.Should().Be("SnapshotDiff");
        change.OccurredAt.Should().Be(harness.Time.GetUtcNow());
    }

    [Fact]
    public async Task Failing_change_log_does_not_block_another_platform()
    {
        var harness = new AdsSyncHarness();
        var google = Source(AdPlatform.GoogleAds, changeLog: true);
        google.FailChangesFor.Add(AdsTestData.AccountId);
        var meta = Source(AdPlatform.MetaAds, changeLog: true);

        var report = await harness.Change(google, meta).RunAsync(default);

        report.Errors.Should().ContainSingle().Which.Should().Contain("GoogleAds").And.Contain("ChangeEvents");
        await using var verify = harness.Verify();
        (await verify.ChangeEvents.CountAsync()).Should().Be(1);
        var googleState = await verify.SyncStates.SingleAsync(s => s.Platform == "GoogleAds" && s.Stream == "ChangeEvents");
        googleState.Watermark.Should().BeNull();
    }

    [Fact]
    public async Task Heblo_executed_change_is_classified_as_heblo()
    {
        // Arrange
        var harness = new AdsSyncHarness();
        var lookup = new Mock<IAdExecutionLookup>();
        lookup.Setup(l => l.GetExecutedActionsAsync(AdPlatform.GoogleAds, AdsTestData.AccountId,
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new AdExecutedAction("5", AdActionType.PauseAd, AdPlatform.GoogleAds, AdsTestData.AccountId, AdEntityLevel.Ad, "ad-1",
                    null, AdsTestData.Now.AddHours(-3).AddMinutes(-5), new Dictionary<string, string>()),
            });
        harness.Lookup = lookup.Object;

        // Act
        await harness.Change(Source(AdPlatform.GoogleAds, changeLog: true)).RunAsync(default);

        // Assert
        await using var verify = harness.Verify();
        var change = await verify.ChangeEvents.SingleAsync();
        change.Origin.Should().Be("Heblo");
        change.MatchedExecutionId.Should().Be("5");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false`
Expected: FAIL — `CS0246: The type or namespace name 'AdsChangeSyncService' could not be found`.

- [ ] **Step 3: Implement**

`Sync/IAdsChangeSyncService.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

public interface IAdsChangeSyncService
{
    Task<AdsSyncReport> RunAsync(CancellationToken ct);
}
```

`Sync/AdsChangeSyncService.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Configuration;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Persistence.Ads.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>
/// Hourly change detection (spec section 5). Sources with a change log: pull events since the
/// watermark. Sources without one: re-list entities and record snapshot diffs.
/// </summary>
public sealed class AdsChangeSyncService : IAdsChangeSyncService
{
    private readonly IEnumerable<IAdPlatformReadSource> _sources;
    private readonly AdAccountUpserter _accounts;
    private readonly AdEntitySyncStep _entityStep;
    private readonly AdEntityUpserter _entities;
    private readonly AdChangeEventWriter _writer;
    private readonly IAdSyncStateStore _state;
    private readonly AdStreamRunner _runner;
    private readonly TimeProvider _time;
    private readonly AdsSyncOptions _options;
    private readonly ILogger<AdsChangeSyncService> _logger;

    public AdsChangeSyncService(IEnumerable<IAdPlatformReadSource> sources, AdAccountUpserter accounts, AdEntitySyncStep entityStep,
        AdEntityUpserter entities, AdChangeEventWriter writer, IAdSyncStateStore state, AdStreamRunner runner, TimeProvider time,
        IOptions<AdsSyncOptions> options, ILogger<AdsChangeSyncService> logger)
    {
        _sources = sources;
        _accounts = accounts;
        _entityStep = entityStep;
        _entities = entities;
        _writer = writer;
        _state = state;
        _runner = runner;
        _time = time;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AdsSyncReport> RunAsync(CancellationToken ct)
    {
        _options.EnsureValid();
        var sources = _sources.ToList();
        if (sources.Count == 0)
            return AdsSyncReport.Skip("No ad platform read source is registered.");

        var errors = new List<string>();
        var rows = 0;
        foreach (var source in sources)
        {
            ct.ThrowIfCancellationRequested();
            var managed = await _accounts.SyncManagedAsync(source, ct);
            if (managed.Error is not null)
            {
                errors.Add(managed.Error);
                continue;
            }

            foreach (var account in managed.Accounts)
            {
                var outcome = source.Capabilities.ChangeLog
                    ? await PullChangeLogAsync(source, account, ct)
                    : (await _entityStep.RunAsync(source, account, recordSnapshotDiffs: true, ct)).Outcome;
                rows += outcome.RowsWritten;
                if (!outcome.Succeeded)
                    errors.Add(outcome.Error!);
            }
        }

        _logger.LogInformation("AdsChangeSync.Completed sources={Sources} rows={Rows} errors={Errors}", sources.Count, rows, errors.Count);
        return new AdsSyncReport(false, null, sources.Count, rows, errors);
    }

    private async Task<AdStreamOutcome> PullChangeLogAsync(IAdPlatformReadSource source, AdAccount account, CancellationToken ct)
    {
        var state = await _state.GetAsync(source.Platform, account.ExternalId, AdSyncStream.ChangeEvents, ct);
        var startedAt = _time.GetUtcNow();
        var since = AdChangeWindow.Since(state?.Watermark, startedAt, _options.ChangeSync, source.Capabilities.ChangeLogMaxAge);

        return await _runner.RunAsync(source.Platform, account.ExternalId, AdSyncStream.ChangeEvents, async token =>
        {
            var rows = await source.GetChangeEventsAsync(account.ExternalId, since, token);
            var ids = await _entities.LoadIdsAsync(account.Id, token);
            var candidates = rows.Select(AdChangeEventCandidate.FromRow).ToList();
            var written = await _writer.WriteAsync(source.Platform, account, AdChangeSource.PlatformChangeLog, candidates, ids, token);
            // The watermark is when this successful pull started; the overlap on the next pull
            // re-reads anything the platform indexed late, and the unique key drops repeats.
            return new AdStreamWork(written, startedAt);
        }, ct);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false && dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~Features.MarketingAds.Sync.AdsChangeSyncServiceTests"`
Expected: PASS, 6 tests.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync backend/test/Anela.Heblo.Tests/Features/MarketingAds
git commit -m "feat: add hourly ads change sync with snapshot-diff fallback

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Recurring jobs, disabled fallback, module wiring

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/DisabledAdsSyncService.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingAds/Infrastructure/Jobs/AdsDailySyncJob.cs`, `AdsChangeSyncJob.cs`, `AdsSyncJobFailure.cs`
- Create or modify: `backend/src/Anela.Heblo.Application/Features/MarketingAds/MarketingAdsModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs` (only if C1 did not already call `AddMarketingAdsModule`)
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/Jobs/AdsDailySyncJobTests.cs`, `Jobs/AdsChangeSyncJobTests.cs`, `MarketingAdsSyncWiringTests.cs`

**Interfaces:**
- Consumes: `IAdsDailySyncService`, `IAdsChangeSyncService`, `AdsSyncReport` (Tasks 7–8); `IRecurringJob`, `RecurringJobMetadata`, `RecurringJobCategory.Marketing`, `IRecurringJobStatusChecker` (`backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/`).
- Produces: jobs `ads-daily-sync` and `ads-change-sync`; `DisabledAdsSyncService : IAdsDailySyncService, IAdsChangeSyncService` (`const string Reason`); `MarketingAdsModule.AddMarketingAdsModule(this IServiceCollection, IConfiguration)` registering everything in this PR. **Registration rules** (Review Focus 3): every component that needs `AdsDbContext` is registered through a factory (`sp => ActivatorUtilities.CreateInstance<T>(sp)`) so `ValidateOnBuild` in Development cannot trip on a missing context, and the two service interfaces resolve to `DisabledAdsSyncService` when `AdsDbContext` is not registered. `IAdExecutionLookup` → `NoOpAdExecutionLookup` (C3 replaces this line).

- [ ] **Step 1: Write the failing tests**

`Jobs/AdsDailySyncJobTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Configuration;
using Anela.Heblo.Application.Features.MarketingAds.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using FluentAssertions;
using Hangfire;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Features.MarketingAds.Jobs;

public class AdsDailySyncJobTests
{
    private readonly Mock<IAdsDailySyncService> _service = new();

    private static Mock<IRecurringJobStatusChecker> StatusChecker(bool enabled)
    {
        var mock = new Mock<IRecurringJobStatusChecker>();
        mock.Setup(s => s.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync(enabled);
        return mock;
    }

    private AdsDailySyncJob Job(bool enabled = true, AdsSyncOptions? options = null) => new(
        _service.Object, StatusChecker(enabled).Object, Options.Create(options ?? new AdsSyncOptions()),
        NullLogger<AdsDailySyncJob>.Instance);

    [Fact]
    public void Metadata_uses_agreed_name_schedule_and_category()
    {
        var metadata = Job().Metadata;

        metadata.JobName.Should().Be("ads-daily-sync");
        metadata.CronExpression.Should().Be("30 5 * * *");
        metadata.TimeZoneId.Should().Be("Europe/Prague");
        metadata.Category.Should().Be(RecurringJobCategory.Marketing);
        metadata.DefaultIsEnabled.Should().BeTrue();
    }

    [Fact]
    public void ExecuteAsync_never_retries_automatically()
    {
        var attribute = typeof(AdsDailySyncJob).GetMethod(nameof(AdsDailySyncJob.ExecuteAsync))!
            .GetCustomAttributes(typeof(AutomaticRetryAttribute), false).Cast<AutomaticRetryAttribute>().Single();

        attribute.Attempts.Should().Be(0);
    }

    [Fact]
    public void Constructing_with_invalid_options_does_not_throw()
    {
        var act = () => Job(options: new AdsSyncOptions { FactLookbackDays = 999 });

        act.Should().NotThrow();
    }

    [Fact]
    public async Task Disabled_job_does_nothing()
    {
        await Job(enabled: false).ExecuteAsync();

        _service.Verify(s => s.RunAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Skipped_run_completes_quietly()
    {
        _service.Setup(s => s.RunAsync(It.IsAny<CancellationToken>())).ReturnsAsync(AdsSyncReport.Skip(DisabledAdsSyncService.Reason));

        await Job().Invoking(j => j.ExecuteAsync()).Should().NotThrowAsync();
    }

    [Fact]
    public async Task Run_with_stream_failures_throws_so_hangfire_shows_failed()
    {
        _service.Setup(s => s.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdsSyncReport(false, null, 2, 40, new[] { "GoogleAds/1/DailyFacts: 403" }));

        await Job().Invoking(j => j.ExecuteAsync()).Should().ThrowAsync<InvalidOperationException>().WithMessage("*GoogleAds/1/DailyFacts: 403*");
    }
}
```

`Jobs/AdsChangeSyncJobTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Configuration;
using Anela.Heblo.Application.Features.MarketingAds.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Features.MarketingAds.Jobs;

public class AdsChangeSyncJobTests
{
    private readonly Mock<IAdsChangeSyncService> _service = new();

    private AdsChangeSyncJob Job(bool enabled = true)
    {
        var checker = new Mock<IRecurringJobStatusChecker>();
        checker.Setup(s => s.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync(enabled);
        return new AdsChangeSyncJob(_service.Object, checker.Object, Options.Create(new AdsSyncOptions()), NullLogger<AdsChangeSyncJob>.Instance);
    }

    [Fact]
    public void Metadata_is_hourly()
    {
        var metadata = Job().Metadata;

        metadata.JobName.Should().Be("ads-change-sync");
        metadata.CronExpression.Should().Be("15 * * * *");
        metadata.Category.Should().Be(RecurringJobCategory.Marketing);
    }

    [Fact]
    public async Task Enabled_job_runs_the_service_once()
    {
        _service.Setup(s => s.RunAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new AdsSyncReport(false, null, 1, 3, Array.Empty<string>()));

        await Job().ExecuteAsync();

        _service.Verify(s => s.RunAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Many_errors_are_capped_in_the_failure_message()
    {
        var errors = Enumerable.Range(1, 25).Select(i => $"err-{i}").ToArray();
        _service.Setup(s => s.RunAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new AdsSyncReport(false, null, 1, 0, errors));

        var thrown = await Job().Invoking(j => j.ExecuteAsync()).Should().ThrowAsync<InvalidOperationException>();

        thrown.Which.Message.Should().Contain("err-10").And.NotContain("err-11").And.Contain("+15 more");
    }
}
```

`MarketingAdsModuleTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Tests.Features.MarketingAds.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Anela.Heblo.Tests.Features.MarketingAds;

/// <summary>
/// AddRecurringJobs() registers every IRecurringJob in the Application assembly, and job discovery
/// resolves them all at once — one unresolvable dependency would unschedule every job in Heblo.
/// </summary>
public class MarketingAdsSyncWiringTests
{
    private static ServiceProvider Build(Action<IServiceCollection>? extra = null)
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Mock.Of<IRecurringJobStatusChecker>());
        extra?.Invoke(services);
        services.AddMarketingAdsModule(configuration);
        // what AddRecurringJobs() does for Application-assembly jobs
        services.AddScoped<IRecurringJob, AdsDailySyncJob>();
        services.AddScoped<IRecurringJob, AdsChangeSyncJob>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public void Without_ads_database_jobs_resolve_and_sync_is_disabled()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetServices<IRecurringJob>().Should().HaveCount(2);
        scope.ServiceProvider.GetRequiredService<IAdsDailySyncService>().Should().BeOfType<DisabledAdsSyncService>();
        scope.ServiceProvider.GetRequiredService<IAdsChangeSyncService>().Should().BeOfType<DisabledAdsSyncService>();
    }

    [Fact]
    public void With_ads_database_the_real_services_resolve()
    {
        var name = AdsTestDb.NewName();
        using var provider = Build(s => s.AddDbContext<AdsDbContext>(o => o.UseInMemoryDatabase(name)));
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IAdsDailySyncService>().Should().BeOfType<AdsDailySyncService>();
        scope.ServiceProvider.GetRequiredService<IAdsChangeSyncService>().Should().BeOfType<AdsChangeSyncService>();
        scope.ServiceProvider.GetRequiredService<IAdExecutionLookup>().Should().BeOfType<NoOpAdExecutionLookup>();
    }

    [Fact]
    public async Task Disabled_service_reports_skipped()
    {
        var report = await new DisabledAdsSyncService().RunAsync(default);

        report.Skipped.Should().BeTrue();
        report.SkipReason.Should().Contain("AdsDatabase");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false`
Expected: FAIL — `CS0246: The type or namespace name 'AdsDailySyncJob' could not be found` (and `DisabledAdsSyncService`, `AddMarketingAdsModule` if C1 did not create it).

- [ ] **Step 3: Implement**

`Sync/DisabledAdsSyncService.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>Registered when AdsDbContext is not, so both jobs still resolve (and skip) in unconfigured environments.</summary>
public sealed class DisabledAdsSyncService : IAdsDailySyncService, IAdsChangeSyncService
{
    public const string Reason = "AdsDbContext is not registered (AdsDatabase:ConnectionString is not configured).";

    public Task<AdsSyncReport> RunAsync(CancellationToken ct) => Task.FromResult(AdsSyncReport.Skip(Reason));
}
```

`Infrastructure/Jobs/AdsSyncJobFailure.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Sync;

namespace Anela.Heblo.Application.Features.MarketingAds.Infrastructure.Jobs;

/// <summary>Turns a report with failures into the exception Hangfire persists; capped, every error is already logged.</summary>
internal static class AdsSyncJobFailure
{
    private const int MaxReportedErrors = 10;

    public static InvalidOperationException From(string jobName, AdsSyncReport report)
    {
        var shown = string.Join(" | ", report.Errors.Take(MaxReportedErrors));
        var more = report.Errors.Count > MaxReportedErrors ? $" | (+{report.Errors.Count - MaxReportedErrors} more)" : "";
        return new InvalidOperationException($"{jobName}: {report.Errors.Count} stream(s) failed: {shown}{more}");
    }
}
```

`Infrastructure/Jobs/AdsDailySyncJob.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Configuration;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingAds.Infrastructure.Jobs;

public sealed class AdsDailySyncJob : IRecurringJob
{
    private const int LockTimeoutSeconds = 3600;

    private readonly IAdsDailySyncService _sync;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly AdsSyncOptions _options;
    private readonly ILogger<AdsDailySyncJob> _logger;

    public RecurringJobMetadata Metadata { get; }

    public AdsDailySyncJob(IAdsDailySyncService sync, IRecurringJobStatusChecker statusChecker,
        IOptions<AdsSyncOptions> options, ILogger<AdsDailySyncJob> logger)
    {
        _sync = sync ?? throw new ArgumentNullException(nameof(sync));
        _statusChecker = statusChecker ?? throw new ArgumentNullException(nameof(statusChecker));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // No validation here: job discovery constructs every job, and a throw would unschedule all of them.
        Metadata = new RecurringJobMetadata
        {
            JobName = "ads-daily-sync",
            DisplayName = "Ads Daily Sync",
            Description = "Pulls ad accounts, campaigns/ad groups/keywords/ads, daily cost and conversion facts (yesterday plus a 14-day lookback) and search terms from every configured ad platform into the ads schema.",
            CronExpression = _options.DailySync.CronExpression,
            // Always true: the seeder persists this on first run. The runtime no-op when no database
            // or no platform is configured protects unconfigured environments instead.
            DefaultIsEnabled = true,
            TimeZoneId = _options.DailySync.TimeZone,
            Category = RecurringJobCategory.Marketing,
        };
    }

    [DisableConcurrentExecution(LockTimeoutSeconds)]
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken, Metadata.DefaultIsEnabled))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(_options.DailySync.RunTimeoutMinutes));

        _logger.LogInformation("Job {JobName} started.", Metadata.JobName);
        var report = await _sync.RunAsync(timeout.Token);

        if (report.Skipped)
        {
            _logger.LogInformation("Job {JobName} skipped: {Reason}", Metadata.JobName, report.SkipReason);
            return;
        }

        _logger.LogInformation("Job {JobName} finished. Sources={Sources} Rows={Rows} Errors={Errors}",
            Metadata.JobName, report.SourcesAttempted, report.RowsWritten, report.Errors.Count);
        if (report.HasFailures)
            throw AdsSyncJobFailure.From(Metadata.JobName, report);
    }
}
```

`Infrastructure/Jobs/AdsChangeSyncJob.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Configuration;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingAds.Infrastructure.Jobs;

public sealed class AdsChangeSyncJob : IRecurringJob
{
    private const int LockTimeoutSeconds = 1800;

    private readonly IAdsChangeSyncService _sync;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly AdsSyncOptions _options;
    private readonly ILogger<AdsChangeSyncJob> _logger;

    public RecurringJobMetadata Metadata { get; }

    public AdsChangeSyncJob(IAdsChangeSyncService sync, IRecurringJobStatusChecker statusChecker,
        IOptions<AdsSyncOptions> options, ILogger<AdsChangeSyncJob> logger)
    {
        _sync = sync ?? throw new ArgumentNullException(nameof(sync));
        _statusChecker = statusChecker ?? throw new ArgumentNullException(nameof(statusChecker));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Metadata = new RecurringJobMetadata
        {
            JobName = "ads-change-sync",
            DisplayName = "Ads Change Sync",
            Description = "Pulls every ad platform's change history (or diffs entity snapshots where a platform has none) into ads.ad_change_events and flags each change as made by Heblo or out-of-band.",
            CronExpression = _options.ChangeSync.CronExpression,
            DefaultIsEnabled = true,
            TimeZoneId = _options.ChangeSync.TimeZone,
            Category = RecurringJobCategory.Marketing,
        };
    }

    [DisableConcurrentExecution(LockTimeoutSeconds)]
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken, Metadata.DefaultIsEnabled))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(_options.ChangeSync.RunTimeoutMinutes));

        var report = await _sync.RunAsync(timeout.Token);
        if (report.Skipped)
        {
            _logger.LogInformation("Job {JobName} skipped: {Reason}", Metadata.JobName, report.SkipReason);
            return;
        }

        _logger.LogInformation("Job {JobName} finished. Sources={Sources} Rows={Rows} Errors={Errors}",
            Metadata.JobName, report.SourcesAttempted, report.RowsWritten, report.Errors.Count);
        if (report.HasFailures)
            throw AdsSyncJobFailure.From(Metadata.JobName, report);
    }
}
```

`MarketingAdsModule.cs` — if C1 created it, add the body lines below to its existing `AddMarketingAdsModule` (keep C1's lines and its signature; if C1's signature has no `IConfiguration`, add the parameter and update the single call site in `ApplicationModule.cs`). Otherwise create:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Configuration;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Persistence.Ads;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Anela.Heblo.Application.Features.MarketingAds;

public static class MarketingAdsModule
{
    public static IServiceCollection AddMarketingAdsModule(this IServiceCollection services, IConfiguration configuration)
    {
        // No ValidateOnStart: invalid Ads settings must fail a job run, never app startup (AdsSyncOptions.EnsureValid).
        services.Configure<AdsSyncOptions>(configuration.GetSection(AdsSyncOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);

        // Fallback until C3: nothing is executed by Heblo yet. C3 replaces this registration.
        services.AddScoped<IAdExecutionLookup, NoOpAdExecutionLookup>();
        services.AddSingleton<AdChangeOriginMatcher>();

        // Everything below needs AdsDbContext, which C1 registers only when AdsDatabase:ConnectionString
        // is configured. Factories keep ValidateOnBuild (on in Development) from failing the app when
        // it is absent; nothing resolves them unless the context exists.
        AddAdsScoped<IAdSyncStateStore, AdSyncStateStore>(services);
        AddAdsScoped<AdAccountUpserter>(services);
        AddAdsScoped<AdEntityUpserter>(services);
        AddAdsScoped<AdDailyFactUpserter>(services);
        AddAdsScoped<AdSearchTermUpserter>(services);
        AddAdsScoped<AdChangeEventWriter>(services);
        AddAdsScoped<AdStreamRunner>(services);
        AddAdsScoped<AdEntitySyncStep>(services);

        // Jobs (auto-discovered by AddRecurringJobs) depend only on these two, which always resolve.
        services.AddScoped<IAdsDailySyncService>(sp => sp.GetService<AdsDbContext>() is null
            ? new DisabledAdsSyncService()
            : ActivatorUtilities.CreateInstance<AdsDailySyncService>(sp));
        services.AddScoped<IAdsChangeSyncService>(sp => sp.GetService<AdsDbContext>() is null
            ? new DisabledAdsSyncService()
            : ActivatorUtilities.CreateInstance<AdsChangeSyncService>(sp));

        // MediatR handlers and IRecurringJob implementations are discovered by assembly scan.
        return services;
    }

    private static void AddAdsScoped<TService, TImplementation>(IServiceCollection services)
        where TService : class
        where TImplementation : class, TService =>
        services.AddScoped<TService>(sp => ActivatorUtilities.CreateInstance<TImplementation>(sp));

    private static void AddAdsScoped<TService>(IServiceCollection services)
        where TService : class =>
        services.AddScoped(sp => ActivatorUtilities.CreateInstance<TService>(sp));
}
```

In `backend/src/Anela.Heblo.Application/ApplicationModule.cs`, if `AddMarketingAdsModule` is not called yet, add it directly after `services.AddMarketingPerformanceModule(configuration);`:

```csharp
        services.AddMarketingAdsModule(configuration);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false && dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~Features.MarketingAds.Jobs|FullyQualifiedName~Features.MarketingAds.MarketingAdsSyncWiringTests|FullyQualifiedName~Features.MarketingAds.MarketingAdsModuleTests"`
Expected: PASS, 13 tests.

Then the whole-app startup tests, which build the real container (they would catch a broken job discovery):

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~ApplicationStartupTests"`
Expected: PASS (same count as on `main`).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application backend/test/Anela.Heblo.Tests/Features/MarketingAds
git commit -m "feat: schedule ads daily and change sync jobs

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: Postgres integration test for the sync write path

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/Integration/AdsPostgres.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/Integration/AdsSyncPostgresIntegrationTests.cs`

**Interfaces:**
- Consumes: `PostgresSharedContainerFixture` / collection `"PostgresIntegration"` (`backend/test/Anela.Heblo.Tests/Common/`), C1 migrations, `AddMarketingAdsModule` (Task 9), `ScriptedAdReadSource`, `AdsTestData`.
- Produces: `AdsPostgres.CreateMigratedAsync(PostgresSharedContainerFixture, string hint, bool withShoptet) : Task<string>` (connection string of a fresh DB with the `ads` migration — and `shoptet_raw` when asked — applied, role `metabase_ro` present); `AdsPostgres.Options(string connectionString) : DbContextOptions<AdsDbContext>`. Used again by Tasks 11–12.

What InMemory cannot show and this task pins: jsonb normalisation (Review Focus 1), `timestamptz` offset rule (Review Focus 2), real unique keys/PKs under re-runs, and the history-table pin.

- [ ] **Step 1: Write the failing test**

`Integration/AdsPostgres.cs`:

```csharp
using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Persistence.ShoptetOrders;
using Anela.Heblo.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Tests.Features.MarketingAds.Integration;

internal static class AdsPostgres
{
    public static DbContextOptions<AdsDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<AdsDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", AdsDbContext.SchemaName))
            .Options;

    public static async Task<string> CreateMigratedAsync(PostgresSharedContainerFixture fixture, string hint, bool withShoptet)
    {
        var connectionString = await fixture.CreateDatabaseAsync(hint);

        if (withShoptet)
        {
            var shoptet = new DbContextOptionsBuilder<ShoptetOrdersDbContext>()
                .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ShoptetOrdersDbContext.SchemaName))
                .Options;
            await using var shoptetDb = new ShoptetOrdersDbContext(shoptet);
            await shoptetDb.Database.MigrateAsync();
        }

        await using var db = new AdsDbContext(Options(connectionString));
        await db.Database.MigrateAsync();
        // Roles are cluster-wide and the container is shared between test classes.
        await db.Database.ExecuteSqlRawAsync(
            "DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'metabase_ro') THEN CREATE ROLE metabase_ro; END IF; END $$;");
        return connectionString;
    }
}
```

`Integration/AdsSyncPostgresIntegrationTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Tests.Common;
using Anela.Heblo.Tests.Features.MarketingAds.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Anela.Heblo.Tests.Features.MarketingAds.Integration;

[Collection("PostgresIntegration")]
[Trait("Category", "Integration")]
public class AdsSyncPostgresIntegrationTests : IAsyncLifetime
{
    private readonly PostgresSharedContainerFixture _fixture;
    private string _connectionString = null!;

    public AdsSyncPostgresIntegrationTests(PostgresSharedContainerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => _connectionString = await AdsPostgres.CreateMigratedAsync(_fixture, "ads_sync", withShoptet: false);

    public Task DisposeAsync() => Task.CompletedTask;

    private ServiceProvider Provider(ScriptedAdReadSource source, FakeTimeProvider time)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(time);
        services.AddDbContext<AdsDbContext>(o => o.UseNpgsql(_connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", AdsDbContext.SchemaName)));
        services.AddSingleton<IAdPlatformReadSource>(source);
        services.AddMarketingAdsModule(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }

    private static ScriptedAdReadSource SklikWithoutChangeLog()
    {
        var source = new ScriptedAdReadSource(AdPlatform.Sklik, searchTerms: true, changeLog: false) { Accounts = { AdsTestData.Account() } };
        source.Entities[AdsTestData.AccountId] = new List<AdEntitySnapshot>
        {
            // keys deliberately unsorted and of different lengths: jsonb reorders them
            AdsTestData.Campaign("c-1", attributes: new Dictionary<string, string?> { ["zz_budget"] = "500", ["a"] = "x", ["bidding"] = null }),
            AdsTestData.AdGroup("g-1", "c-1"),
            AdsTestData.Ad("ad-1", "g-1"),
        };
        source.Facts[AdsTestData.AccountId] = new List<AdDailyFactRow> { AdsTestData.Fact(AdEntityLevel.Campaign, "c-1", AdsTestData.Yesterday, 100m) };
        source.SearchTerms[AdsTestData.AccountId] = new List<AdSearchTermRow> { AdsTestData.Term("g-1", AdsTestData.Yesterday, "krém", 3m, matchType: null) };
        return source;
    }

    private static async Task RunDailyAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var report = await scope.ServiceProvider.GetRequiredService<IAdsDailySyncService>().RunAsync(default);
        report.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Second_identical_run_writes_no_snapshot_diff_events()
    {
        // Arrange
        var time = new FakeTimeProvider(AdsTestData.Now);
        await using var provider = Provider(SklikWithoutChangeLog(), time);

        // Act
        await RunDailyAsync(provider);
        time.Advance(TimeSpan.FromDays(1));
        await RunDailyAsync(provider);

        // Assert — through a fresh context
        await using var verify = new AdsDbContext(AdsPostgres.Options(_connectionString));
        (await verify.ChangeEvents.CountAsync()).Should().Be(0);
        (await verify.Entities.CountAsync()).Should().Be(3);
        (await verify.DailyFacts.CountAsync()).Should().Be(1);
        (await verify.SearchTermsDaily.SingleAsync()).MatchType.Should().Be(AdsDbValues.NoMatchType);
        (await verify.SyncStates.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task Status_change_is_detected_against_jsonb_stored_state()
    {
        var time = new FakeTimeProvider(AdsTestData.Now);
        var source = SklikWithoutChangeLog();
        await using var provider = Provider(source, time);
        await RunDailyAsync(provider);
        source.Entities[AdsTestData.AccountId][2] = AdsTestData.Ad("ad-1", "g-1", AdEntityStatus.Paused);
        time.Advance(TimeSpan.FromHours(1));

        await RunDailyAsync(provider);

        await using var verify = new AdsDbContext(AdsPostgres.Options(_connectionString));
        var change = await verify.ChangeEvents.SingleAsync();
        change.ChangeType.Should().Be("Status");
        change.Origin.Should().Be("OutOfBand");
    }

    [Fact]
    public async Task Change_event_with_offset_timestamp_persists()
    {
        // Arrange
        var time = new FakeTimeProvider(AdsTestData.Now);
        var source = SklikWithoutChangeLog();
        await using var provider = Provider(source, time);
        await RunDailyAsync(provider);
        using var scope = provider.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<AdChangeEventWriter>();
        var db = scope.ServiceProvider.GetRequiredService<AdsDbContext>();
        var account = await db.Accounts.AsNoTracking().SingleAsync();
        var local = new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.FromHours(2));
        var row = AdsTestData.Change("evt-1", local, AdEntityLevel.Ad, "ad-1", "PAUSED");

        // Act
        await writer.WriteAsync(AdPlatform.Sklik, account, AdChangeSource.PlatformChangeLog,
            new[] { AdChangeEventCandidate.FromRow(row) }, new Dictionary<AdEntityKey, long>(), default);

        // Assert
        await using var verify = new AdsDbContext(AdsPostgres.Options(_connectionString));
        var stored = await verify.ChangeEvents.SingleAsync();
        stored.OccurredAt.Should().Be(local);
        stored.NewValueJson.Should().Contain("PAUSED");
    }

    [Fact]
    public async Task Migration_history_lives_in_the_ads_schema()
    {
        await using var verify = new AdsDbContext(AdsPostgres.Options(_connectionString));
        var count = await verify.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM ads.\"__EFMigrationsHistory\"")
            .SingleAsync();
        count.Should().BeGreaterThan(0);
    }
}
```

- [ ] **Step 2: Run the test**

This task adds no production code: it pins behaviour Tasks 2–9 already implement against a real Postgres. Start podman, build, run:

Run: `podman machine start; dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false && dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "Category=Integration&FullyQualifiedName~Features.MarketingAds.Integration.AdsSyncPostgresIntegrationTests"`
Expected: PASS, 4 tests. To prove the jsonb test is not vacuous, temporarily change `AdEntityUpserter` to compare `row.AttributesJson != AdAttributesJson.Serialize(snapshot.Attributes)` instead of the parsed diff and re-run `Second_identical_run_writes_no_snapshot_diff_events` — expected FAIL (`Expected ... 0, but found 1`). Revert the change.

If `Migration_history_lives_in_the_ads_schema` fails, C1 did not pin `MigrationsHistoryTable` — fix it in C1's `AdsPersistenceModule` and `AdsDbContextFactory` (both), per ADR-007, in this PR.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/MarketingAds/Integration
git commit -m "test: pin ads sync persistence against real postgres

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 11: Metabase views and grants

**Files:**
- Modify (replace placeholder): `backend/src/Anela.Heblo.Persistence.Ads/Sql/ads_read_views.sql`
- Modify: `docs/architecture/metabase.md`
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/Integration/AdsReadViewsIntegrationTests.cs`

**Interfaces:**
- Consumes: C1 tables (`ads.ad_accounts`, `ad_entities`, `ad_daily_facts`, `ad_change_events`); `shoptet_raw."order"` (`order_date date`, `status_id int` with `-4` = cancelled, `cash_desk_order bool`, `price_without_vat numeric NULL`, `exchange_rate numeric NULL` = order currency per CZK); `AdsPostgres` (Task 10).
- Produces views (column names are the contract for Metabase and for `AdsReadRepository` in Task 12):
  - `ads.v_ads_campaign_monthly(month, platform, account_external_id, account_name, campaign_external_id, campaign_name, campaign_status, currency, days_with_data, impressions, clicks, cost, conversions, conversion_value, ctr_pct, avg_cpc, platform_roas)` — granted.
  - `ads.v_ads_change_events(id, occurred_at, platform, account_external_id, account_name, entity_level, entity_external_id, entity_name, change_type, actor_kind, actor, source, origin, matched_execution_id, old_value, new_value)` — granted.
  - `ads.v_ads_blended_daily(date, ad_cost_czk, google_ads_cost_czk, meta_ads_cost_czk, sklik_cost_czk, has_non_czk_ad_cost, orders, revenue_without_vat_czk, eshop_revenue_without_vat_czk)` — **not** granted (Heblo's MCP handler reads it).
  - `ads.v_ads_blended_monthly(month, days_with_data, ad_cost_czk, google_ads_cost_czk, meta_ads_cost_czk, sklik_cost_czk, has_non_czk_ad_cost, orders, revenue_without_vat_czk, eshop_revenue_without_vat_czk, blended_roas, pno_pct)` — granted.
  - Blended views exist only where `shoptet_raw."order"` exists.

- [ ] **Step 1: Write the failing test**

`Integration/AdsReadViewsIntegrationTests.cs`:

```csharp
using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Persistence.Ads.Entities;
using Anela.Heblo.Persistence.ShoptetOrders;
using Anela.Heblo.Persistence.ShoptetOrders.Entities;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Tests.Features.MarketingAds.Integration;

[Collection("PostgresIntegration")]
[Trait("Category", "Integration")]
public class AdsReadViewsIntegrationTests
{
    private static readonly DateOnly Day = new(2026, 9, 1);
    private static readonly DateTimeOffset Synced = new(2026, 9, 2, 3, 0, 0, TimeSpan.Zero);
    private static readonly string AdsScript = Path.Combine(AppContext.BaseDirectory, "MarketingAds", "Sql", "ads_read_views.sql");
    private static readonly string ShoptetScript = Path.Combine(AppContext.BaseDirectory, "MarketingAds", "Sql", "shoptet_raw_views.sql");

    private readonly PostgresSharedContainerFixture _fixture;

    public AdsReadViewsIntegrationTests(PostgresSharedContainerFixture fixture) => _fixture = fixture;

    private static async Task RunScriptAsync(DbContext db, string path) =>
        await db.Database.ExecuteSqlRawAsync(await File.ReadAllTextAsync(path));

    private static async Task<long> CampaignAsync(AdsDbContext db, string platform, string account, string currency, bool managed, decimal cost)
    {
        var acc = new AdAccount { Platform = platform, ExternalId = account, Name = account, Currency = currency, TimeZone = "Europe/Prague", IsManaged = managed };
        db.Accounts.Add(acc);
        await db.SaveChangesAsync();
        var campaign = NewEntity(acc.Id, "Campaign", $"{account}-c", null);
        db.Entities.Add(campaign);
        await db.SaveChangesAsync();
        var adGroup = NewEntity(acc.Id, "AdGroup", $"{account}-g", campaign.Id);
        db.Entities.Add(adGroup);
        await db.SaveChangesAsync();
        db.DailyFacts.Add(Fact(campaign.Id, cost, currency));
        db.DailyFacts.Add(Fact(adGroup.Id, cost, currency)); // same money at a lower level: must not be added twice
        await db.SaveChangesAsync();
        return acc.Id;
    }

    private static AdEntity NewEntity(long accountId, string level, string externalId, long? parentId) => new()
    {
        AccountId = accountId, Level = level, ExternalId = externalId, ParentId = parentId, Name = externalId, Status = "Enabled",
        AttributesJson = "{}", FirstSeenAt = Synced, LastSeenAt = Synced, UpdatedAt = Synced,
    };

    private static AdDailyFact Fact(long entityId, decimal cost, string currency) => new()
    {
        EntityId = entityId, Date = Day, Impressions = 1000, Clicks = 50, Cost = cost, Conversions = 2, ConversionValue = cost * 4, Currency = currency,
    };

    private static ShoptetOrder Order(string code, decimal withoutVat, int status = 2, decimal rate = 1m, bool cashDesk = false) => new()
    {
        Code = code, CreationTime = Synced, OrderDate = Day, StatusId = status, PriceWithoutVat = withoutVat,
        ExchangeRate = rate, CashDeskOrder = cashDesk, SyncedAt = Synced,
    };

    private async Task<(AdsDbContext Ads, ShoptetOrdersDbContext Shoptet)> SeedAsync()
    {
        var connectionString = await AdsPostgres.CreateMigratedAsync(_fixture, "ads_views", withShoptet: true);
        var ads = new AdsDbContext(AdsPostgres.Options(connectionString));
        var shoptet = new ShoptetOrdersDbContext(new DbContextOptionsBuilder<ShoptetOrdersDbContext>().UseNpgsql(connectionString).Options);

        await CampaignAsync(ads, "GoogleAds", "g", "CZK", managed: true, cost: 100m);
        await CampaignAsync(ads, "Sklik", "s", "CZK", managed: true, cost: 50m);
        await CampaignAsync(ads, "GoogleAds", "agency", "CZK", managed: false, cost: 999m);
        await CampaignAsync(ads, "MetaAds", "m-eur", "EUR", managed: true, cost: 10m);

        shoptet.Orders.AddRange(
            Order("A1", 1000m),
            Order("EUR1", 40m, rate: 0.04m),        // 40 EUR / 0.04 = 1000 CZK
            Order("SHOP1", 300m, cashDesk: true),   // revenue yes, e-shop revenue no
            Order("X1", 5000m, status: -4));        // cancelled
        await shoptet.SaveChangesAsync();
        return (ads, shoptet);
    }

    [Fact]
    public async Task Campaign_monthly_reads_one_level_of_managed_accounts()
    {
        var (ads, shoptet) = await SeedAsync();
        await using var _ = ads;
        await using var __ = shoptet;
        await RunScriptAsync(ads, AdsScript);

        var costs = await ads.Database
            .SqlQuery<decimal>($"SELECT cost AS \"Value\" FROM ads.v_ads_campaign_monthly WHERE account_external_id = 'g'")
            .ToListAsync();
        var accounts = await ads.Database
            .SqlQuery<string>($"SELECT account_external_id AS \"Value\" FROM ads.v_ads_campaign_monthly")
            .ToListAsync();

        costs.Should().Equal(100m);
        accounts.Should().NotContain("agency");
    }

    [Fact]
    public async Task Blended_monthly_is_net_revenue_without_cancelled_over_czk_campaign_cost()
    {
        var (ads, shoptet) = await SeedAsync();
        await using var _ = ads;
        await using var __ = shoptet;
        await RunScriptAsync(ads, AdsScript);
        await RunScriptAsync(ads, AdsScript); // re-runnable

        var row = await ads.Database.SqlQuery<BlendedRow>($@"
            SELECT ad_cost_czk AS ""AdCost"", google_ads_cost_czk AS ""Google"", sklik_cost_czk AS ""Sklik"",
                   has_non_czk_ad_cost AS ""NonCzk"", orders::int AS ""Orders"",
                   revenue_without_vat_czk AS ""Revenue"", eshop_revenue_without_vat_czk AS ""Eshop"",
                   blended_roas AS ""Roas"", pno_pct AS ""Pno""
            FROM ads.v_ads_blended_monthly WHERE month = DATE '2026-09-01'").SingleAsync();

        row.AdCost.Should().Be(150m);
        row.Google.Should().Be(100m);
        row.Sklik.Should().Be(50m);
        row.NonCzk.Should().BeTrue();
        row.Orders.Should().Be(3);
        row.Revenue.Should().Be(2300m);
        row.Eshop.Should().Be(2000m);
        row.Roas.Should().Be(15.3333m);
        row.Pno.Should().Be(6.52m);
    }

    [Fact]
    public async Task Metabase_can_read_the_granted_views_only()
    {
        var (ads, shoptet) = await SeedAsync();
        await using var _ = ads;
        await using var __ = shoptet;
        await RunScriptAsync(ads, AdsScript);

        async Task<bool> CanSelect(string relation) => await ads.Database
            .SqlQuery<bool>($"SELECT has_table_privilege('metabase_ro', {relation}, 'SELECT') AS \"Value\"").SingleAsync();

        (await CanSelect("ads.v_ads_campaign_monthly")).Should().BeTrue();
        (await CanSelect("ads.v_ads_blended_monthly")).Should().BeTrue();
        (await CanSelect("ads.v_ads_change_events")).Should().BeTrue();
        (await CanSelect("ads.v_ads_blended_daily")).Should().BeFalse();
        (await CanSelect("ads.ad_daily_facts")).Should().BeFalse();
        (await CanSelect("ads.ad_change_events")).Should().BeFalse();
    }

    [Fact]
    public async Task Rerunning_the_shoptet_script_does_not_drop_the_ads_views()
    {
        // shoptet_raw_views.sql drops order_fact with CASCADE; the ads views must not depend on it.
        var (ads, shoptet) = await SeedAsync();
        await using var _ = ads;
        await using var __ = shoptet;
        await RunScriptAsync(shoptet, ShoptetScript);
        await RunScriptAsync(ads, AdsScript);

        await RunScriptAsync(shoptet, ShoptetScript);

        var exists = await ads.Database
            .SqlQuery<bool>($"SELECT to_regclass('ads.v_ads_blended_monthly') IS NOT NULL AS \"Value\"").SingleAsync();
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task Without_shoptet_raw_the_script_succeeds_and_skips_blended_views()
    {
        var connectionString = await AdsPostgres.CreateMigratedAsync(_fixture, "ads_views_noshop", withShoptet: false);
        await using var ads = new AdsDbContext(AdsPostgres.Options(connectionString));

        await RunScriptAsync(ads, AdsScript);

        var blended = await ads.Database
            .SqlQuery<bool>($"SELECT to_regclass('ads.v_ads_blended_daily') IS NULL AS \"Value\"").SingleAsync();
        var campaign = await ads.Database
            .SqlQuery<bool>($"SELECT to_regclass('ads.v_ads_campaign_monthly') IS NOT NULL AS \"Value\"").SingleAsync();
        blended.Should().BeTrue();
        campaign.Should().BeTrue();
    }

    private sealed class BlendedRow
    {
        public decimal AdCost { get; set; }
        public decimal Google { get; set; }
        public decimal Sklik { get; set; }
        public bool NonCzk { get; set; }
        public int Orders { get; set; }
        public decimal Revenue { get; set; }
        public decimal Eshop { get; set; }
        public decimal? Roas { get; set; }
        public decimal? Pno { get; set; }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false && dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "Category=Integration&FullyQualifiedName~Features.MarketingAds.Integration.AdsReadViewsIntegrationTests"`
Expected: FAIL — `42P01: relation "ads.v_ads_campaign_monthly" does not exist` (the script is still the placeholder).

- [ ] **Step 3: Write the SQL**

Replace `backend/src/Anela.Heblo.Persistence.Ads/Sql/ads_read_views.sql` with:

```sql
-- =============================================================================
-- ads — Metabase read views and grants (marketing agents platform, PR C2)
-- =============================================================================
-- Idempotent: safe to run repeatedly, in any environment. Run after the AdsDbContext
-- migration (and, for the blended views, after the shoptet_raw migration):
--
--   psql "$CONNECTION_STRING" -v ON_ERROR_STOP=1 \
--     -f backend/src/Anela.Heblo.Persistence.Ads/Sql/ads_read_views.sql
--
-- Design notes the SQL depends on:
--  * ad_daily_facts holds every level a platform reports. Summing across levels double-counts,
--    so every view reads exactly ONE level: Campaign.
--  * Cost is net of VAT in the ad account's currency. Blended views add up CZK accounts only
--    and flag a day that also had non-CZK spend (has_non_czk_ad_cost) rather than converting
--    at a guessed rate.
--  * Revenue reads shoptet_raw."order" — the base TABLE, not shoptet_raw.order_fact.
--    shoptet_raw_views.sql drops order_fact with CASCADE on every run, which would silently
--    drop any view built on it. The rules below are copied from order_fact; keep them in step:
--      cancelled  = status_id = -4
--      CZK amount = price / exchange_rate (order currency per CZK); a missing rate counts as 1
--      e-shop     = NOT cash_desk_order
--  * Only month-grain views and the (small, PII-free) change log are granted to metabase_ro.
--    v_ads_blended_daily serves Heblo's MCP read handler and stays ungranted.
-- =============================================================================

DROP VIEW IF EXISTS ads.v_ads_blended_monthly;
DROP VIEW IF EXISTS ads.v_ads_blended_daily;
DROP VIEW IF EXISTS ads.v_ads_change_events;
DROP VIEW IF EXISTS ads.v_ads_campaign_monthly;

-- -----------------------------------------------------------------------------
-- Campaign performance per month, platform-reported conversions.
-- -----------------------------------------------------------------------------
CREATE VIEW ads.v_ads_campaign_monthly AS
SELECT
    date_trunc('month', f.date::timestamp)::date                       AS month,
    a.platform,
    a.external_id                                                      AS account_external_id,
    a.name                                                             AS account_name,
    e.external_id                                                      AS campaign_external_id,
    e.name                                                             AS campaign_name,
    e.status                                                           AS campaign_status,
    f.currency,
    count(DISTINCT f.date)                                             AS days_with_data,
    sum(f.impressions)                                                 AS impressions,
    sum(f.clicks)                                                      AS clicks,
    sum(f.cost)                                                        AS cost,
    sum(f.conversions)                                                 AS conversions,
    sum(f.conversion_value)                                            AS conversion_value,
    CASE WHEN sum(f.impressions) > 0
         THEN round(sum(f.clicks)::numeric * 100 / sum(f.impressions), 3) END AS ctr_pct,
    CASE WHEN sum(f.clicks) > 0
         THEN round(sum(f.cost) / sum(f.clicks), 2) END                AS avg_cpc,
    CASE WHEN sum(f.cost) > 0
         THEN round(sum(f.conversion_value) / sum(f.cost), 4) END      AS platform_roas
FROM ads.ad_daily_facts f
JOIN ads.ad_entities e ON e.id = f.entity_id AND e.level = 'Campaign'
JOIN ads.ad_accounts a ON a.id = e.account_id
WHERE a.is_managed
GROUP BY 1, a.platform, a.external_id, a.name, e.external_id, e.name, e.status, f.currency;

COMMENT ON VIEW ads.v_ads_campaign_monthly IS
'Campaign-level ad performance per month as the platforms report it. Conversions are the platform''s own attribution and over-count; compare with v_ads_blended_monthly.';

-- -----------------------------------------------------------------------------
-- Every change made to a managed or unmanaged ad account, Heblo or out-of-band.
-- -----------------------------------------------------------------------------
CREATE VIEW ads.v_ads_change_events AS
SELECT
    c.id,
    c.occurred_at,
    a.platform,
    a.external_id                                   AS account_external_id,
    a.name                                          AS account_name,
    e.level                                         AS entity_level,
    COALESCE(e.external_id, c.entity_external_ref)  AS entity_external_id,
    e.name                                          AS entity_name,
    c.change_type,
    c.actor_kind,
    c.actor,
    c.source,
    c.origin,
    c.matched_execution_id,
    c.old_value,
    c.new_value
FROM ads.ad_change_events c
JOIN ads.ad_accounts a ON a.id = c.account_id
LEFT JOIN ads.ad_entities e ON e.id = c.entity_id;

COMMENT ON VIEW ads.v_ads_change_events IS
'Ad account change history. origin = OutOfBand means the change did not come through an executed Heblo proposal (agency, platform UI, auto-applied recommendation).';

-- -----------------------------------------------------------------------------
-- Blended reality check: real revenue / total ad spend.
-- -----------------------------------------------------------------------------
DO $blended$
BEGIN
    IF to_regclass('shoptet_raw."order"') IS NULL THEN
        RAISE NOTICE 'ads: shoptet_raw."order" does not exist here; skipping the blended views.';
        RETURN;
    END IF;

    EXECUTE $view$
    CREATE VIEW ads.v_ads_blended_daily AS
    WITH ad_cost AS (
        SELECT
            f.date,
            sum(f.cost) FILTER (WHERE f.currency = 'CZK')                              AS ad_cost_czk,
            sum(f.cost) FILTER (WHERE f.currency = 'CZK' AND a.platform = 'GoogleAds') AS google_ads_cost_czk,
            sum(f.cost) FILTER (WHERE f.currency = 'CZK' AND a.platform = 'MetaAds')   AS meta_ads_cost_czk,
            sum(f.cost) FILTER (WHERE f.currency = 'CZK' AND a.platform = 'Sklik')     AS sklik_cost_czk,
            bool_or(f.currency <> 'CZK')                                               AS has_non_czk_ad_cost
        FROM ads.ad_daily_facts f
        JOIN ads.ad_entities e ON e.id = f.entity_id AND e.level = 'Campaign'
        JOIN ads.ad_accounts a ON a.id = e.account_id
        WHERE a.is_managed
        GROUP BY f.date
    ),
    revenue AS (
        SELECT
            o.order_date                                                                AS date,
            count(*)                                                                    AS orders,
            sum(o.price_without_vat / NULLIF(coalesce(o.exchange_rate, 1), 0))          AS revenue_without_vat_czk,
            sum(o.price_without_vat / NULLIF(coalesce(o.exchange_rate, 1), 0))
                FILTER (WHERE NOT o.cash_desk_order)                                    AS eshop_revenue_without_vat_czk
        FROM shoptet_raw."order" o
        WHERE o.status_id <> -4
        GROUP BY o.order_date
    )
    SELECT
        coalesce(c.date, r.date)                        AS date,
        coalesce(c.ad_cost_czk, 0)                      AS ad_cost_czk,
        coalesce(c.google_ads_cost_czk, 0)              AS google_ads_cost_czk,
        coalesce(c.meta_ads_cost_czk, 0)                AS meta_ads_cost_czk,
        coalesce(c.sklik_cost_czk, 0)                   AS sklik_cost_czk,
        coalesce(c.has_non_czk_ad_cost, false)          AS has_non_czk_ad_cost,
        coalesce(r.orders, 0)                           AS orders,
        coalesce(r.revenue_without_vat_czk, 0)          AS revenue_without_vat_czk,
        coalesce(r.eshop_revenue_without_vat_czk, 0)    AS eshop_revenue_without_vat_czk
    FROM ad_cost c
    FULL OUTER JOIN revenue r ON r.date = c.date
    $view$;

    EXECUTE $view$
    CREATE VIEW ads.v_ads_blended_monthly AS
    SELECT
        date_trunc('month', date::timestamp)::date      AS month,
        count(*)                                        AS days_with_data,
        sum(ad_cost_czk)                                AS ad_cost_czk,
        sum(google_ads_cost_czk)                        AS google_ads_cost_czk,
        sum(meta_ads_cost_czk)                          AS meta_ads_cost_czk,
        sum(sklik_cost_czk)                             AS sklik_cost_czk,
        bool_or(has_non_czk_ad_cost)                    AS has_non_czk_ad_cost,
        sum(orders)                                     AS orders,
        sum(revenue_without_vat_czk)                    AS revenue_without_vat_czk,
        sum(eshop_revenue_without_vat_czk)              AS eshop_revenue_without_vat_czk,
        CASE WHEN sum(ad_cost_czk) > 0
             THEN round(sum(revenue_without_vat_czk) / sum(ad_cost_czk), 4) END      AS blended_roas,
        CASE WHEN sum(revenue_without_vat_czk) > 0
             THEN round(sum(ad_cost_czk) * 100 / sum(revenue_without_vat_czk), 2) END AS pno_pct
    FROM ads.v_ads_blended_daily
    GROUP BY 1
    $view$;

    EXECUTE $c$COMMENT ON VIEW ads.v_ads_blended_monthly IS
'Blended reality check: Shoptet revenue without VAT (non-cancelled, CZK) / total campaign-level ad cost of managed CZK accounts. has_non_czk_ad_cost = some spend was in another currency and is NOT included.'$c$;
END
$blended$;

-- =============================================================================
-- Grants — views only, never the raw tables (docs/architecture/metabase.md)
-- =============================================================================
DO $grants$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'metabase_ro') THEN
        EXECUTE 'GRANT USAGE ON SCHEMA ads TO metabase_ro';
        -- Revoke first: ALL TABLES covers views too, so this clears any earlier blanket grant.
        EXECUTE 'REVOKE ALL ON ALL TABLES IN SCHEMA ads FROM metabase_ro';
        EXECUTE 'GRANT SELECT ON ads.v_ads_campaign_monthly TO metabase_ro';
        EXECUTE 'GRANT SELECT ON ads.v_ads_change_events    TO metabase_ro';
        IF to_regclass('ads.v_ads_blended_monthly') IS NOT NULL THEN
            EXECUTE 'GRANT SELECT ON ads.v_ads_blended_monthly TO metabase_ro';
        END IF;
        RAISE NOTICE 'ads: granted metabase_ro SELECT on the v_ads_* Metabase views.';
    ELSE
        RAISE NOTICE 'ads: role metabase_ro does not exist here; skipping grants.';
    END IF;
END
$grants$;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false && dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "Category=Integration&FullyQualifiedName~Features.MarketingAds.Integration.AdsReadViewsIntegrationTests"`
Expected: PASS, 5 tests. (If `ExecuteSqlRawAsync` chokes on the script, check for `{`/`}` characters — there must be none, EF treats them as format placeholders.)

- [ ] **Step 5: Document in `docs/architecture/metabase.md`**

1. If C1 did not already add it, in the table under *Reporting schemas in `Heblo_V3`* add the row:

```markdown
| `ads` | Google Ads / Meta / Sklik campaign data and change history (marketing agents platform) | `AdsDbContext` |
```

2. After the `### flexi_raw` subsection (before `## Payroll (#35)`), add:

```markdown
### `ads`

Granted to `metabase_ro` (defined in
[`backend/src/Anela.Heblo.Persistence.Ads/Sql/ads_read_views.sql`](../../backend/src/Anela.Heblo.Persistence.Ads/Sql/ads_read_views.sql),
idempotent, applied by hand like the other schemas):

| View | Granularity | Notes |
|---|---|---|
| `v_ads_campaign_monthly` | month × campaign | platform-reported metrics, campaign level only, managed accounts only |
| `v_ads_blended_monthly` | month | Shoptet revenue without VAT (non-cancelled) ÷ CZK campaign ad cost, all platforms; created only where `shoptet_raw` exists |
| `v_ads_change_events` | one row per change | row grain, deliberately: a few rows per day, no customer PII (`actor` can be a staff/agency e-mail) |

Not granted: every `ads` table and the helper view `v_ads_blended_daily` (read by Heblo's MCP
read handler). The blended views read `shoptet_raw."order"` rather than `order_fact`, because
`shoptet_raw_views.sql` drops `order_fact` with `CASCADE` on every run. Run order on a new
environment: `AdsDbContext` migration → `shoptet_raw_views.sql` (if not yet) → `ads_read_views.sql`:

```bash
psql "<connection>" -v ON_ERROR_STOP=1 -f backend/src/Anela.Heblo.Persistence.Ads/Sql/ads_read_views.sql
```
```

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence.Ads/Sql/ads_read_views.sql docs/architecture/metabase.md backend/test/Anela.Heblo.Tests/Features/MarketingAds/Integration
git commit -m "feat: add ads metabase views with blended reality check

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 12: Read repository and DTOs for the agents' data

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingAds/Contracts/AdAccountDto.cs`, `AdPerformanceRowDto.cs`, `AdSearchTermRowDto.cs`, `AdBlendedPeriodDto.cs`, `BlendedGranularity.cs`, `AdChangeEventDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingAds/Reporting/IAdsReadRepository.cs`, `AdReadQueries.cs`, `AdBlendedDayRow.cs`, `AdMetrics.cs`, `AdsReadRepository.cs`, `UnavailableAdsReadRepository.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/MarketingAds/MarketingAdsModule.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/Reporting/AdsReadRepositoryTests.cs`, `Integration/AdsReadRepositoryBlendedIntegrationTests.cs`

**Interfaces:**
- Consumes: C1 entities, `AdsDbValues` (Task 1), the sync services (to arrange data), `v_ads_blended_daily` (Task 11), `AdsPostgres` (Task 10).
- Produces (C4's MCP tools consume these through the handlers of Task 13):
  - DTO classes (all `public class`, settable properties): `AdAccountDto { AdPlatform Platform; string ExternalId; string Name; string Currency; string TimeZone; bool IsManaged }`; `AdPerformanceRowDto { string EntityExternalId; string Name; AdEntityStatus Status; string? ParentExternalId; string? ParentName; long Impressions; long Clicks; decimal Cost; decimal Conversions; decimal ConversionValue; decimal? CtrPercent; decimal? AverageCpc; decimal? Roas }`; `AdSearchTermRowDto { string SearchTerm; KeywordMatchType? MatchType; string AdGroupExternalId; string AdGroupName; string? CampaignExternalId; string? CampaignName; long Impressions; long Clicks; decimal Cost; decimal Conversions; decimal ConversionValue }`; `AdBlendedPeriodDto { DateOnly PeriodStart; bool IsPartial; decimal AdCostCzk; decimal GoogleAdsCostCzk; decimal MetaAdsCostCzk; decimal SklikCostCzk; bool HasNonCzkAdCost; int Orders; decimal RevenueWithoutVatCzk; decimal EshopRevenueWithoutVatCzk; decimal? BlendedRoas; decimal? PnoPercent }`; `enum BlendedGranularity { Daily = 1, Monthly = 2 }`; `AdChangeEventDto { long Id; AdPlatform Platform; string AccountExternalId; DateTimeOffset OccurredAt; string? Actor; AdChangeActorKind ActorKind; AdEntityLevel? EntityLevel; string? EntityExternalId; string? EntityName; string ChangeType; string? OldValueJson; string? NewValueJson; AdChangeSource Source; AdChangeOrigin Origin; string? MatchedExecutionId }`.
  - Query records: `AdPerformanceQuery(AdPlatform Platform, string AccountExternalId, AdEntityLevel Level, DateOnly From, DateOnly To, string? ParentExternalId, int PageNumber, int PageSize)`; `AdPerformancePage(string Currency, int TotalCount, IReadOnlyList<AdPerformanceRowDto> Rows)`; `AdSearchTermQuery(AdPlatform Platform, string AccountExternalId, string? CampaignExternalId, string? AdGroupExternalId, DateOnly From, DateOnly To, decimal? MinCost, int Top)`; `AdChangeEventQuery(AdPlatform? Platform, string? AccountExternalId, AdChangeOrigin? Origin, AdChangeActorKind? ActorKind, DateTimeOffset From, DateTimeOffset To, int PageNumber, int PageSize)`; `AdChangeEventPage(int TotalCount, IReadOnlyList<AdChangeEventDto> Rows)`.
  - `class AdBlendedDayRow { DateOnly Date; decimal AdCostCzk; decimal GoogleAdsCostCzk; decimal MetaAdsCostCzk; decimal SklikCostCzk; bool HasNonCzkAdCost; int Orders; decimal RevenueWithoutVatCzk; decimal EshopRevenueWithoutVatCzk }`.
  - `AdMetrics.CtrPercent(long impressions, long clicks)`, `AverageCpc(decimal cost, long clicks)`, `Roas(decimal value, decimal cost)`, `PnoPercent(decimal cost, decimal revenue)` — all `decimal?`, null when the denominator is 0.
  - `interface IAdsReadRepository { bool IsAvailable { get; } Task<IReadOnlyList<AdAccountDto>> ListAccountsAsync(AdPlatform? platform, bool includeUnmanaged, CancellationToken ct); Task<AdPerformancePage?> GetPerformanceAsync(AdPerformanceQuery query, CancellationToken ct); Task<IReadOnlyList<AdSearchTermRowDto>?> GetSearchTermsAsync(AdSearchTermQuery query, CancellationToken ct); Task<IReadOnlyList<AdBlendedDayRow>?> GetBlendedDailyAsync(DateOnly from, DateOnly to, CancellationToken ct); Task<AdChangeEventPage> GetChangeEventsAsync(AdChangeEventQuery query, CancellationToken ct); }` — `null` from performance/search terms = unknown account; `null` from blended = view missing (`42P01`).

- [ ] **Step 1: Write the failing tests**

`Reporting/AdsReadRepositoryTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Reporting;
using Anela.Heblo.Tests.Features.MarketingAds.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Tests.Features.MarketingAds.Reporting;

public class AdsReadRepositoryTests
{
    private static readonly DateOnly Day = AdsTestData.Yesterday;

    /// <summary>Two campaigns (c-1, c-2), ad group g-1 under c-1, facts at campaign and ad-group level over two days.</summary>
    private static async Task<AdsSyncHarness> SeedAsync()
    {
        var harness = new AdsSyncHarness();
        var source = new ScriptedAdReadSource(AdPlatform.GoogleAds, searchTerms: true, changeLog: false) { Accounts = { AdsTestData.Account() } };
        source.Entities[AdsTestData.AccountId] = new List<AdEntitySnapshot>
        {
            AdsTestData.Campaign("c-1"), AdsTestData.Campaign("c-2"), AdsTestData.AdGroup("g-1", "c-1"), AdsTestData.AdGroup("g-2", "c-2"),
        };
        source.Facts[AdsTestData.AccountId] = new List<AdDailyFactRow>
        {
            AdsTestData.Fact(AdEntityLevel.Campaign, "c-1", Day, 100m, clicks: 10, impressions: 1000, value: 400m),
            AdsTestData.Fact(AdEntityLevel.Campaign, "c-1", Day.AddDays(-1), 50m, clicks: 5, impressions: 500, value: 100m),
            AdsTestData.Fact(AdEntityLevel.Campaign, "c-2", Day, 300m),
            AdsTestData.Fact(AdEntityLevel.AdGroup, "g-1", Day, 100m),
        };
        source.SearchTerms[AdsTestData.AccountId] = new List<AdSearchTermRow>
        {
            AdsTestData.Term("g-1", Day, "kosmetika", 10m), AdsTestData.Term("g-1", Day.AddDays(-1), "kosmetika", 5m),
            AdsTestData.Term("g-1", Day, "zdarma", 1m), AdsTestData.Term("g-2", Day, "krém", 7m),
        };
        await harness.Daily(source).RunAsync(default);
        return harness;
    }

    private static AdsReadRepository Repo(AdsSyncHarness harness) => new(harness.Verify());

    private static AdPerformanceQuery Perf(AdEntityLevel level, string? parent = null, int page = 1, int size = 100) =>
        new(AdPlatform.GoogleAds, AdsTestData.AccountId, level, Day.AddDays(-1), Day, parent, page, size);

    [Fact]
    public async Task Performance_sums_only_the_requested_level_over_the_range()
    {
        var page = await Repo(await SeedAsync()).GetPerformanceAsync(Perf(AdEntityLevel.Campaign), default);

        page!.Currency.Should().Be("CZK");
        page.TotalCount.Should().Be(2);
        page.Rows.Select(r => r.EntityExternalId).Should().Equal("c-2", "c-1"); // by cost, descending
        var c1 = page.Rows.Single(r => r.EntityExternalId == "c-1");
        c1.Cost.Should().Be(150m);
        c1.Clicks.Should().Be(15);
        c1.CtrPercent.Should().Be(1.000m);
        c1.AverageCpc.Should().Be(10.00m);
        c1.Roas.Should().Be(3.3333m);
    }

    [Fact]
    public async Task Performance_filters_by_parent_and_pages()
    {
        var repo = Repo(await SeedAsync());

        var children = await repo.GetPerformanceAsync(Perf(AdEntityLevel.AdGroup, parent: "c-1"), default);
        var secondPage = await repo.GetPerformanceAsync(Perf(AdEntityLevel.Campaign, page: 2, size: 1), default);

        children!.Rows.Should().ContainSingle().Which.ParentName.Should().Be("Campaign c-1");
        secondPage!.Rows.Should().ContainSingle().Which.EntityExternalId.Should().Be("c-1");
        secondPage.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task Unknown_account_returns_null()
    {
        var page = await Repo(await SeedAsync()).GetPerformanceAsync(Perf(AdEntityLevel.Campaign) with { AccountExternalId = "nope" }, default);

        page.Should().BeNull();
    }

    [Fact]
    public async Task Search_terms_aggregate_days_filter_by_campaign_and_respect_top_and_min_cost()
    {
        var repo = Repo(await SeedAsync());
        var query = new AdSearchTermQuery(AdPlatform.GoogleAds, AdsTestData.AccountId, "c-1", null, Day.AddDays(-1), Day, null, 10);

        var all = await repo.GetSearchTermsAsync(query, default);
        var top = await repo.GetSearchTermsAsync(query with { Top = 1 }, default);
        var expensive = await repo.GetSearchTermsAsync(query with { MinCost = 2m }, default);

        all!.Select(t => t.SearchTerm).Should().Equal("kosmetika", "zdarma");
        all[0].Cost.Should().Be(15m);
        all[0].CampaignExternalId.Should().Be("c-1");
        all[0].AdGroupName.Should().Be("Ad group g-1");
        top!.Should().ContainSingle();
        expensive!.Select(t => t.SearchTerm).Should().Equal("kosmetika");
    }

    [Fact]
    public async Task Accounts_are_managed_only_unless_asked()
    {
        var harness = await SeedAsync();
        await using (var edit = harness.Verify())
        {
            (await edit.Accounts.SingleAsync()).IsManaged = false;
            await edit.SaveChangesAsync();
        }

        (await Repo(harness).ListAccountsAsync(null, includeUnmanaged: false, default)).Should().BeEmpty();
        (await Repo(harness).ListAccountsAsync(AdPlatform.GoogleAds, includeUnmanaged: true, default))
            .Should().ContainSingle().Which.Platform.Should().Be(AdPlatform.GoogleAds);
    }

    [Fact]
    public async Task Change_events_filter_by_origin_newest_first_with_entity_names()
    {
        // Arrange — two status changes an hour apart via the snapshot-diff path
        var harness = await SeedAsync();
        var source = new ScriptedAdReadSource(AdPlatform.GoogleAds, changeLog: false) { Accounts = { AdsTestData.Account() } };
        source.Entities[AdsTestData.AccountId] = new List<AdEntitySnapshot> { AdsTestData.Campaign("c-1", AdEntityStatus.Paused) };
        harness.Time.Advance(TimeSpan.FromHours(1));
        await harness.Change(source).RunAsync(default);
        source.Entities[AdsTestData.AccountId][0] = AdsTestData.Campaign("c-1", AdEntityStatus.Enabled);
        harness.Time.Advance(TimeSpan.FromHours(1));
        await harness.Change(source).RunAsync(default);

        // Act
        var page = await Repo(harness).GetChangeEventsAsync(new AdChangeEventQuery(AdPlatform.GoogleAds, null, AdChangeOrigin.OutOfBand, null,
            AdsTestData.Now.AddDays(-1), AdsTestData.Now.AddDays(1), 1, 50), default);
        var heblo = await Repo(harness).GetChangeEventsAsync(new AdChangeEventQuery(null, null, AdChangeOrigin.Heblo, null,
            AdsTestData.Now.AddDays(-1), AdsTestData.Now.AddDays(1), 1, 50), default);

        // Assert
        page.TotalCount.Should().Be(2);
        page.Rows[0].OccurredAt.Should().BeAfter(page.Rows[1].OccurredAt);
        page.Rows[0].EntityName.Should().Be("Campaign c-1");
        page.Rows[0].Source.Should().Be(AdChangeSource.SnapshotDiff);
        heblo.TotalCount.Should().Be(0);
    }
}
```

`Integration/AdsReadRepositoryBlendedIntegrationTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Reporting;
using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Tests.Features.MarketingAds.Integration;

[Collection("PostgresIntegration")]
[Trait("Category", "Integration")]
public class AdsReadRepositoryBlendedIntegrationTests
{
    private static readonly string AdsScript = Path.Combine(AppContext.BaseDirectory, "MarketingAds", "Sql", "ads_read_views.sql");
    private readonly PostgresSharedContainerFixture _fixture;

    public AdsReadRepositoryBlendedIntegrationTests(PostgresSharedContainerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Reads_the_blended_daily_view_with_date_parameters()
    {
        // Arrange
        var connectionString = await AdsPostgres.CreateMigratedAsync(_fixture, "ads_blend_repo", withShoptet: true);
        await using var db = new AdsDbContext(AdsPostgres.Options(connectionString));
        await db.Database.ExecuteSqlRawAsync(await File.ReadAllTextAsync(AdsScript));
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO shoptet_raw.\"order\" (code, creation_time, order_date, status_id, is_paid, cash_desk_order, price_without_vat, exchange_rate, " +
            "shipping_price_with_vat, shipping_price_without_vat, billing_price_with_vat, billing_price_without_vat, discount_with_vat, discount_without_vat, " +
            "product_price_with_vat, product_price_without_vat, product_units, vat_payer, raw_payload, synced_at) VALUES " +
            "('A1', now(), DATE '2026-09-01', 2, true, false, 1000, 1, 0,0,0,0,0,0,0,0,0, true, '{}', now())," +
            "('A2', now(), DATE '2026-09-05', 2, true, false, 500, 1, 0,0,0,0,0,0,0,0,0, true, '{}', now())");

        // Act
        var rows = await new AdsReadRepository(db).GetBlendedDailyAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3), default);

        // Assert
        rows.Should().NotBeNull();
        rows!.Should().ContainSingle();
        rows[0].Date.Should().Be(new DateOnly(2026, 9, 1));
        rows[0].RevenueWithoutVatCzk.Should().Be(1000m);
        rows[0].Orders.Should().Be(1);
    }

    [Fact]
    public async Task Missing_view_returns_null_instead_of_throwing()
    {
        var connectionString = await AdsPostgres.CreateMigratedAsync(_fixture, "ads_blend_missing", withShoptet: false);
        await using var db = new AdsDbContext(AdsPostgres.Options(connectionString));

        var rows = await new AdsReadRepository(db).GetBlendedDailyAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3), default);

        rows.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false`
Expected: FAIL — `CS0246: The type or namespace name 'AdsReadRepository' could not be found`.

- [ ] **Step 3: Implement DTOs** (one file each in `Contracts/`)

```csharp
// Contracts/AdAccountDto.cs
namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

public class AdAccountDto
{
    public AdPlatform Platform { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string TimeZone { get; set; } = string.Empty;
    public bool IsManaged { get; set; }
}
```

```csharp
// Contracts/AdPerformanceRowDto.cs
namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>One entity's platform-reported metrics summed over the requested date range. Cost is net of VAT, account currency.</summary>
public class AdPerformanceRowDto
{
    public string EntityExternalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AdEntityStatus Status { get; set; }
    public string? ParentExternalId { get; set; }
    public string? ParentName { get; set; }
    public long Impressions { get; set; }
    public long Clicks { get; set; }
    public decimal Cost { get; set; }
    public decimal Conversions { get; set; }
    public decimal ConversionValue { get; set; }
    public decimal? CtrPercent { get; set; }
    public decimal? AverageCpc { get; set; }
    public decimal? Roas { get; set; }
}
```

```csharp
// Contracts/AdSearchTermRowDto.cs
namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

public class AdSearchTermRowDto
{
    public string SearchTerm { get; set; } = string.Empty;
    public KeywordMatchType? MatchType { get; set; }
    public string AdGroupExternalId { get; set; } = string.Empty;
    public string AdGroupName { get; set; } = string.Empty;
    public string? CampaignExternalId { get; set; }
    public string? CampaignName { get; set; }
    public long Impressions { get; set; }
    public long Clicks { get; set; }
    public decimal Cost { get; set; }
    public decimal Conversions { get; set; }
    public decimal ConversionValue { get; set; }
}
```

```csharp
// Contracts/BlendedGranularity.cs
namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

public enum BlendedGranularity
{
    Daily = 1,
    Monthly = 2,
}
```

```csharp
// Contracts/AdBlendedPeriodDto.cs
namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>Real revenue (Shoptet, without VAT, non-cancelled, CZK) against total CZK ad cost at campaign level.</summary>
public class AdBlendedPeriodDto
{
    public DateOnly PeriodStart { get; set; }
    /// <summary>The period extends outside the requested range (a month cut by From/To).</summary>
    public bool IsPartial { get; set; }
    public decimal AdCostCzk { get; set; }
    public decimal GoogleAdsCostCzk { get; set; }
    public decimal MetaAdsCostCzk { get; set; }
    public decimal SklikCostCzk { get; set; }
    /// <summary>Some ad spend was in a non-CZK account and is NOT included in AdCostCzk.</summary>
    public bool HasNonCzkAdCost { get; set; }
    public int Orders { get; set; }
    public decimal RevenueWithoutVatCzk { get; set; }
    public decimal EshopRevenueWithoutVatCzk { get; set; }
    public decimal? BlendedRoas { get; set; }
    public decimal? PnoPercent { get; set; }
}
```

```csharp
// Contracts/AdChangeEventDto.cs
namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

public class AdChangeEventDto
{
    public long Id { get; set; }
    public AdPlatform Platform { get; set; }
    public string AccountExternalId { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public string? Actor { get; set; }
    public AdChangeActorKind ActorKind { get; set; }
    public AdEntityLevel? EntityLevel { get; set; }
    public string? EntityExternalId { get; set; }
    public string? EntityName { get; set; }
    public string ChangeType { get; set; } = string.Empty;
    public string? OldValueJson { get; set; }
    public string? NewValueJson { get; set; }
    public AdChangeSource Source { get; set; }
    public AdChangeOrigin Origin { get; set; }
    public string? MatchedExecutionId { get; set; }
}
```

- [ ] **Step 4: Implement the reporting types**

`Reporting/AdReadQueries.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Application.Features.MarketingAds.Reporting;

public sealed record AdPerformanceQuery(AdPlatform Platform, string AccountExternalId, AdEntityLevel Level,
    DateOnly From, DateOnly To, string? ParentExternalId, int PageNumber, int PageSize);

public sealed record AdPerformancePage(string Currency, int TotalCount, IReadOnlyList<AdPerformanceRowDto> Rows);

public sealed record AdSearchTermQuery(AdPlatform Platform, string AccountExternalId, string? CampaignExternalId,
    string? AdGroupExternalId, DateOnly From, DateOnly To, decimal? MinCost, int Top);

public sealed record AdChangeEventQuery(AdPlatform? Platform, string? AccountExternalId, AdChangeOrigin? Origin,
    AdChangeActorKind? ActorKind, DateTimeOffset From, DateTimeOffset To, int PageNumber, int PageSize);

public sealed record AdChangeEventPage(int TotalCount, IReadOnlyList<AdChangeEventDto> Rows);
```

`Reporting/AdBlendedDayRow.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Reporting;

/// <summary>Row of ads.v_ads_blended_daily, materialised with Database.SqlQuery (property names = column aliases).</summary>
public class AdBlendedDayRow
{
    public DateOnly Date { get; set; }
    public decimal AdCostCzk { get; set; }
    public decimal GoogleAdsCostCzk { get; set; }
    public decimal MetaAdsCostCzk { get; set; }
    public decimal SklikCostCzk { get; set; }
    public bool HasNonCzkAdCost { get; set; }
    public int Orders { get; set; }
    public decimal RevenueWithoutVatCzk { get; set; }
    public decimal EshopRevenueWithoutVatCzk { get; set; }
}
```

`Reporting/AdMetrics.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Reporting;

/// <summary>Derived ratios; null when the denominator is zero. Rounding matches the Metabase views.</summary>
public static class AdMetrics
{
    public static decimal? CtrPercent(long impressions, long clicks) =>
        impressions > 0 ? Math.Round((decimal)clicks * 100 / impressions, 3) : null;

    public static decimal? AverageCpc(decimal cost, long clicks) =>
        clicks > 0 ? Math.Round(cost / clicks, 2) : null;

    public static decimal? Roas(decimal value, decimal cost) =>
        cost > 0 ? Math.Round(value / cost, 4) : null;

    public static decimal? PnoPercent(decimal cost, decimal revenue) =>
        revenue > 0 ? Math.Round(cost * 100 / revenue, 2) : null;
}
```

`Reporting/IAdsReadRepository.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Application.Features.MarketingAds.Reporting;

public interface IAdsReadRepository
{
    /// <summary>False when AdsDbContext is not configured in this environment.</summary>
    bool IsAvailable { get; }

    Task<IReadOnlyList<AdAccountDto>> ListAccountsAsync(AdPlatform? platform, bool includeUnmanaged, CancellationToken ct);

    /// <summary>Null when the account is unknown.</summary>
    Task<AdPerformancePage?> GetPerformanceAsync(AdPerformanceQuery query, CancellationToken ct);

    /// <summary>Null when the account is unknown.</summary>
    Task<IReadOnlyList<AdSearchTermRowDto>?> GetSearchTermsAsync(AdSearchTermQuery query, CancellationToken ct);

    /// <summary>Null when ads.v_ads_blended_daily does not exist (views script not run, or no shoptet_raw).</summary>
    Task<IReadOnlyList<AdBlendedDayRow>?> GetBlendedDailyAsync(DateOnly from, DateOnly to, CancellationToken ct);

    Task<AdChangeEventPage> GetChangeEventsAsync(AdChangeEventQuery query, CancellationToken ct);
}
```

`Reporting/UnavailableAdsReadRepository.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Application.Features.MarketingAds.Reporting;

/// <summary>Registered when AdsDbContext is not. Handlers check IsAvailable first and never call the methods.</summary>
public sealed class UnavailableAdsReadRepository : IAdsReadRepository
{
    public bool IsAvailable => false;

    public Task<IReadOnlyList<AdAccountDto>> ListAccountsAsync(AdPlatform? platform, bool includeUnmanaged, CancellationToken ct) => throw Unavailable();
    public Task<AdPerformancePage?> GetPerformanceAsync(AdPerformanceQuery query, CancellationToken ct) => throw Unavailable();
    public Task<IReadOnlyList<AdSearchTermRowDto>?> GetSearchTermsAsync(AdSearchTermQuery query, CancellationToken ct) => throw Unavailable();
    public Task<IReadOnlyList<AdBlendedDayRow>?> GetBlendedDailyAsync(DateOnly from, DateOnly to, CancellationToken ct) => throw Unavailable();
    public Task<AdChangeEventPage> GetChangeEventsAsync(AdChangeEventQuery query, CancellationToken ct) => throw Unavailable();

    private static InvalidOperationException Unavailable() =>
        new("The ads database is not configured (AdsDatabase:ConnectionString).");
}
```

`Reporting/AdsReadRepository.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Persistence.Ads;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anela.Heblo.Application.Features.MarketingAds.Reporting;

/// <summary>
/// Read side for agents (ADR-008: operational, not reporting). Every fact query aggregates
/// exactly one entity level — facts are stored at every level and summing levels double-counts.
/// </summary>
public sealed class AdsReadRepository : IAdsReadRepository
{
    private const string UndefinedTable = "42P01";
    private readonly AdsDbContext _db;

    public AdsReadRepository(AdsDbContext db) => _db = db;

    public bool IsAvailable => true;

    public async Task<IReadOnlyList<AdAccountDto>> ListAccountsAsync(AdPlatform? platform, bool includeUnmanaged, CancellationToken ct)
    {
        var query = _db.Accounts.AsNoTracking();
        if (platform is { } p)
        {
            var value = AdsDbValues.ToDb(p);
            query = query.Where(a => a.Platform == value);
        }
        if (!includeUnmanaged)
            query = query.Where(a => a.IsManaged);

        var rows = await query.OrderBy(a => a.Platform).ThenBy(a => a.Name).ToListAsync(ct);
        return rows.Select(a => new AdAccountDto
        {
            Platform = AdsDbValues.FromDb<AdPlatform>(a.Platform),
            ExternalId = a.ExternalId,
            Name = a.Name,
            Currency = a.Currency,
            TimeZone = a.TimeZone,
            IsManaged = a.IsManaged,
        }).ToList();
    }

    public async Task<AdPerformancePage?> GetPerformanceAsync(AdPerformanceQuery query, CancellationToken ct)
    {
        var account = await FindAccountAsync(query.Platform, query.AccountExternalId, ct);
        if (account is null)
            return null;

        var level = AdsDbValues.ToDb(query.Level);
        var entities = _db.Entities.AsNoTracking().Where(e => e.AccountId == account.Value.Id && e.Level == level);
        if (!string.IsNullOrWhiteSpace(query.ParentExternalId))
        {
            var parentIds = _db.Entities.Where(e => e.AccountId == account.Value.Id && e.Level != level && e.ExternalId == query.ParentExternalId)
                .Select(e => (long?)e.Id);
            entities = entities.Where(e => parentIds.Contains(e.ParentId));
        }

        var grouped =
            from fact in _db.DailyFacts.AsNoTracking()
            join entity in entities on fact.EntityId equals entity.Id
            where fact.Date >= query.From && fact.Date <= query.To
            group fact by fact.EntityId into g
            select new
            {
                EntityId = g.Key,
                Impressions = g.Sum(x => x.Impressions),
                Clicks = g.Sum(x => x.Clicks),
                Cost = g.Sum(x => x.Cost),
                Conversions = g.Sum(x => x.Conversions),
                ConversionValue = g.Sum(x => x.ConversionValue),
            };

        var total = await grouped.CountAsync(ct);
        var page = await grouped.OrderByDescending(x => x.Cost).ThenBy(x => x.EntityId)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        var names = await LoadNamesAsync(page.Select(x => x.EntityId), ct);

        var rows = page.Select(x =>
        {
            var entity = names[x.EntityId];
            var parent = entity.ParentId is { } pid && names.TryGetValue(pid, out var p) ? p : null;
            return new AdPerformanceRowDto
            {
                EntityExternalId = entity.ExternalId,
                Name = entity.Name,
                Status = AdsDbValues.FromDb<AdEntityStatus>(entity.Status),
                ParentExternalId = parent?.ExternalId,
                ParentName = parent?.Name,
                Impressions = x.Impressions,
                Clicks = x.Clicks,
                Cost = x.Cost,
                Conversions = x.Conversions,
                ConversionValue = x.ConversionValue,
                CtrPercent = AdMetrics.CtrPercent(x.Impressions, x.Clicks),
                AverageCpc = AdMetrics.AverageCpc(x.Cost, x.Clicks),
                Roas = AdMetrics.Roas(x.ConversionValue, x.Cost),
            };
        }).ToList();

        return new AdPerformancePage(account.Value.Currency, total, rows);
    }

    public async Task<IReadOnlyList<AdSearchTermRowDto>?> GetSearchTermsAsync(AdSearchTermQuery query, CancellationToken ct)
    {
        var account = await FindAccountAsync(query.Platform, query.AccountExternalId, ct);
        if (account is null)
            return null;

        var adGroupLevel = AdsDbValues.ToDb(AdEntityLevel.AdGroup);
        var campaignLevel = AdsDbValues.ToDb(AdEntityLevel.Campaign);
        var adGroups = _db.Entities.AsNoTracking().Where(e => e.AccountId == account.Value.Id && e.Level == adGroupLevel);
        if (!string.IsNullOrWhiteSpace(query.AdGroupExternalId))
            adGroups = adGroups.Where(e => e.ExternalId == query.AdGroupExternalId);
        if (!string.IsNullOrWhiteSpace(query.CampaignExternalId))
        {
            var campaignIds = _db.Entities.Where(e => e.AccountId == account.Value.Id && e.Level == campaignLevel && e.ExternalId == query.CampaignExternalId)
                .Select(e => (long?)e.Id);
            adGroups = adGroups.Where(e => campaignIds.Contains(e.ParentId));
        }

        var grouped =
            from term in _db.SearchTermsDaily.AsNoTracking()
            join adGroup in adGroups on term.AdGroupEntityId equals adGroup.Id
            where term.Date >= query.From && term.Date <= query.To
            group term by new { term.AdGroupEntityId, term.SearchTerm, term.MatchType } into g
            select new
            {
                g.Key.AdGroupEntityId,
                g.Key.SearchTerm,
                g.Key.MatchType,
                Impressions = g.Sum(x => x.Impressions),
                Clicks = g.Sum(x => x.Clicks),
                Cost = g.Sum(x => x.Cost),
                Conversions = g.Sum(x => x.Conversions),
                ConversionValue = g.Sum(x => x.ConversionValue),
            };
        if (query.MinCost is { } minCost)
            grouped = grouped.Where(x => x.Cost >= minCost);

        var rows = await grouped.OrderByDescending(x => x.Cost).ThenBy(x => x.SearchTerm).Take(query.Top).ToListAsync(ct);
        var adGroupNames = await LoadNamesAsync(rows.Select(r => r.AdGroupEntityId), ct);
        var campaignNames = await LoadNamesAsync(adGroupNames.Values.Where(a => a.ParentId is not null).Select(a => a.ParentId!.Value), ct);

        return rows.Select(r =>
        {
            var adGroup = adGroupNames[r.AdGroupEntityId];
            var campaign = adGroup.ParentId is { } pid && campaignNames.TryGetValue(pid, out var c) ? c : null;
            return new AdSearchTermRowDto
            {
                SearchTerm = r.SearchTerm,
                MatchType = AdsDbValues.MatchTypeFromDb(r.MatchType),
                AdGroupExternalId = adGroup.ExternalId,
                AdGroupName = adGroup.Name,
                CampaignExternalId = campaign?.ExternalId,
                CampaignName = campaign?.Name,
                Impressions = r.Impressions,
                Clicks = r.Clicks,
                Cost = r.Cost,
                Conversions = r.Conversions,
                ConversionValue = r.ConversionValue,
            };
        }).ToList();
    }

    public async Task<IReadOnlyList<AdBlendedDayRow>?> GetBlendedDailyAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        try
        {
            return await _db.Database.SqlQuery<AdBlendedDayRow>($@"
                SELECT date AS ""Date"", ad_cost_czk AS ""AdCostCzk"", google_ads_cost_czk AS ""GoogleAdsCostCzk"",
                       meta_ads_cost_czk AS ""MetaAdsCostCzk"", sklik_cost_czk AS ""SklikCostCzk"",
                       has_non_czk_ad_cost AS ""HasNonCzkAdCost"", orders::int AS ""Orders"",
                       revenue_without_vat_czk AS ""RevenueWithoutVatCzk"", eshop_revenue_without_vat_czk AS ""EshopRevenueWithoutVatCzk""
                FROM ads.v_ads_blended_daily
                WHERE date >= {from} AND date <= {to}
                ORDER BY date").ToListAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == UndefinedTable)
        {
            return null;
        }
    }

    public async Task<AdChangeEventPage> GetChangeEventsAsync(AdChangeEventQuery query, CancellationToken ct)
    {
        var from = query.From.ToUniversalTime();
        var to = query.To.ToUniversalTime();
        var rows =
            from change in _db.ChangeEvents.AsNoTracking()
            join account in _db.Accounts.AsNoTracking() on change.AccountId equals account.Id
            where change.OccurredAt >= from && change.OccurredAt <= to
            select new { change, account };

        if (query.Platform is { } platform)
        {
            var value = AdsDbValues.ToDb(platform);
            rows = rows.Where(x => x.account.Platform == value);
        }
        if (!string.IsNullOrWhiteSpace(query.AccountExternalId))
            rows = rows.Where(x => x.account.ExternalId == query.AccountExternalId);
        if (query.Origin is { } origin)
        {
            var value = AdsDbValues.ToDb(origin);
            rows = rows.Where(x => x.change.Origin == value);
        }
        if (query.ActorKind is { } actorKind)
        {
            var value = AdsDbValues.ToDb(actorKind);
            rows = rows.Where(x => x.change.ActorKind == value);
        }

        var total = await rows.CountAsync(ct);
        var page = await rows.OrderByDescending(x => x.change.OccurredAt).ThenByDescending(x => x.change.Id)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        var entities = await LoadNamesAsync(page.Where(x => x.change.EntityId is not null).Select(x => x.change.EntityId!.Value), ct);

        return new AdChangeEventPage(total, page.Select(x =>
        {
            var entity = x.change.EntityId is { } id && entities.TryGetValue(id, out var e) ? e : null;
            return new AdChangeEventDto
            {
                Id = x.change.Id,
                Platform = AdsDbValues.FromDb<AdPlatform>(x.account.Platform),
                AccountExternalId = x.account.ExternalId,
                OccurredAt = x.change.OccurredAt,
                Actor = x.change.Actor,
                ActorKind = AdsDbValues.FromDb<AdChangeActorKind>(x.change.ActorKind),
                EntityLevel = entity is null ? null : AdsDbValues.FromDb<AdEntityLevel>(entity.Level),
                EntityExternalId = entity?.ExternalId ?? x.change.EntityExternalRef,
                EntityName = entity?.Name,
                ChangeType = x.change.ChangeType,
                OldValueJson = x.change.OldValueJson,
                NewValueJson = x.change.NewValueJson,
                Source = AdsDbValues.FromDb<AdChangeSource>(x.change.Source),
                Origin = AdsDbValues.FromDb<AdChangeOrigin>(x.change.Origin),
                MatchedExecutionId = x.change.MatchedExecutionId,
            };
        }).ToList());
    }

    private async Task<(long Id, string Currency)?> FindAccountAsync(AdPlatform platform, string externalId, CancellationToken ct)
    {
        var value = AdsDbValues.ToDb(platform);
        var account = await _db.Accounts.AsNoTracking()
            .Where(a => a.Platform == value && a.ExternalId == externalId)
            .Select(a => new { a.Id, a.Currency })
            .FirstOrDefaultAsync(ct);
        return account is null ? null : (account.Id, account.Currency);
    }

    private sealed record EntityName(long Id, string Level, string ExternalId, string Name, string Status, long? ParentId);

    /// <summary>Loads the given entities and their parents.</summary>
    private async Task<IReadOnlyDictionary<long, EntityName>> LoadNamesAsync(IEnumerable<long> ids, CancellationToken ct)
    {
        var wanted = ids.Distinct().ToList();
        if (wanted.Count == 0)
            return new Dictionary<long, EntityName>();

        var rows = await _db.Entities.AsNoTracking()
            .Where(e => wanted.Contains(e.Id))
            .Select(e => new EntityName(e.Id, e.Level, e.ExternalId, e.Name, e.Status, e.ParentId))
            .ToListAsync(ct);
        var parentIds = rows.Where(r => r.ParentId is not null).Select(r => r.ParentId!.Value).Except(wanted).Distinct().ToList();
        var parents = parentIds.Count == 0 ? new List<EntityName>() : await _db.Entities.AsNoTracking()
            .Where(e => parentIds.Contains(e.Id))
            .Select(e => new EntityName(e.Id, e.Level, e.ExternalId, e.Name, e.Status, e.ParentId))
            .ToListAsync(ct);
        return rows.Concat(parents).ToDictionary(r => r.Id);
    }
}
```

Register in `MarketingAdsModule.AddMarketingAdsModule`, just before the `return`:

```csharp
        services.AddScoped<IAdsReadRepository>(sp => sp.GetService<AdsDbContext>() is { } db
            ? new AdsReadRepository(db)
            : new UnavailableAdsReadRepository());
```

(add `using Anela.Heblo.Application.Features.MarketingAds.Reporting;`).

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false && dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~Features.MarketingAds.Reporting.AdsReadRepositoryTests"`
Expected: PASS, 6 tests.

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "Category=Integration&FullyQualifiedName~Features.MarketingAds.Integration.AdsReadRepositoryBlendedIntegrationTests"`
Expected: PASS, 2 tests.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingAds backend/test/Anela.Heblo.Tests/Features/MarketingAds
git commit -m "feat: add ads read repository for agent-facing queries

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 13: MediatR read handlers (the C4 MCP tool surface's data side)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingAds/Reporting/AdReadValidation.cs`, `Reporting/AdBlendedAggregator.cs`
- Create (Request / Response / Handler each): `UseCases/GetAdAccounts/`, `UseCases/GetAdPerformance/`, `UseCases/GetAdSearchTerms/`, `UseCases/GetBlendedPerformance/`, `UseCases/GetAdChangeHistory/` under `backend/src/Anela.Heblo.Application/Features/MarketingAds/`
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/UseCases/AdReadHandlersTests.cs`, `Reporting/AdBlendedAggregatorTests.cs`

**Interfaces:**
- Consumes: `IAdsReadRepository`, DTOs, query records (Task 12); `BaseResponse`, `ErrorCodes` (`backend/src/Anela.Heblo.Application/Shared/`).
- Produces (C4 calls these with `IMediator.Send`; handlers do **not** check feature permissions — C4's MCP tools gate with `EnsureFeatureAccess`):

| Request (class, `IRequest<…Response>`) | Fields (defaults) | Response (inherits `BaseResponse`) |
|---|---|---|
| `GetAdAccountsRequest` | `AdPlatform? Platform`, `bool IncludeUnmanaged` | `GetAdAccountsResponse { List<AdAccountDto> Accounts }` |
| `GetAdPerformanceRequest` | `AdPlatform Platform`, `string AccountExternalId`, `AdEntityLevel Level`, `DateOnly From`, `DateOnly To`, `string? ParentExternalId`, `int PageNumber = 1`, `int PageSize = 100` | `GetAdPerformanceResponse { AdPlatform Platform; string AccountExternalId; AdEntityLevel Level; DateOnly From; DateOnly To; string Currency; int PageNumber; int PageSize; int TotalCount; List<AdPerformanceRowDto> Rows }` |
| `GetAdSearchTermsRequest` | `AdPlatform Platform`, `string AccountExternalId`, `string? CampaignExternalId`, `string? AdGroupExternalId`, `DateOnly From`, `DateOnly To`, `decimal? MinCost`, `int Top = 200` | `GetAdSearchTermsResponse { DateOnly From; DateOnly To; List<AdSearchTermRowDto> Terms }` |
| `GetBlendedPerformanceRequest` | `DateOnly From`, `DateOnly To`, `BlendedGranularity Granularity = Monthly` | `GetBlendedPerformanceResponse { BlendedGranularity Granularity; DateOnly From; DateOnly To; List<AdBlendedPeriodDto> Periods; AdBlendedPeriodDto Total }` |
| `GetAdChangeHistoryRequest` | `AdPlatform? Platform`, `string? AccountExternalId`, `AdChangeOrigin? Origin`, `AdChangeActorKind? ActorKind`, `DateTimeOffset? From`, `DateTimeOffset? To` (default: last 30 days), `int PageNumber = 1`, `int PageSize = 100` | `GetAdChangeHistoryResponse { DateTimeOffset From; DateTimeOffset To; int PageNumber; int PageSize; int TotalCount; List<AdChangeEventDto> Events }` |

  Errors (existing codes only): repository unavailable → `ConfigurationError` (`Params["Reason"] = "AdsDatabaseNotConfigured"`); blended view missing → `ConfigurationError` (`"BlendedViewMissing"`); missing account id → `RequiredFieldMissing` (`Params["Field"]`); From > To or range too long → `InvalidDateRange` (`Params["MaxDays"]`); bad paging/top → `InvalidValue` (`Params["Field"]`); unknown account → `ResourceNotFound` (`Params["Account"]`). Limits: performance and search terms ≤ 93 days, blended ≤ 731 days, change history ≤ 92 days; page size 1..500; top 1..1000.
  - `AdReadValidation.DateRange(DateOnly from, DateOnly to, int maxDays) : Dictionary<string,string>?`, `AdReadValidation.Positive(string field, int value, int max) : Dictionary<string,string>?`.
  - `AdBlendedAggregator.ToPeriods(IReadOnlyList<AdBlendedDayRow> rows, BlendedGranularity grain, DateOnly from, DateOnly to) : List<AdBlendedPeriodDto>`; `AdBlendedAggregator.Total(IReadOnlyList<AdBlendedDayRow> rows, DateOnly from) : AdBlendedPeriodDto`.

- [ ] **Step 1: Write the failing tests**

`Reporting/AdBlendedAggregatorTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Reporting;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.MarketingAds.Reporting;

public class AdBlendedAggregatorTests
{
    private static AdBlendedDayRow Day(int month, int day, decimal cost, decimal revenue, bool nonCzk = false) => new()
    {
        Date = new DateOnly(2026, month, day), AdCostCzk = cost, GoogleAdsCostCzk = cost, Orders = 1,
        RevenueWithoutVatCzk = revenue, EshopRevenueWithoutVatCzk = revenue, HasNonCzkAdCost = nonCzk,
    };

    [Fact]
    public void Monthly_sums_days_and_recomputes_ratios_from_sums()
    {
        // Arrange — ratios must come from summed cost and revenue, never by averaging daily ratios
        var rows = new[] { Day(9, 1, 100m, 1000m), Day(9, 2, 50m, 2000m, nonCzk: true), Day(10, 1, 10m, 0m) };

        // Act
        var periods = AdBlendedAggregator.ToPeriods(rows, BlendedGranularity.Monthly, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 15));

        // Assert
        periods.Should().HaveCount(2);
        var september = periods[0];
        september.PeriodStart.Should().Be(new DateOnly(2026, 9, 1));
        september.AdCostCzk.Should().Be(150m);
        september.RevenueWithoutVatCzk.Should().Be(3000m);
        september.BlendedRoas.Should().Be(20.0000m);
        september.PnoPercent.Should().Be(5.00m);
        september.HasNonCzkAdCost.Should().BeTrue();
        september.IsPartial.Should().BeFalse();
        periods[1].IsPartial.Should().BeTrue(); // October cut at the 15th
        periods[1].PnoPercent.Should().BeNull(); // no revenue
    }

    [Fact]
    public void Daily_keeps_one_period_per_row()
    {
        var periods = AdBlendedAggregator.ToPeriods(new[] { Day(9, 1, 100m, 1000m) }, BlendedGranularity.Daily, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 1));

        periods.Should().ContainSingle().Which.BlendedRoas.Should().Be(10.0000m);
    }

    [Fact]
    public void Total_spans_all_rows()
    {
        var total = AdBlendedAggregator.Total(new[] { Day(9, 1, 100m, 1000m), Day(10, 1, 100m, 1000m) }, new DateOnly(2026, 9, 1));

        total.AdCostCzk.Should().Be(200m);
        total.Orders.Should().Be(2);
        total.BlendedRoas.Should().Be(10.0000m);
    }
}
```

`UseCases/AdReadHandlersTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Reporting;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdChangeHistory;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdPerformance;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdSearchTerms;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetBlendedPerformance;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdAccounts;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace Anela.Heblo.Tests.Features.MarketingAds.UseCases;

public class AdReadHandlersTests
{
    private static readonly DateOnly To = new(2026, 10, 6);
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);
    private readonly Mock<IAdsReadRepository> _repo = new();

    public AdReadHandlersTests() => _repo.SetupGet(r => r.IsAvailable).Returns(true);

    private GetAdPerformanceRequest Perf(Action<GetAdPerformanceRequest>? change = null)
    {
        var request = new GetAdPerformanceRequest
        {
            Platform = AdPlatform.GoogleAds, AccountExternalId = "acc", Level = AdEntityLevel.Campaign, From = To.AddDays(-6), To = To,
        };
        change?.Invoke(request);
        return request;
    }

    [Fact]
    public async Task Unavailable_database_is_a_configuration_error_for_every_handler()
    {
        _repo.SetupGet(r => r.IsAvailable).Returns(false);

        var responses = new BaseResponse[]
        {
            await new GetAdAccountsHandler(_repo.Object).Handle(new GetAdAccountsRequest(), default),
            await new GetAdPerformanceHandler(_repo.Object).Handle(Perf(), default),
            await new GetAdSearchTermsHandler(_repo.Object).Handle(new GetAdSearchTermsRequest { AccountExternalId = "acc", From = To, To = To }, default),
            await new GetBlendedPerformanceHandler(_repo.Object).Handle(new GetBlendedPerformanceRequest { From = To, To = To }, default),
            await new GetAdChangeHistoryHandler(_repo.Object, new FakeTimeProvider(Now)).Handle(new GetAdChangeHistoryRequest(), default),
        };

        responses.Should().OnlyContain(r => !r.Success && r.ErrorCode == ErrorCodes.ConfigurationError && r.Params!["Reason"] == "AdsDatabaseNotConfigured");
    }

    [Theory]
    [InlineData(1, 0)]   // From after To
    [InlineData(-93, 0)] // 94 days
    public async Task Performance_rejects_bad_ranges(int fromOffset, int toOffset)
    {
        var response = await new GetAdPerformanceHandler(_repo.Object).Handle(Perf(r => { r.From = To.AddDays(fromOffset); r.To = To.AddDays(toOffset); }), default);

        response.ErrorCode.Should().Be(ErrorCodes.InvalidDateRange);
        _repo.Verify(r => r.GetPerformanceAsync(It.IsAny<AdPerformanceQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(0, 100, "PageNumber")]
    [InlineData(1, 0, "PageSize")]
    [InlineData(1, 501, "PageSize")]
    public async Task Performance_rejects_bad_paging(int page, int size, string field)
    {
        var response = await new GetAdPerformanceHandler(_repo.Object).Handle(Perf(r => { r.PageNumber = page; r.PageSize = size; }), default);

        response.ErrorCode.Should().Be(ErrorCodes.InvalidValue);
        response.Params!["Field"].Should().Be(field);
    }

    [Fact]
    public async Task Performance_requires_an_account()
    {
        var response = await new GetAdPerformanceHandler(_repo.Object).Handle(Perf(r => r.AccountExternalId = " "), default);

        response.ErrorCode.Should().Be(ErrorCodes.RequiredFieldMissing);
    }

    [Fact]
    public async Task Performance_unknown_account_is_not_found()
    {
        _repo.Setup(r => r.GetPerformanceAsync(It.IsAny<AdPerformanceQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync((AdPerformancePage?)null);

        var response = await new GetAdPerformanceHandler(_repo.Object).Handle(Perf(), default);

        response.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        response.Params!["Account"].Should().Be("acc");
    }

    [Fact]
    public async Task Performance_passes_the_query_and_maps_the_page()
    {
        var page = new AdPerformancePage("CZK", 7, new[] { new AdPerformanceRowDto { EntityExternalId = "c-1", Cost = 10m } });
        _repo.Setup(r => r.GetPerformanceAsync(
                new AdPerformanceQuery(AdPlatform.GoogleAds, "acc", AdEntityLevel.Campaign, To.AddDays(-6), To, "p", 2, 50),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(page);

        var response = await new GetAdPerformanceHandler(_repo.Object).Handle(Perf(r => { r.ParentExternalId = "p"; r.PageNumber = 2; r.PageSize = 50; }), default);

        response.Success.Should().BeTrue();
        response.Currency.Should().Be("CZK");
        response.TotalCount.Should().Be(7);
        response.Rows.Should().ContainSingle().Which.EntityExternalId.Should().Be("c-1");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public async Task Search_terms_reject_bad_top(int top)
    {
        var response = await new GetAdSearchTermsHandler(_repo.Object).Handle(
            new GetAdSearchTermsRequest { Platform = AdPlatform.Sklik, AccountExternalId = "acc", From = To, To = To, Top = top }, default);

        response.ErrorCode.Should().Be(ErrorCodes.InvalidValue);
        response.Params!["Field"].Should().Be("Top");
    }

    [Fact]
    public async Task Search_terms_return_the_repository_rows()
    {
        _repo.Setup(r => r.GetSearchTermsAsync(It.IsAny<AdSearchTermQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AdSearchTermRowDto> { new() { SearchTerm = "zdarma", Cost = 3m } });

        var response = await new GetAdSearchTermsHandler(_repo.Object).Handle(
            new GetAdSearchTermsRequest { Platform = AdPlatform.Sklik, AccountExternalId = "acc", From = To.AddDays(-30), To = To }, default);

        response.Terms.Should().ContainSingle().Which.SearchTerm.Should().Be("zdarma");
    }

    [Fact]
    public async Task Blended_missing_view_is_a_configuration_error()
    {
        _repo.Setup(r => r.GetBlendedDailyAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AdBlendedDayRow>?)null);

        var response = await new GetBlendedPerformanceHandler(_repo.Object).Handle(new GetBlendedPerformanceRequest { From = To.AddDays(-30), To = To }, default);

        response.ErrorCode.Should().Be(ErrorCodes.ConfigurationError);
        response.Params!["Reason"].Should().Be("BlendedViewMissing");
    }

    [Fact]
    public async Task Blended_aggregates_by_requested_grain_with_total()
    {
        _repo.Setup(r => r.GetBlendedDailyAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new AdBlendedDayRow { Date = new DateOnly(2026, 9, 1), AdCostCzk = 100m, RevenueWithoutVatCzk = 1000m, Orders = 2 },
                new AdBlendedDayRow { Date = new DateOnly(2026, 9, 2), AdCostCzk = 100m, RevenueWithoutVatCzk = 1000m, Orders = 3 },
            });

        var response = await new GetBlendedPerformanceHandler(_repo.Object).Handle(new GetBlendedPerformanceRequest
        {
            From = new DateOnly(2026, 9, 1), To = new DateOnly(2026, 9, 30), Granularity = BlendedGranularity.Monthly,
        }, default);

        response.Periods.Should().ContainSingle().Which.Orders.Should().Be(5);
        response.Total.BlendedRoas.Should().Be(10.0000m);
    }

    [Fact]
    public async Task Change_history_defaults_to_the_last_30_days()
    {
        _repo.Setup(r => r.GetChangeEventsAsync(It.IsAny<AdChangeEventQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdChangeEventPage(0, Array.Empty<AdChangeEventDto>()));

        var response = await new GetAdChangeHistoryHandler(_repo.Object, new FakeTimeProvider(Now))
            .Handle(new GetAdChangeHistoryRequest { Origin = AdChangeOrigin.OutOfBand }, default);

        response.From.Should().Be(Now.AddDays(-30));
        response.To.Should().Be(Now);
        _repo.Verify(r => r.GetChangeEventsAsync(
            It.Is<AdChangeEventQuery>(q => q.Origin == AdChangeOrigin.OutOfBand && q.From == Now.AddDays(-30) && q.PageSize == 100),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Change_history_rejects_more_than_92_days()
    {
        var response = await new GetAdChangeHistoryHandler(_repo.Object, new FakeTimeProvider(Now))
            .Handle(new GetAdChangeHistoryRequest { From = Now.AddDays(-100), To = Now }, default);

        response.ErrorCode.Should().Be(ErrorCodes.InvalidDateRange);
    }

    [Fact]
    public async Task List_accounts_passes_filters()
    {
        _repo.Setup(r => r.ListAccountsAsync(AdPlatform.MetaAds, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AdAccountDto> { new() { ExternalId = "act_1" } });

        var response = await new GetAdAccountsHandler(_repo.Object).Handle(new GetAdAccountsRequest { Platform = AdPlatform.MetaAds, IncludeUnmanaged = true }, default);

        response.Accounts.Should().ContainSingle().Which.ExternalId.Should().Be("act_1");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false`
Expected: FAIL — `CS0246` for `GetAdPerformanceHandler`, `AdBlendedAggregator`, etc.

- [ ] **Step 3: Implement shared helpers**

`Reporting/AdReadValidation.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Reporting;

/// <summary>Returns error params, or null when valid. Bounded ranges keep agent queries cheap on the B1ms server.</summary>
public static class AdReadValidation
{
    public static Dictionary<string, string>? DateRange(DateOnly from, DateOnly to, int maxDays) =>
        from > to || to.DayNumber - from.DayNumber + 1 > maxDays
            ? new Dictionary<string, string> { ["From"] = from.ToString("yyyy-MM-dd"), ["To"] = to.ToString("yyyy-MM-dd"), ["MaxDays"] = maxDays.ToString() }
            : null;

    public static Dictionary<string, string>? Positive(string field, int value, int max) =>
        value < 1 || value > max
            ? new Dictionary<string, string> { ["Field"] = field, ["Value"] = value.ToString(), ["Max"] = max.ToString() }
            : null;

    public static Dictionary<string, string> NotConfigured(string reason = "AdsDatabaseNotConfigured") => new() { ["Reason"] = reason };
}
```

`Reporting/AdBlendedAggregator.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Application.Features.MarketingAds.Reporting;

/// <summary>Sums daily rows into periods; ratios are recomputed from the sums (never averaged).</summary>
public static class AdBlendedAggregator
{
    public static List<AdBlendedPeriodDto> ToPeriods(IReadOnlyList<AdBlendedDayRow> rows, BlendedGranularity grain, DateOnly from, DateOnly to) =>
        rows.GroupBy(r => grain == BlendedGranularity.Monthly ? new DateOnly(r.Date.Year, r.Date.Month, 1) : r.Date)
            .OrderBy(g => g.Key)
            .Select(g => Build(g.ToList(), g.Key, IsPartial(g.Key, grain, from, to)))
            .ToList();

    public static AdBlendedPeriodDto Total(IReadOnlyList<AdBlendedDayRow> rows, DateOnly from) => Build(rows, from, false);

    private static bool IsPartial(DateOnly start, BlendedGranularity grain, DateOnly from, DateOnly to) =>
        grain == BlendedGranularity.Monthly && (start < from || start.AddMonths(1).AddDays(-1) > to);

    private static AdBlendedPeriodDto Build(IReadOnlyList<AdBlendedDayRow> rows, DateOnly start, bool isPartial)
    {
        var cost = rows.Sum(r => r.AdCostCzk);
        var revenue = rows.Sum(r => r.RevenueWithoutVatCzk);
        return new AdBlendedPeriodDto
        {
            PeriodStart = start,
            IsPartial = isPartial,
            AdCostCzk = cost,
            GoogleAdsCostCzk = rows.Sum(r => r.GoogleAdsCostCzk),
            MetaAdsCostCzk = rows.Sum(r => r.MetaAdsCostCzk),
            SklikCostCzk = rows.Sum(r => r.SklikCostCzk),
            HasNonCzkAdCost = rows.Any(r => r.HasNonCzkAdCost),
            Orders = rows.Sum(r => r.Orders),
            RevenueWithoutVatCzk = revenue,
            EshopRevenueWithoutVatCzk = rows.Sum(r => r.EshopRevenueWithoutVatCzk),
            BlendedRoas = AdMetrics.Roas(revenue, cost),
            PnoPercent = AdMetrics.PnoPercent(cost, revenue),
        };
    }
}
```

- [ ] **Step 4: Implement the use cases**

`UseCases/GetAdAccounts/GetAdAccountsRequest.cs`, `GetAdAccountsResponse.cs`, `GetAdAccountsHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdAccounts;

public class GetAdAccountsRequest : IRequest<GetAdAccountsResponse>
{
    public AdPlatform? Platform { get; set; }
    public bool IncludeUnmanaged { get; set; }
}
```

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdAccounts;

public class GetAdAccountsResponse : BaseResponse
{
    public List<AdAccountDto> Accounts { get; set; } = new();

    public GetAdAccountsResponse() { }
    public GetAdAccountsResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}
```

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Reporting;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdAccounts;

public class GetAdAccountsHandler : IRequestHandler<GetAdAccountsRequest, GetAdAccountsResponse>
{
    private readonly IAdsReadRepository _repository;

    public GetAdAccountsHandler(IAdsReadRepository repository) => _repository = repository;

    public async Task<GetAdAccountsResponse> Handle(GetAdAccountsRequest request, CancellationToken cancellationToken)
    {
        if (!_repository.IsAvailable)
            return new GetAdAccountsResponse(ErrorCodes.ConfigurationError, AdReadValidation.NotConfigured());

        var accounts = await _repository.ListAccountsAsync(request.Platform, request.IncludeUnmanaged, cancellationToken);
        return new GetAdAccountsResponse { Accounts = accounts.ToList() };
    }
}
```

`UseCases/GetAdPerformance/`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdPerformance;

public class GetAdPerformanceRequest : IRequest<GetAdPerformanceResponse>
{
    public AdPlatform Platform { get; set; }
    public string AccountExternalId { get; set; } = string.Empty;
    /// <summary>Exactly one level is aggregated; facts exist at every level and must never be summed across levels.</summary>
    public AdEntityLevel Level { get; set; } = AdEntityLevel.Campaign;
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    /// <summary>Only children of this campaign/ad group.</summary>
    public string? ParentExternalId { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 100;
}
```

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdPerformance;

public class GetAdPerformanceResponse : BaseResponse
{
    public AdPlatform Platform { get; set; }
    public string AccountExternalId { get; set; } = string.Empty;
    public AdEntityLevel Level { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public List<AdPerformanceRowDto> Rows { get; set; } = new();

    public GetAdPerformanceResponse() { }
    public GetAdPerformanceResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}
```

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Reporting;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdPerformance;

public class GetAdPerformanceHandler : IRequestHandler<GetAdPerformanceRequest, GetAdPerformanceResponse>
{
    public const int MaxRangeDays = 93;
    public const int MaxPageSize = 500;
    private readonly IAdsReadRepository _repository;

    public GetAdPerformanceHandler(IAdsReadRepository repository) => _repository = repository;

    public async Task<GetAdPerformanceResponse> Handle(GetAdPerformanceRequest request, CancellationToken cancellationToken)
    {
        if (!_repository.IsAvailable)
            return new GetAdPerformanceResponse(ErrorCodes.ConfigurationError, AdReadValidation.NotConfigured());
        if (string.IsNullOrWhiteSpace(request.AccountExternalId))
            return new GetAdPerformanceResponse(ErrorCodes.RequiredFieldMissing, new() { ["Field"] = nameof(request.AccountExternalId) });
        if (AdReadValidation.DateRange(request.From, request.To, MaxRangeDays) is { } rangeError)
            return new GetAdPerformanceResponse(ErrorCodes.InvalidDateRange, rangeError);
        if ((AdReadValidation.Positive(nameof(request.PageNumber), request.PageNumber, int.MaxValue)
             ?? AdReadValidation.Positive(nameof(request.PageSize), request.PageSize, MaxPageSize)) is { } pagingError)
            return new GetAdPerformanceResponse(ErrorCodes.InvalidValue, pagingError);

        var page = await _repository.GetPerformanceAsync(new AdPerformanceQuery(request.Platform, request.AccountExternalId.Trim(),
            request.Level, request.From, request.To, request.ParentExternalId, request.PageNumber, request.PageSize), cancellationToken);
        if (page is null)
            return new GetAdPerformanceResponse(ErrorCodes.ResourceNotFound, new() { ["Account"] = request.AccountExternalId });

        return new GetAdPerformanceResponse
        {
            Platform = request.Platform,
            AccountExternalId = request.AccountExternalId,
            Level = request.Level,
            From = request.From,
            To = request.To,
            Currency = page.Currency,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            TotalCount = page.TotalCount,
            Rows = page.Rows.ToList(),
        };
    }
}
```

`UseCases/GetAdSearchTerms/`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdSearchTerms;

public class GetAdSearchTermsRequest : IRequest<GetAdSearchTermsResponse>
{
    public AdPlatform Platform { get; set; }
    public string AccountExternalId { get; set; } = string.Empty;
    public string? CampaignExternalId { get; set; }
    public string? AdGroupExternalId { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public decimal? MinCost { get; set; }
    public int Top { get; set; } = 200;
}
```

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdSearchTerms;

public class GetAdSearchTermsResponse : BaseResponse
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public List<AdSearchTermRowDto> Terms { get; set; } = new();

    public GetAdSearchTermsResponse() { }
    public GetAdSearchTermsResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}
```

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Reporting;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdSearchTerms;

public class GetAdSearchTermsHandler : IRequestHandler<GetAdSearchTermsRequest, GetAdSearchTermsResponse>
{
    public const int MaxRangeDays = 93;
    public const int MaxTop = 1000;
    private readonly IAdsReadRepository _repository;

    public GetAdSearchTermsHandler(IAdsReadRepository repository) => _repository = repository;

    public async Task<GetAdSearchTermsResponse> Handle(GetAdSearchTermsRequest request, CancellationToken cancellationToken)
    {
        if (!_repository.IsAvailable)
            return new GetAdSearchTermsResponse(ErrorCodes.ConfigurationError, AdReadValidation.NotConfigured());
        if (string.IsNullOrWhiteSpace(request.AccountExternalId))
            return new GetAdSearchTermsResponse(ErrorCodes.RequiredFieldMissing, new() { ["Field"] = nameof(request.AccountExternalId) });
        if (AdReadValidation.DateRange(request.From, request.To, MaxRangeDays) is { } rangeError)
            return new GetAdSearchTermsResponse(ErrorCodes.InvalidDateRange, rangeError);
        if (AdReadValidation.Positive(nameof(request.Top), request.Top, MaxTop) is { } topError)
            return new GetAdSearchTermsResponse(ErrorCodes.InvalidValue, topError);

        var terms = await _repository.GetSearchTermsAsync(new AdSearchTermQuery(request.Platform, request.AccountExternalId.Trim(),
            request.CampaignExternalId, request.AdGroupExternalId, request.From, request.To, request.MinCost, request.Top), cancellationToken);
        if (terms is null)
            return new GetAdSearchTermsResponse(ErrorCodes.ResourceNotFound, new() { ["Account"] = request.AccountExternalId });

        return new GetAdSearchTermsResponse { From = request.From, To = request.To, Terms = terms.ToList() };
    }
}
```

`UseCases/GetBlendedPerformance/`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.UseCases.GetBlendedPerformance;

public class GetBlendedPerformanceRequest : IRequest<GetBlendedPerformanceResponse>
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public BlendedGranularity Granularity { get; set; } = BlendedGranularity.Monthly;
}
```

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.UseCases.GetBlendedPerformance;

public class GetBlendedPerformanceResponse : BaseResponse
{
    public BlendedGranularity Granularity { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public List<AdBlendedPeriodDto> Periods { get; set; } = new();
    public AdBlendedPeriodDto Total { get; set; } = new();

    public GetBlendedPerformanceResponse() { }
    public GetBlendedPerformanceResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}
```

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Reporting;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.UseCases.GetBlendedPerformance;

public class GetBlendedPerformanceHandler : IRequestHandler<GetBlendedPerformanceRequest, GetBlendedPerformanceResponse>
{
    public const int MaxRangeDays = 731;
    private readonly IAdsReadRepository _repository;

    public GetBlendedPerformanceHandler(IAdsReadRepository repository) => _repository = repository;

    public async Task<GetBlendedPerformanceResponse> Handle(GetBlendedPerformanceRequest request, CancellationToken cancellationToken)
    {
        if (!_repository.IsAvailable)
            return new GetBlendedPerformanceResponse(ErrorCodes.ConfigurationError, AdReadValidation.NotConfigured());
        if (AdReadValidation.DateRange(request.From, request.To, MaxRangeDays) is { } rangeError)
            return new GetBlendedPerformanceResponse(ErrorCodes.InvalidDateRange, rangeError);

        var rows = await _repository.GetBlendedDailyAsync(request.From, request.To, cancellationToken);
        if (rows is null)
            return new GetBlendedPerformanceResponse(ErrorCodes.ConfigurationError, AdReadValidation.NotConfigured("BlendedViewMissing"));

        return new GetBlendedPerformanceResponse
        {
            Granularity = request.Granularity,
            From = request.From,
            To = request.To,
            Periods = AdBlendedAggregator.ToPeriods(rows, request.Granularity, request.From, request.To),
            Total = AdBlendedAggregator.Total(rows, request.From),
        };
    }
}
```

`UseCases/GetAdChangeHistory/`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdChangeHistory;

public class GetAdChangeHistoryRequest : IRequest<GetAdChangeHistoryResponse>
{
    public AdPlatform? Platform { get; set; }
    public string? AccountExternalId { get; set; }
    public AdChangeOrigin? Origin { get; set; }
    public AdChangeActorKind? ActorKind { get; set; }
    /// <summary>Default: To minus 30 days.</summary>
    public DateTimeOffset? From { get; set; }
    /// <summary>Default: now.</summary>
    public DateTimeOffset? To { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 100;
}
```

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdChangeHistory;

public class GetAdChangeHistoryResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public List<AdChangeEventDto> Events { get; set; } = new();

    public GetAdChangeHistoryResponse() { }
    public GetAdChangeHistoryResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}
```

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Reporting;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdChangeHistory;

public class GetAdChangeHistoryHandler : IRequestHandler<GetAdChangeHistoryRequest, GetAdChangeHistoryResponse>
{
    public const int DefaultDays = 30;
    public const int MaxRangeDays = 92;
    public const int MaxPageSize = 500;
    private readonly IAdsReadRepository _repository;
    private readonly TimeProvider _time;

    public GetAdChangeHistoryHandler(IAdsReadRepository repository, TimeProvider time)
    {
        _repository = repository;
        _time = time;
    }

    public async Task<GetAdChangeHistoryResponse> Handle(GetAdChangeHistoryRequest request, CancellationToken cancellationToken)
    {
        if (!_repository.IsAvailable)
            return new GetAdChangeHistoryResponse(ErrorCodes.ConfigurationError, AdReadValidation.NotConfigured());

        var to = request.To ?? _time.GetUtcNow();
        var from = request.From ?? to.AddDays(-DefaultDays);
        if (from > to || to - from > TimeSpan.FromDays(MaxRangeDays))
            return new GetAdChangeHistoryResponse(ErrorCodes.InvalidDateRange, new() { ["MaxDays"] = MaxRangeDays.ToString() });
        if ((AdReadValidation.Positive(nameof(request.PageNumber), request.PageNumber, int.MaxValue)
             ?? AdReadValidation.Positive(nameof(request.PageSize), request.PageSize, MaxPageSize)) is { } pagingError)
            return new GetAdChangeHistoryResponse(ErrorCodes.InvalidValue, pagingError);

        var page = await _repository.GetChangeEventsAsync(new AdChangeEventQuery(request.Platform, request.AccountExternalId,
            request.Origin, request.ActorKind, from, to, request.PageNumber, request.PageSize), cancellationToken);

        return new GetAdChangeHistoryResponse
        {
            From = from,
            To = to,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            TotalCount = page.TotalCount,
            Events = page.Rows.ToList(),
        };
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false && dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~Features.MarketingAds.UseCases|FullyQualifiedName~Features.MarketingAds.Reporting.AdBlendedAggregatorTests"`
Expected: PASS, 20 tests.

Also run the reflection contract test that requires every Application `*Response` to inherit `BaseResponse`:

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~BaseResponse"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingAds backend/test/Anela.Heblo.Tests/Features/MarketingAds
git commit -m "feat: add mediatr read handlers for ads performance, search terms, blended and change history

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 14: Process docs, index, full validation

**Files:**
- Create: `docs/processes/sync-ads-daily.md`, `docs/processes/sync-ads-change-history.md`, `docs/processes/module-marketing-ads.md` (if C1 already created the module doc, extend it instead)
- Modify: `docs/processes/INDEX.md` (generated)

**Interfaces:**
- Consumes: everything above. Produces documentation only.

- [ ] **Step 1: Get the SHA for `verified_at`**

Run: `git rev-parse --short=9 HEAD`
Use the printed value (quoted) as `verified_at` in all three docs below — the doc's own commit in this PR also counts, so it need not be the final squash SHA.

- [ ] **Step 2: Write `docs/processes/sync-ads-daily.md`**

```markdown
---
process: sync-ads-daily
kind: sync
module: marketing-ads
summary: Daily pull of every configured ad platform's accounts, campaigns/ad groups/keywords/ads, cost and conversion facts (yesterday plus a 14-day lookback) and search terms into Heblo_V3.ads, read by AI marketing agents over MCP and by Metabase.
owns:
  - backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/**
  - backend/src/Anela.Heblo.Application/Features/MarketingAds/Infrastructure/Jobs/AdsDailySyncJob.cs
  - backend/src/Anela.Heblo.Application/Features/MarketingAds/Configuration/**
  - backend/src/Anela.Heblo.Persistence.Ads/Sql/**
verified_at: "<SHA from Step 1>"
related: [sync-ads-change-history, sync-shoptet-orders]
---

# Ads daily sync → ads (agents + Metabase)

## Purpose
The measurement backbone of the marketing agents platform (spec `docs/superpowers/specs/2026-10-07-marketing-agents-platform-design.md`, section 5). AI agents read it over MCP to *manage* campaigns (which keyword wastes money, which search term needs a negative keyword); people read the month-grain views in Metabase (`v_ads_campaign_monthly`, `v_ads_blended_monthly`). The blended view answers "how much real Shoptet revenue per koruna of ad spend", because platform-reported conversions over-count.

## Trigger
- Hangfire recurring job `ads-daily-sync` (category Marketing), cron `30 5 * * *`, Europe/Prague. `[AutomaticRetry(Attempts = 0)]`, `[DisableConcurrentExecution(3600)]`. Manual trigger from Recurring Jobs.
- Always registered (Application assembly scan). It does nothing — logs "skipped" — when `AdsDbContext` is not registered (no `AdsDatabase:ConnectionString`) or no `IAdPlatformReadSource` is registered (each platform adapter registers one only when its own secrets are really configured).

## Data flow
For every registered source (Google Ads, Meta, Sklik), sequentially:
1. `GetAccountsAsync` → upsert `ads.ad_accounts` by (platform, external_id). New accounts get `is_managed` from `Ads:ManagedAccounts`; existing rows keep theirs.
2. For each **managed** account: `GetEntitiesAsync` → upsert `ads.ad_entities` by (account, level, external_id), parents linked. For sources **without** a change log, name/status/attributes differences become `ads.ad_change_events` rows (`source = SnapshotDiff`).
3. For each date from yesterday−`FactLookbackDays` to yesterday (account time zone): `GetDailyFactsAsync` → replace that account's `ads.ad_daily_facts` rows for the date (update, insert, delete rows the platform no longer reports).
4. Same for `GetSearchTermsAsync` → `ads.ad_search_term_daily` (Google, Sklik only).
5. Each step writes `ads.sync_state` (platform × account × stream `Entities` / `DailyFacts` / `SearchTerms`): status, last_success_at, last_error, watermark (= last synced date).

## Logic & formulas
- Facts are stored at every level the platform reports. **Never sum across levels** — every query and view picks one level (views use Campaign).
- Cost is net of VAT in the account's currency. Blended views add only CZK accounts and set `has_non_czk_ad_cost` otherwise.
- Blended revenue = Shoptet `order.price_without_vat / coalesce(exchange_rate, 1)`, excluding `status_id = -4` (cancelled); e-shop revenue additionally excludes `cash_desk_order`. ROAS = revenue / cost; PNO % = cost × 100 / revenue.
- A failing source or stream is isolated: the others still run; the job then throws so Hangfire shows Failed with the (capped) error list.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `Ads:FactLookbackDays` | 14 | days before yesterday re-pulled daily (late conversions); 0..30 |
| `Ads:BatchSize` | 500 | rows per SaveChanges |
| `Ads:ManagedAccounts` | [] | `Platform:externalId` keys of Anela's own accounts; empty = all discovered accounts managed |
| `Ads:DailySync:CronExpression` | `30 5 * * *` | seed cron (DB value wins after first run) |
| `Ads:DailySync:RunTimeoutMinutes` | 45 | run is cancelled after this |
| `AdsDatabase:ConnectionString` | (KV `AdsDatabase--ConnectionString`) | gates the whole stack |

## Runtime facts
None yet — first production run pending (needs C1 migration applied, `ads_read_views.sql` run, KV secret and at least one platform read source).

## Known quirks
- Rows for entities the entity listing did not return are skipped and logged as `AdsDailySync.UnmappedRows` (e.g. facts for a removed campaign the platform no longer lists).
- Entities that disappear from a listing are not marked removed; platforms normally keep listing them with status Removed.
- If the change-event write fails after the entity rows were updated, that snapshot diff is lost (the next run sees no difference). The stream is recorded as Failed.
- Re-running `shoptet_raw_views.sql` does not affect the ads views (they read `shoptet_raw."order"`, not `order_fact`).

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Infrastructure/Jobs/AdsDailySyncJob.cs` — schedule, skip/fail semantics
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/AdsDailySyncService.cs` — orchestration
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/AdDailyFactUpserter.cs` — replace-per-day semantics
- `backend/src/Anela.Heblo.Persistence.Ads/Sql/ads_read_views.sql` — Metabase views + grants
```

- [ ] **Step 3: Write `docs/processes/sync-ads-change-history.md`**

```markdown
---
process: sync-ads-change-history
kind: sync
module: marketing-ads
summary: Hourly import of every change made to Anela's ad accounts — from each platform's change history, or by diffing entity snapshots where a platform has none — into Heblo_V3.ads.ad_change_events, each flagged as made by Heblo or out-of-band.
owns:
  - backend/src/Anela.Heblo.Application/Features/MarketingAds/Infrastructure/Jobs/AdsChangeSyncJob.cs
  - backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/AdsChangeSyncService.cs
  - backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/AdChangeOriginMatcher.cs
  - backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/AdChangeEventWriter.cs
verified_at: "<SHA from Step 1>"
related: [sync-ads-daily]
---

# Ads change history → ads.ad_change_events

## Purpose
Observability over who changed what in the ad accounts — Heblo (approved proposals), the agency, a person in the platform UI, or a platform's auto-applied recommendation. Spec success criterion 2: every change appears within a day, flagged *Heblo* or *out-of-band*. Read by agents (MCP `GetAdChangeHistory`, C4) and in Metabase (`ads.v_ads_change_events`).

## Trigger
Hangfire recurring job `ads-change-sync` (category Marketing), cron `15 * * * *`, Europe/Prague, `[AutomaticRetry(Attempts = 0)]`, `[DisableConcurrentExecution(1800)]`. Same skip rules as `ads-daily-sync`. Hangfire runs one worker, so the run is kept short (`Ads:ChangeSync:RunTimeoutMinutes` = 15).

## Data flow
For every registered source and managed account:
- **Source with a change log** (`Capabilities.ChangeLog`): `GetChangeEventsAsync(since)` where `since` = watermark − `OverlapMinutes` (first run: now − `InitialLookbackDays`), never earlier than now − `ChangeLogMaxAge` + 1 h. Rows are inserted once per (account, source, external_event_id); the watermark becomes the time the successful pull started. `sync_state` stream `ChangeEvents`.
- **Source without one**: re-list entities and diff name/status/attributes against `ads.ad_entities` (`source = SnapshotDiff`, `actor_kind = Unknown`, `external_event_id = snapshot:{level}:{id}:{field}:{unix}`); new entities after the first successful listing produce `Created` events. `sync_state` stream `Entities`.

## Logic & formulas
Origin (`AdChangeOriginMatcher`, pure): `Heblo` when an action returned by `IAdExecutionLookup` (C3's execution log) targets the same entity (target id or created resource id), is the same change (PauseAd ↔ a value "Paused"; AddNegativeKeyword ↔ created criterion id or the keyword text), executed within ±`OutOfBandMatchWindowMinutes` (120), and — when the platform reports an actor and `Ads:HebloActors:<Platform>` is configured — the actor is one of Heblo's platform users. Otherwise `OutOfBand`. Until C3 ships, the lookup is a no-op and **every change is OutOfBand**. Values that are not JSON are stored as JSON strings; timestamps are stored in UTC.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `Ads:ChangeSync:CronExpression` | `15 * * * *` | seed cron |
| `Ads:ChangeSync:InitialLookbackDays` | 7 | first pull reach |
| `Ads:ChangeSync:OverlapMinutes` | 60 | re-read before the watermark (late indexing) |
| `Ads:OutOfBandMatchWindowMinutes` | 120 | ± window for matching executions |
| `Ads:HebloActors:<Platform>` | (none) | platform user names/emails Heblo writes as |

## Runtime facts
None yet — first production run pending.

## Known quirks
- Platform change logs use free-form change types; the matcher compares string values inside the JSON, so a platform that reports a pause without the word "Paused"/"PAUSED" anywhere in the new value cannot be matched to Heblo.
- Snapshot diffs see only the net state between two runs: pause-then-re-enable within one hour is invisible on sources without a change log.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/AdsChangeSyncService.cs` — orchestration and watermark
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/AdChangeOriginMatcher.cs` — Heblo vs out-of-band rules
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Contracts/IAdExecutionLookup.cs` — port C3 implements
```

- [ ] **Step 4: Write `docs/processes/module-marketing-ads.md`** (if C1 created it, merge these sections into it)

```markdown
---
process: module-marketing-ads
kind: module
module: marketing-ads
summary: Heblo's ad-platform backbone for AI marketing agents — daily Google Ads/Meta/Sklik performance and change history in the ads schema, read by agents over MCP and by people in Metabase.
owns: []
verified_at: "<SHA from Step 1>"
related: [sync-ads-daily, sync-ads-change-history, sync-shoptet-orders]
---

# Marketing Ads (Reklamní kampaně)

## Purpose
"Heblo is the eyes and the hands, agents are the brain" (handoff `docs/handoff/marketing-agents-platform.md`). This module holds campaign-level data at management granularity for all ad platforms and records every account change, so agents can propose changes and people can see what anyone did. Proposals/approvals (C3), identity and MCP tools (C4) and UI (C5) build on it.

## Users & screens
No Heblo page yet. Agents and the chief of marketing via MCP tools (C4: `ListAdAccounts`, `GetAdPerformance`, `GetAdSearchTerms`, `GetBlendedPerformance`, `GetAdChangeHistory`, backed by this module's MediatR handlers). People via Metabase views `ads.v_ads_campaign_monthly`, `ads.v_ads_blended_monthly`, `ads.v_ads_change_events`.

## Processes
- `sync-ads-daily` — accounts, entities, daily facts, search terms; 05:30 daily.
- `sync-ads-change-history` — platform change logs / snapshot diffs, Heblo vs out-of-band; hourly.

## Data owned
- `ads.ad_accounts` — one ad account per platform; `is_managed` = Anela's own.
- `ads.ad_entities` — campaign / ad group / keyword / negative keyword / ad, current state.
- `ads.ad_daily_facts` — metrics per entity per day, every reported level.
- `ads.ad_search_term_daily` — search terms per ad group per day (Google, Sklik).
- `ads.ad_change_events` — every observed account change with origin.
- `ads.sync_state` — health per platform × account × stream.
- Views `ads.v_ads_*` (`backend/src/Anela.Heblo.Persistence.Ads/Sql/ads_read_views.sql`).

## External systems
Google Ads API, Meta Marketing API, Sklik Drak API — read only, through `IAdPlatformReadSource` implementations in the platform adapters (WS1–WS3). Reads `shoptet_raw."order"` for the blended views.

## Dependencies
Reads `shoptet_raw` (Shoptet orders mirror). Platform adapters `Adapters.GoogleAds`, `Adapters.MetaAds`, `Adapters.Sklik` plug in their read sources. C3 will plug in `IAdExecutionLookup`.

## Known quirks
- Platform-reported conversions over-count; use the blended view for "is advertising paying off".
- Non-CZK ad accounts are excluded from blended cost (flagged, not converted).

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/MarketingAdsModule.cs` — wiring and the "not configured" fallbacks
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Reporting/AdsReadRepository.cs` — agent-facing queries
```

- [ ] **Step 5: Regenerate the index and check**

Run: `python3 scripts/process-docs/check.py index && python3 scripts/process-docs/check.py check`
Expected: index regenerated with a `## marketing-ads` section; `check` reports no errors for the three new docs (no dead globs, no orphan for `AdsDailySyncJob` / `AdsChangeSyncJob`).

- [ ] **Step 6: Full validation**

```bash
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet format Anela.Heblo.sln --verify-no-changes || (dotnet format Anela.Heblo.sln && git diff --stat)
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "Category!=Integration"
podman machine start
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "Category=Integration&FullyQualifiedName~MarketingAds"
```

Expected: build succeeds with no new warnings; format clean (commit any formatting it applied); all non-integration tests pass (same failures as `main`, if any — compare with `git stash`-free check: run the same command on `origin/main` only if a failure looks unrelated); all MarketingAds integration tests pass (Tasks 10–12: 11 tests). No frontend files changed, so no FE gate.

- [ ] **Step 7: Commit**

```bash
git add docs/processes docs/architecture/metabase.md
git commit -m "docs: add process docs for ads daily and change sync

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 8: Push and open the PR**

```bash
git push -u origin feature/marketing-core-c2-sync-detection
gh pr create --title "feat: marketing core C2 — ads sync, change detection, Metabase views" --body "$(cat <<'BODY'
## Summary
- `ads-daily-sync` (05:30) and `ads-change-sync` (hourly) over every registered `IAdPlatformReadSource`, per-source/per-stream isolation, `ads.sync_state` per stream
- Upserts by natural key; facts and search terms replaced per account × day (14-day lookback)
- Change detection: platform change log or snapshot-diff fallback; pure out-of-band matcher behind `IAdExecutionLookup` (no-op until C3)
- Metabase views `v_ads_campaign_monthly`, `v_ads_blended_monthly`, `v_ads_change_events` + `metabase_ro` grants (`ads_read_views.sql`)
- MediatR read handlers for C4's MCP tools: accounts, performance, search terms, blended, change history
- Process docs + module overview

## Rollout (manual, per environment)
1. C1 migration applied (`AdsDbContext`), `AdsDatabase--ConnectionString` in KV.
2. `psql "<conn>" -v ON_ERROR_STOP=1 -f backend/src/Anela.Heblo.Persistence.Ads/Sql/ads_read_views.sql`
3. Restart the app; confirm `ads-daily-sync` and `ads-change-sync` in Recurring Jobs. Without a platform read source both log "skipped".

## Test plan
- [ ] `dotnet test --filter "FullyQualifiedName~MarketingAds"` (unit)
- [ ] `--filter "Category=Integration&FullyQualifiedName~MarketingAds"` with podman
- [ ] Staging: run views script, trigger both jobs by hand, check `ads.sync_state`

🤖 Generated with [Claude Code](https://claude.com/claude-code)
BODY
)"
```

---

## Spec deviations

1. **Views and grants ship as an idempotent SQL file** (`Persistence.Ads/Sql/ads_read_views.sql`, applied by hand with `psql`), not inside an EF migration — this is exactly how `flexi_raw`, `shoptet_raw` and `ga4_agg` do it (`docs/architecture/metabase.md`), and it keeps the views re-runnable when their definition changes.
2. **`v_ads_blended_daily` exists but is not granted.** Spec §5 names `v_ads_blended_daily`/`_monthly`; the task brief and the Metabase grant model (month-grain views only) grant only the monthly one. The daily view serves the MCP read handler.
3. **`v_ads_change_events` is granted although it is row-grain**, against the "month-grain views only" convention: change events are a few rows per day and carry no customer PII (the `actor` column can hold a staff or agency e-mail). Flag for review.
4. **Blended revenue reads `shoptet_raw."order"`, not `order_fact`**, because `shoptet_raw_views.sql` drops `order_fact` with `CASCADE` on every run. The cancelled/currency rules are duplicated from `order_fact` (with a comment to keep them in step). **Ad cost is CZK only**; non-CZK spend is flagged (`has_non_czk_ad_cost`), not converted — `ads` has no FX rates.
5. **New enums `AdChangeOrigin`, `AdChangeSource`, `AdSyncStream`** are added in C2 — spec §12.2 does not list them, but §4.1 names their values.
6. **Out-of-band "same change" uses value tokens.** §12.2's `AdChangeEventRow.ChangeType` is free-form per platform, so the matcher compares string values inside `NewValueJson` ("Paused", keyword text) plus created resource ids. Platform read sources (WS1–WS3) should keep the status / keyword text in `NewValueJson`.
7. **Snapshot diff runs in both jobs** for sources without a change log (otherwise the daily entity upsert would absorb changes); `Created` events only after the first successful entity sync; disappearing entities are not detected.
8. **Change-log watermark = time the successful pull started** (not the newest event time), with a configurable overlap; duplicates are dropped by the unique key.
9. **`is_managed` for new accounts comes from `Ads:ManagedAccounts`** (empty = all managed); existing rows are never overwritten. The spec does not say who sets it.
10. **Read side lives in Application** (`Reporting/AdsReadRepository` over `AdsDbContext`), like the GA4/Ecomail services that use their contexts directly. Handlers do not check feature permissions — C4's MCP tools gate them (`EnsureFeatureAccess`), as spec §7.5 places the gates on the tools.
11. **Tests use `ScriptedAdReadSource`** as the single read-source seam (Task 1): C1's `FakeAdPlatformReadSource` only fails globally (`FailWith`), so it cannot express per-stream isolation scenarios.
12. **Request names follow the C4 plan's expectations** (`GetAdAccountsRequest`, `PageNumber`, `BlendedGranularity Granularity`). Two remaining differences C4 must adapt to: `GetAdSearchTermsRequest` filters by `CampaignExternalId` / `AdGroupExternalId` and returns the top-N by cost (`Top`, `MinCost`) instead of `ParentLevel`/`ParentExternalId` + paging; `GetAdChangeHistoryRequest` uses `From`/`To` rather than `Since`/`Until`.
13. **`matched_execution_id` is text** (C1 stores C3's execution id as text, no cross-context FK), so `AdExecutedAction.ExecutionId` is a `string`.
