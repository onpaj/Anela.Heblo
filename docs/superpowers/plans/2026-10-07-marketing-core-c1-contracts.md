# Marketing Core C1 — Contracts, `ads` Schema, Test Kit, ADR-008 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land the shared, platform-agnostic foundation of the marketing agents platform: the contract types every ad-platform adapter implements, the `ads` Postgres schema with its own `AdsDbContext`, a reusable test kit (fakes + abstract contract-test suites), and ADR-008 — with no sync job and no proposal logic.

**Architecture:** Contracts (enums, records, interfaces, `AdSettingsGuard`) live in `Anela.Heblo.Application/Features/MarketingAds/Contracts`. A new project `Anela.Heblo.Persistence.Ads` mirrors `Anela.Heblo.Persistence.Ga4` (own keyed `NpgsqlDataSource`, own Polly pipeline, `MigrationsHistoryTable` pinned to `ads` on the runtime **and** the design-time path). `MarketingAdsModule` (Application) registers it only when `AdsDatabase:ConnectionString` is a real, parseable Npgsql connection string. A new class library `Anela.Heblo.MarketingAds.TestKit` ships `FakeAdPlatformReadSource`, `FakeAdActionExecutor` and two abstract xUnit suites whose `[Fact]`s pin the cross-platform semantics; `Anela.Heblo.Tests` runs those suites against the fakes and proves they are not vacuous.

**Tech Stack:** .NET 8, EF Core 8.0.8 + Npgsql.EntityFrameworkCore.PostgreSQL 8.0.4, Polly 8.4.1, xUnit 2.9.2, FluentAssertions 6.12.0, Testcontainers.PostgreSql 3.6.0 (integration tests, `Category=Integration`).

**Spec:** `docs/superpowers/specs/2026-10-07-marketing-agents-platform-design.md` (sections 2.2, 3, 4, 12 are binding for this PR; section 12 names are canonical). Context: `docs/handoff/marketing-agents-platform.md`.

## Global Constraints

- Contract namespace: `Anela.Heblo.Application.Features.MarketingAds.Contracts`, folder `backend/src/Anela.Heblo.Application/Features/MarketingAds/Contracts/`, **one type per file**, names and members exactly as spec 12.2.
- Enum numeric values exactly: `AdPlatform { GoogleAds = 1, MetaAds = 2, Sklik = 3 }`, `AdEntityLevel { Campaign = 1, AdGroup = 2, Keyword = 3, NegativeKeyword = 4, Ad = 5 }`, `AdEntityStatus { Unknown = 0, Enabled = 1, Paused = 2, Removed = 3 }`, `KeywordMatchType { Exact = 1, Phrase = 2, Broad = 3 }`, `AdChangeActorKind { Unknown = 0, Heblo = 1, User = 2, PlatformAutomation = 3 }`, `AdActionType { AddNegativeKeyword = 1, PauseAd = 2 }`, `AdExecutionOutcome { Succeeded = 1, Failed = 2, StaleState = 3 }`.
- Contract types are internal records (they never cross the API boundary) — the "DTOs are classes" rule does not apply to them. Nothing in this PR is an API DTO.
- `ads` persistence: project `backend/src/Anela.Heblo.Persistence.Ads/`, `AdsDbContext.SchemaName = "ads"`, `AdsPersistenceModule`, config key `AdsDatabase:ConnectionString`, Key Vault secret `AdsDatabase--ConnectionString` (never an App Service setting).
- Every analytics `DbContext` pins `MigrationsHistoryTable` to its own schema on **both** the runtime registration and the design-time factory (ADR-007).
- Registration gated on a value that passes `AdSettingsGuard.IsConfigured` **and** parses as an Npgsql connection string with a Host; a bad value must leave the stack unregistered, never crash startup.
- Test kit: project `backend/test/Anela.Heblo.MarketingAds.TestKit/`, namespace `Anela.Heblo.MarketingAds.TestKit`, classes `AdPlatformReadSourceContractTests`, `AdActionExecutorContractTests`, `FakeAdPlatformReadSource`, `FakeAdActionExecutor`.
- Tables (spec 4.1): `ad_accounts`, `ad_entities`, `ad_daily_facts`, `ad_search_term_daily`, `ad_change_events`, `sync_state` — snake_case columns.
- NO sync jobs, NO Hangfire jobs, NO proposals, NO MCP tools, NO controllers, NO frontend in this PR. Therefore no `docs/processes/` doc is needed (C2 adds them).
- Backend validation: `dotnet build` + `dotnet format` + touched tests. Build first, then `dotnet test --no-build` with `-p:UseSharedCompilation=false` (concurrent worktrees contend). CI excludes `Category=Integration`; Postgres tests need `podman machine start` locally (docker is aliased to podman).
- Commits: conventional (`feat:`, `test:`, `docs:`), each message ends with a blank line and `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **A platform returns timestamps with a local offset** (Meta/Sklik report `+02:00`). Npgsql refuses to write a non-UTC `DateTimeOffset` to `timestamptz`, so a C2 sync would crash mid-batch. Expected: stored as the same instant in UTC. Pinned in Task 2 (model inspection, every `DateTimeOffset`/`DateTimeOffset?` carries `UtcDateTimeOffsetConverter`) and Task 3 (real Postgres round trip with `+02:00`).
2. **`AdsDatabase:ConnectionString` holds a Key Vault placeholder, prose, `InMemory`, or a host-less string.** Expected: the app boots and the `ads` stack is simply unregistered. Pinned in Task 4 (`MarketingAdsModuleTests` theory).
3. **A whitespace-padded placeholder (`"  -- stored in Key Vault --"`) or a template value (`act_XXXXXXXXX`, `your-…`)** treated as a real credential would register a platform with garbage. Expected: `AdSettingsGuard.IsConfigured` returns false. Pinned in Task 1.
4. **A contract suite that passes vacuously** (e.g. asserts inside an empty loop) would let a broken platform adapter through. Expected: deliberately broken sources/executors fail the inherited facts. Pinned by the self-tests in Tasks 5 and 6.
5. **Re-syncing the same account or change event** must not silently insert duplicates. Expected: the natural-key unique indexes reject them with Postgres `23505`. Pinned in Task 3 (verified through a second `DbContext`).

---

## Before you start

- [ ] **Step 1: Read the spec and handoff** — `docs/superpowers/specs/2026-10-07-marketing-agents-platform-design.md` (all of sections 2–4 and 12) and `docs/handoff/marketing-agents-platform.md` sections 2–3. Also skim `docs/architecture/development_guidelines.md` (ADR-004, ADR-007) and `docs/architecture/metabase.md`.

- [ ] **Step 2: Check prerequisites.** C1 is the first core PR; it depends on nothing but the GA4/Analytics persistence patterns already on `main`.

```bash
git fetch origin
git ls-tree --name-only origin/main backend/src/Anela.Heblo.Persistence.Ga4/Ga4PersistenceModule.cs
git ls-tree --name-only origin/main backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/AnalyticsPersistenceModuleTests.cs
git ls-tree --name-only origin/main backend/src/Anela.Heblo.Persistence.Ads
git grep -c "ADR-008" origin/main -- docs/architecture/development_guidelines.md
```

Expected: the first two print their path; the third prints nothing (C1 not yet done); the last prints nothing (no ADR-008 yet). If `Persistence.Ads` or ADR-008 already exist on `main`, stop and report — C1 has landed.

- [ ] **Step 3: Make sure the spec travels with the PR.**

```bash
git ls-tree --name-only origin/main docs/superpowers/specs/2026-10-07-marketing-agents-platform-design.md
```

If that prints nothing, the design docs are not on `main` yet. Check `git ls-remote --heads origin feature/marketing-agents-platform`. If the branch exists, bring the docs in after creating your branch (Step 4):

```bash
git checkout origin/feature/marketing-agents-platform -- \
  docs/superpowers/specs/2026-10-07-marketing-agents-platform-design.md \
  docs/handoff/marketing-agents-platform.md \
  docs/superpowers/plans/2026-10-07-marketing-core-c1-contracts.md
git commit -m "$(cat <<'EOF'
docs: marketing agents platform spec, handoff and C1 plan

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

If the branch does not exist on origin either, stop and ask Ondrej to push `feature/marketing-agents-platform`.

- [ ] **Step 4: Create the branch** (you are in a worktree branched from `main`):

```bash
git switch -c feature/marketing-core-c1-contracts
```

---

## File Structure

**Create — contracts** (`backend/src/Anela.Heblo.Application/Features/MarketingAds/Contracts/`): `AdPlatform.cs`, `AdEntityLevel.cs`, `AdEntityStatus.cs`, `KeywordMatchType.cs`, `AdChangeActorKind.cs`, `AdActionType.cs`, `AdExecutionOutcome.cs`, `AdSourceCapabilities.cs`, `AdAccountSnapshot.cs`, `AdEntitySnapshot.cs`, `AdDailyFactRow.cs`, `AdSearchTermRow.cs`, `AdChangeEventRow.cs`, `AdAction.cs`, `AdTargetState.cs`, `AdExecutionResult.cs`, `AdActionValues.cs`, `AdActionPayloadKeys.cs`, `AdSettingsGuard.cs`, `IAdPlatformReadSource.cs`, `IAdActionExecutor.cs`.

**Create — module:** `backend/src/Anela.Heblo.Application/Features/MarketingAds/MarketingAdsModule.cs` (composition root; C2/C3 extend it).

**Create — persistence** (`backend/src/Anela.Heblo.Persistence.Ads/`): `Anela.Heblo.Persistence.Ads.csproj`, `AdsDbContext.cs`, `AdsDbContextFactory.cs`, `AdsPersistenceModule.cs`, `UtcDateTimeOffsetConverter.cs`, `Entities/AdAccount.cs`, `Entities/AdEntity.cs`, `Entities/AdDailyFact.cs`, `Entities/AdSearchTermDaily.cs`, `Entities/AdChangeEvent.cs`, `Entities/AdSyncState.cs`, `Entities/AdSyncStreams.cs`, `Entities/AdChangeSources.cs`, `Entities/AdChangeOrigins.cs`, `Migrations/<timestamp>_InitialAdsSchema*.cs` + `AdsDbContextModelSnapshot.cs` (generated).

**Create — test kit** (`backend/test/Anela.Heblo.MarketingAds.TestKit/`): `Anela.Heblo.MarketingAds.TestKit.csproj`, `FakeAdPlatformReadSource.cs`, `FakeAdActionExecutor.cs`, `AdPlatformReadSourceContractTests.cs`, `AdActionExecutorContractTests.cs`.

**Create — tests** (`backend/test/Anela.Heblo.Tests/`): `Features/MarketingAds/AdSettingsGuardTests.cs`, `Features/MarketingAds/AdContractEnumTests.cs`, `Features/MarketingAds/MarketingAdsModuleTests.cs`, `Features/MarketingAds/TestKit/FakeAdPlatformReadSourceContractTests.cs`, `Features/MarketingAds/TestKit/FakeAdPlatformReadSourceTests.cs`, `Features/MarketingAds/TestKit/AdPlatformReadSourceContractSelfTests.cs`, `Features/MarketingAds/TestKit/FakeAdActionExecutorContractTests.cs`, `Features/MarketingAds/TestKit/FakeAdActionExecutorTests.cs`, `Features/MarketingAds/TestKit/AdActionExecutorContractSelfTests.cs`, `Persistence/MarketingAds/AdsPersistenceModuleTests.cs`, `Persistence/MarketingAds/AdsDbContextMigrationIntegrationTests.cs`.

**Modify:** `Anela.Heblo.sln` (two projects), `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` (reference Persistence.Ads), `backend/src/Anela.Heblo.Application/ApplicationModule.cs` (call `AddMarketingAdsModule`), `backend/src/Anela.Heblo.API/appsettings.json` (`AdsDatabase` section), `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` (reference Persistence.Ads + TestKit), `docs/architecture/development_guidelines.md` (ADR-008), `docs/architecture/metabase.md` (`ads` row).

`Program.cs` is **not** modified: every feature module is wired through `ApplicationModule.AddApplicationServices`, which `Program.cs` already calls.

---
### Task 1: Contract types and `AdSettingsGuard`

**Files:**
- Create: all 21 files under `backend/src/Anela.Heblo.Application/Features/MarketingAds/Contracts/` listed in File Structure
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/AdSettingsGuardTests.cs`, `backend/test/Anela.Heblo.Tests/Features/MarketingAds/AdContractEnumTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces (used by every later task and by WS1–WS3): the spec 12.2 types verbatim, e.g. `IAdPlatformReadSource.GetDailyFactsAsync(string accountExternalId, DateOnly date, CancellationToken ct) : Task<IReadOnlyList<AdDailyFactRow>>`, `IAdActionExecutor.ExecuteAsync(AdAction action, CancellationToken ct) : Task<AdExecutionResult>`, `AdSettingsGuard.IsConfigured(params string?[] values) : bool`, constants `AdActionValues.Absent|Present|Enabled|Paused`, `AdActionPayloadKeys.Text|MatchType`.

- [ ] **Step 1: Write the failing tests**

`backend/test/Anela.Heblo.Tests/Features/MarketingAds/AdSettingsGuardTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.MarketingAds;

public class AdSettingsGuardTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-- stored in Key Vault --")]
    [InlineData("-- stored in secrets.json --")]
    [InlineData("  -- stored in Key Vault --")]
    [InlineData("act_XXXXXXXXX")]
    [InlineData("XXX-XXX-XXXX")]
    [InlineData("your-entra-id-group-id-here")]
    [InlineData("Your-Api-Token")]
    public void IsConfigured_returns_false_for_blank_values_and_placeholders(string? value)
    {
        // Act
        var isConfigured = AdSettingsGuard.IsConfigured(value);

        // Assert
        isConfigured.Should().BeFalse();
    }

    [Theory]
    [InlineData("act_123456789")]
    [InlineData("123-456-7890")]
    [InlineData("Host=heblosql.postgres.database.azure.com;Database=Heblo_V3;Username=heblo")]
    [InlineData("EAAB-real-looking-token")]
    public void IsConfigured_returns_true_for_real_values(string value)
    {
        AdSettingsGuard.IsConfigured(value).Should().BeTrue();
    }

    [Fact]
    public void IsConfigured_returns_false_when_any_of_several_values_is_a_placeholder()
    {
        AdSettingsGuard.IsConfigured("123-456-7890", "-- stored in Key Vault --", "token").Should().BeFalse();
    }

    [Fact]
    public void IsConfigured_returns_true_when_every_value_is_real()
    {
        AdSettingsGuard.IsConfigured("123-456-7890", "developer-token", "refresh-token").Should().BeTrue();
    }

    [Fact]
    public void IsConfigured_returns_false_when_called_without_values()
    {
        AdSettingsGuard.IsConfigured().Should().BeFalse();
        AdSettingsGuard.IsConfigured(null!).Should().BeFalse();
    }
}
```

`backend/test/Anela.Heblo.Tests/Features/MarketingAds/AdContractEnumTests.cs` — pins the canonical numeric values so a reorder can never silently change meaning:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.MarketingAds;

public class AdContractEnumTests
{
    [Fact]
    public void Enum_values_match_the_canonical_names_in_spec_section_12_2()
    {
        ((int)AdPlatform.GoogleAds).Should().Be(1);
        ((int)AdPlatform.MetaAds).Should().Be(2);
        ((int)AdPlatform.Sklik).Should().Be(3);

        ((int)AdEntityLevel.Campaign).Should().Be(1);
        ((int)AdEntityLevel.AdGroup).Should().Be(2);
        ((int)AdEntityLevel.Keyword).Should().Be(3);
        ((int)AdEntityLevel.NegativeKeyword).Should().Be(4);
        ((int)AdEntityLevel.Ad).Should().Be(5);

        ((int)AdEntityStatus.Unknown).Should().Be(0);
        ((int)AdEntityStatus.Enabled).Should().Be(1);
        ((int)AdEntityStatus.Paused).Should().Be(2);
        ((int)AdEntityStatus.Removed).Should().Be(3);

        ((int)KeywordMatchType.Exact).Should().Be(1);
        ((int)KeywordMatchType.Phrase).Should().Be(2);
        ((int)KeywordMatchType.Broad).Should().Be(3);

        ((int)AdChangeActorKind.Unknown).Should().Be(0);
        ((int)AdChangeActorKind.Heblo).Should().Be(1);
        ((int)AdChangeActorKind.User).Should().Be(2);
        ((int)AdChangeActorKind.PlatformAutomation).Should().Be(3);

        ((int)AdActionType.AddNegativeKeyword).Should().Be(1);
        ((int)AdActionType.PauseAd).Should().Be(2);

        ((int)AdExecutionOutcome.Succeeded).Should().Be(1);
        ((int)AdExecutionOutcome.Failed).Should().Be(2);
        ((int)AdExecutionOutcome.StaleState).Should().Be(3);
    }

    [Fact]
    public void Action_value_and_payload_key_constants_match_the_spec()
    {
        AdActionValues.Absent.Should().Be("Absent");
        AdActionValues.Present.Should().Be("Present");
        AdActionValues.Enabled.Should().Be("Enabled");
        AdActionValues.Paused.Should().Be("Paused");
        AdActionPayloadKeys.Text.Should().Be("text");
        AdActionPayloadKeys.MatchType.Should().Be("matchType");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false 2>&1 | grep -E "error CS" | head -5`
Expected: `error CS0234: The type or namespace name 'MarketingAds' does not exist in the namespace 'Anela.Heblo.Application.Features'`.

- [ ] **Step 3: Create the enums** (one file each, all in namespace `Anela.Heblo.Application.Features.MarketingAds.Contracts`)

`AdPlatform.cs`:
```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

public enum AdPlatform
{
    GoogleAds = 1,
    MetaAds = 2,
    Sklik = 3,
}
```

`AdEntityLevel.cs`:
```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>A Meta ad set is an <see cref="AdGroup"/>.</summary>
public enum AdEntityLevel
{
    Campaign = 1,
    AdGroup = 2,
    Keyword = 3,
    NegativeKeyword = 4,
    Ad = 5,
}
```

`AdEntityStatus.cs`:
```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

public enum AdEntityStatus
{
    Unknown = 0,
    Enabled = 1,
    Paused = 2,
    Removed = 3,
}
```

`KeywordMatchType.cs`:
```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

public enum KeywordMatchType
{
    Exact = 1,
    Phrase = 2,
    Broad = 3,
}
```

`AdChangeActorKind.cs`:
```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

public enum AdChangeActorKind
{
    Unknown = 0,
    Heblo = 1,
    User = 2,
    PlatformAutomation = 3,
}
```

`AdActionType.cs`:
```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>The v1 action allowlist (spec 4.3). New types are additive: enum value, payload schema, renderer, executor.</summary>
public enum AdActionType
{
    AddNegativeKeyword = 1,
    PauseAd = 2,
}
```

`AdExecutionOutcome.cs`:
```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>
/// <see cref="StaleState"/> is set by the core (spec 6.2) when ReadCurrentAsync disagrees with the
/// action's OldValue; executors themselves only ever return Succeeded or Failed.
/// </summary>
public enum AdExecutionOutcome
{
    Succeeded = 1,
    Failed = 2,
    StaleState = 3,
}
```

- [ ] **Step 4: Create the records and constants**

`AdSourceCapabilities.cs`:
```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>
/// What a read source can deliver. An unsupported capability makes the matching method return an
/// empty list (never throw). <paramref name="ChangeLogMaxAge"/> is how far back the platform's change
/// history reaches (Google: 30 days); null means unlimited and is only meaningful with a change log.
/// </summary>
public sealed record AdSourceCapabilities(bool SearchTerms, bool ChangeLog, TimeSpan? ChangeLogMaxAge);
```

`AdAccountSnapshot.cs`:
```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary><paramref name="Currency"/> is an ISO 4217 code (e.g. "CZK"); <paramref name="TimeZone"/> an IANA id.</summary>
public sealed record AdAccountSnapshot(string ExternalId, string Name, string Currency, string TimeZone);
```

`AdEntitySnapshot.cs`:
```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>
/// One campaign / ad group / keyword / ad as the platform reports it. Ids are platform external ids
/// only — the core maps them to <c>ads.ad_entities.id</c>. Platform-specific fields (keyword text and
/// match type, budgets, bidding strategy) go to <paramref name="Attributes"/>, stored as jsonb.
/// </summary>
public sealed record AdEntitySnapshot(
    AdEntityLevel Level, string ExternalId, AdEntityLevel? ParentLevel, string? ParentExternalId,
    string Name, AdEntityStatus Status, IReadOnlyDictionary<string, string?> Attributes);
```

`AdDailyFactRow.cs`:
```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>
/// Metrics for one entity on one day. Reported at every level the platform reports, so consumers
/// must aggregate a single level (summing levels double-counts). Cost is net of VAT, in account currency.
/// </summary>
public sealed record AdDailyFactRow(
    AdEntityLevel Level, string EntityExternalId, DateOnly Date, long Impressions, long Clicks,
    decimal Cost, decimal Conversions, decimal ConversionValue, string Currency);
```

`AdSearchTermRow.cs`:
```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

public sealed record AdSearchTermRow(
    string AdGroupExternalId, DateOnly Date, string SearchTerm, KeywordMatchType? MatchType,
    long Impressions, long Clicks, decimal Cost, decimal Conversions, decimal ConversionValue, string Currency);
```

`AdChangeEventRow.cs`:
```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>One entry of a platform's change history. Old/new values are JSON (stored as jsonb) or null.</summary>
public sealed record AdChangeEventRow(
    string ExternalEventId, DateTimeOffset OccurredAt, string? Actor, AdChangeActorKind ActorKind,
    AdEntityLevel? EntityLevel, string? EntityExternalId, string ChangeType,
    string? OldValueJson, string? NewValueJson);
```

`AdAction.cs`:
```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>
/// One typed change to an ad account (spec 4.3 / 12.2). Conventions:
/// AddNegativeKeyword → TargetLevel Campaign|AdGroup, OldValue Absent, NewValue Present, payload text + matchType (KeywordMatchType name);
/// PauseAd → TargetLevel Ad, OldValue Enabled, NewValue Paused, empty payload.
/// </summary>
public sealed record AdAction(
    AdActionType Type, AdPlatform Platform, string AccountExternalId,
    AdEntityLevel TargetLevel, string TargetExternalId,
    string OldValue, string NewValue, IReadOnlyDictionary<string, string> Payload);
```

`AdTargetState.cs`:
```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary><paramref name="CurrentValue"/> uses the <see cref="AdActionValues"/> vocabulary.</summary>
public sealed record AdTargetState(bool Exists, string? CurrentValue, string? RawJson);
```

`AdExecutionResult.cs`:
```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>
/// <paramref name="PlatformResourceId"/> identifies what the platform created or changed (for
/// AddNegativeKeyword: the negative criterion's id, which revert uses).
/// </summary>
public sealed record AdExecutionResult(
    AdExecutionOutcome Outcome, string? BeforeValue, string? AfterValue,
    string? PlatformResourceId, string? PlatformResponseJson, string? Error);
```

`AdActionValues.cs`:
```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

public static class AdActionValues
{
    public const string Absent = "Absent";
    public const string Present = "Present";
    public const string Enabled = "Enabled";
    public const string Paused = "Paused";
}
```

`AdActionPayloadKeys.cs`:
```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

public static class AdActionPayloadKeys
{
    public const string Text = "text";
    public const string MatchType = "matchType";
}
```

- [ ] **Step 5: Create the interfaces**

`IAdPlatformReadSource.cs`:
```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>
/// Read side of one ad platform (spec 4.2). Rows carry platform external ids only. Unsupported
/// capabilities return an empty list, never throw. Transport/auth errors throw; the core catches them
/// per source so one failing platform never blocks the others. A platform registers its source only
/// when <see cref="AdSettingsGuard.IsConfigured"/> holds for all its settings.
/// </summary>
public interface IAdPlatformReadSource
{
    AdPlatform Platform { get; }
    AdSourceCapabilities Capabilities { get; }
    Task<IReadOnlyList<AdAccountSnapshot>> GetAccountsAsync(CancellationToken ct);
    Task<IReadOnlyList<AdEntitySnapshot>> GetEntitiesAsync(string accountExternalId, CancellationToken ct);
    Task<IReadOnlyList<AdDailyFactRow>> GetDailyFactsAsync(string accountExternalId, DateOnly date, CancellationToken ct);
    Task<IReadOnlyList<AdSearchTermRow>> GetSearchTermsAsync(string accountExternalId, DateOnly date, CancellationToken ct);
    Task<IReadOnlyList<AdChangeEventRow>> GetChangeEventsAsync(string accountExternalId, DateTimeOffset since, CancellationToken ct);
}
```

`IAdActionExecutor.cs`:
```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>
/// Write side of one ad platform (spec 4.2 / 12.2). Executors do NOT compare old values — the core
/// does (spec 6.2). ExecuteAsync returns Failed on a platform-side rejection and throws only for
/// transport/auth failures.
/// </summary>
public interface IAdActionExecutor
{
    AdPlatform Platform { get; }
    IReadOnlySet<AdActionType> SupportedActions { get; }
    Task<AdTargetState> ReadCurrentAsync(AdAction action, CancellationToken ct);
    Task<AdExecutionResult> ExecuteAsync(AdAction action, CancellationToken ct);
    Task<AdExecutionResult> RevertAsync(AdAction action, AdExecutionResult original, CancellationToken ct);
}
```

- [ ] **Step 6: Create `AdSettingsGuard.cs`**

```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>
/// Decides whether platform settings are really configured. appsettings.json ships placeholders
/// ("-- stored in secrets.json --", "act_XXXXXXXXX", "XXX-XXX-XXXX", "your-…"), and a Key Vault
/// placeholder survives a plain IsNullOrWhiteSpace check — registering a platform on such a value
/// would make every call fail at runtime (or, with eager parsing, stop the app booting).
/// Known limitation (spec 12.2): a genuine secret containing "XXX" is treated as unconfigured.
/// </summary>
public static class AdSettingsGuard
{
    private const string PlaceholderPrefix = "--";
    private const string TemplateMarker = "XXX";
    private const string TemplatePrefix = "your-";

    public static bool IsConfigured(params string?[] values) =>
        values is { Length: > 0 } && values.All(IsRealValue);

    private static bool IsRealValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        return !trimmed.StartsWith(PlaceholderPrefix, StringComparison.Ordinal)
            && !trimmed.Contains(TemplateMarker, StringComparison.Ordinal)
            && !trimmed.StartsWith(TemplatePrefix, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.MarketingAds"
```
Expected: build `0 Error(s)`; tests `Passed!  - Failed:     0, Passed:    19`.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingAds/Contracts \
        backend/test/Anela.Heblo.Tests/Features/MarketingAds
git commit -m "$(cat <<'EOF'
feat: add marketing ads contract types and AdSettingsGuard

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: `Anela.Heblo.Persistence.Ads` — `AdsDbContext`, entities, module, design-time factory

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence.Ads/` — `Anela.Heblo.Persistence.Ads.csproj`, `AdsDbContext.cs`, `AdsDbContextFactory.cs`, `AdsPersistenceModule.cs`, `UtcDateTimeOffsetConverter.cs`, `Entities/*.cs` (9 files)
- Modify: `Anela.Heblo.sln`, `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
- Test: `backend/test/Anela.Heblo.Tests/Persistence/MarketingAds/AdsPersistenceModuleTests.cs`

**Interfaces:**
- Consumes: `Anela.Heblo.Persistence.Infrastructure.Resilience` (`IDbResiliencePipelineProvider`, `DbResiliencePipelineProvider`, `DbResilienceOptions`, `DbResilienceMetrics`, `PollyExecutionStrategy`, `NpgsqlConnectionInterceptor`) from `Anela.Heblo.Persistence`.
- Produces (C2 relies on these exact names):
  - `AdsDbContext` with `const string SchemaName = "ads"`, `const string MigrationsHistoryTableName = "__EFMigrationsHistory"`, `DbSet`s `Accounts`, `Entities`, `DailyFacts`, `SearchTermsDaily`, `ChangeEvents`, `SyncStates`.
  - `AdsPersistenceModule.ServiceKey = "ads"`, `IServiceCollection AddAdsPersistenceServices(this IServiceCollection services, string connectionString, int maxPoolSize)`.
  - Entities in `Anela.Heblo.Persistence.Ads.Entities`: `AdAccount`, `AdEntity`, `AdDailyFact`, `AdSearchTermDaily` (`const string UnknownMatchType = "Unknown"`), `AdChangeEvent`, `AdSyncState`; vocabularies `AdSyncStreams` (`Entities`, `DailyFacts`, `SearchTerms`, `ChangeEvents`), `AdChangeSources` (`PlatformChangeLog`, `SnapshotDiff`), `AdChangeOrigins` (`Heblo`, `OutOfBand`).
  - Enum-valued columns (`platform`, `level`, `status`, `actor_kind`, `match_type`) hold the **contract enum name** as text (`AdPlatform.GoogleAds.ToString()` → `"GoogleAds"`). `Persistence.Ads` cannot reference `Application` (Application references it), so the mapping lives in C2.

- [ ] **Step 1: Create the project file** `backend/src/Anela.Heblo.Persistence.Ads/Anela.Heblo.Persistence.Ads.csproj` (identical package set to `Persistence.Ga4`):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Polly" Version="8.4.1" />
    <PackageReference Include="Polly.Extensions" Version="8.4.1" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.8" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.8" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.0.4" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="8.0.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Anela.Heblo.Persistence\Anela.Heblo.Persistence.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add it to the solution and reference it from the test project**

```bash
dotnet sln Anela.Heblo.sln add backend/src/Anela.Heblo.Persistence.Ads/Anela.Heblo.Persistence.Ads.csproj
dotnet add backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj reference backend/src/Anela.Heblo.Persistence.Ads/Anela.Heblo.Persistence.Ads.csproj
grep -n "Anela.Heblo.Persistence.Ads" Anela.Heblo.sln
```

Expected: a `Project(...) = "Anela.Heblo.Persistence.Ads", "backend\src\Anela.Heblo.Persistence.Ads\..."` line. Take its GUID and confirm it is nested under the existing `src` folder:

```bash
grep -n "<GUID> = " Anela.Heblo.sln
```
Expected: `<GUID> = {87237941-D198-446F-919C-467E4968E85F}`. If `dotnet sln` created a new `backend`/`src` folder pair instead, edit `GlobalSection(NestedProjects)` by hand so the project maps to `{87237941-D198-446F-919C-467E4968E85F}` and delete the duplicate folder entries.

- [ ] **Step 3: Write the failing tests** `backend/test/Anela.Heblo.Tests/Persistence/MarketingAds/AdsPersistenceModuleTests.cs` (modelled on `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/AnalyticsPersistenceModuleTests.cs`):

```csharp
using System.Diagnostics.Metrics;
using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Persistence.Infrastructure.Resilience;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly.Timeout;

namespace Anela.Heblo.Tests.Persistence.MarketingAds;

public class AdsPersistenceModuleTests
{
    private const string ConnectionString = "Host=localhost;Database=test;Username=test;Password=test";

    private static readonly string AdsHistoryTable =
        $"{AdsDbContext.SchemaName}.\"{AdsDbContext.MigrationsHistoryTableName}\"";

    private static readonly string PublicHistoryTable =
        $"public.\"{AdsDbContext.MigrationsHistoryTableName}\"";

    [Fact]
    public async Task AdsResiliencePipeline_IsIsolatedFromTheRequestPathPipeline()
    {
        // Arrange — the request path's pipeline is tuned to a budget far too tight for a batch upsert.
        var services = new ServiceCollection();
        services.AddSingleton<IMeterFactory>(new TestMeterFactory());
        services.AddSingleton<DbResilienceMetrics>();
        services.AddSingleton<IDbResiliencePipelineProvider>(sp =>
            new DbResiliencePipelineProvider(
                Options.Create(new DbResilienceOptions { TotalTimeBudget = TimeSpan.FromMilliseconds(300) }),
                sp.GetRequiredService<DbResilienceMetrics>(),
                NullLogger<DbResiliencePipelineProvider>.Instance));
        services.AddAdsPersistenceServices(ConnectionString, maxPoolSize: 5);
        await using var provider = services.BuildServiceProvider();

        var adsPipeline = provider.GetRequiredKeyedService<IDbResiliencePipelineProvider>(AdsPersistenceModule.ServiceKey);
        var requestPathPipeline = provider.GetRequiredService<IDbResiliencePipelineProvider>();

        // Act
        var result = await adsPipeline.Pipeline.ExecuteAsync(async ct =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(600), ct);
            return 42;
        });
        var requestPathAct = async () => await requestPathPipeline.Pipeline.ExecuteAsync(async ct =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(600), ct);
            return 42;
        });

        // Assert
        adsPipeline.Should().NotBeSameAs(requestPathPipeline);
        result.Should().Be(42);
        await requestPathAct.Should().ThrowAsync<TimeoutRejectedException>();
    }

    [Fact]
    public async Task MigrationsHistoryTable_LivesInTheAdsSchema_AtRuntime()
    {
        // EF Core does not derive the history table's schema from HasDefaultSchema (ADR-007): without
        // the explicit pin this context would write into public."__EFMigrationsHistory".
        var services = new ServiceCollection();
        services.AddSingleton<IMeterFactory>(new TestMeterFactory());
        services.AddSingleton<DbResilienceMetrics>();
        services.AddLogging();
        services.AddSingleton<NpgsqlConnectionInterceptor>();
        services.AddAdsPersistenceServices(ConnectionString, maxPoolSize: 5);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AdsDbContext>();

        var createScript = context.GetInfrastructure().GetRequiredService<IHistoryRepository>().GetCreateScript();

        createScript.Should().Contain(AdsHistoryTable);
        createScript.Should().NotContain(PublicHistoryTable);
    }

    [Fact]
    public void DesignTimeFactory_AlsoPinsTheMigrationsHistoryTableToTheAdsSchema()
    {
        // `dotnet ef` goes through AdsDbContextFactory, not AddAdsPersistenceServices; migrations are
        // applied manually in this project, so this path matters as much as the runtime one.
        using var context = new AdsDbContextFactory().CreateDbContext([]);

        var createScript = context.GetInfrastructure().GetRequiredService<IHistoryRepository>().GetCreateScript();

        createScript.Should().Contain(AdsHistoryTable);
        createScript.Should().NotContain(PublicHistoryTable);
    }

    [Fact]
    public void Model_MapsExactlyTheSixAdsTables_IntoTheAdsSchema()
    {
        using var context = new AdsDbContextFactory().CreateDbContext([]);

        var tables = context.Model.GetEntityTypes()
            .Select(t => $"{t.GetSchema()}.{t.GetTableName()}")
            .ToList();

        tables.Should().BeEquivalentTo(
            "ads.ad_accounts", "ads.ad_entities", "ads.ad_daily_facts",
            "ads.ad_search_term_daily", "ads.ad_change_events", "ads.sync_state");
    }

    [Fact]
    public void EveryDateTimeOffsetProperty_IncludingNullableOnes_IsNormalisedToUtc()
    {
        // Npgsql refuses to write a DateTimeOffset with a non-zero offset to timestamptz. Platforms
        // report local offsets (+02:00), so the context normalises every such property.
        using var context = new AdsDbContextFactory().CreateDbContext([]);

        var properties = context.Model.GetEntityTypes()
            .SelectMany(t => t.GetProperties())
            .Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?))
            .ToList();

        properties.Should().Contain(p => p.ClrType == typeof(DateTimeOffset?), "sync_state.watermark is nullable");
        properties.Should().AllSatisfy(p => p.GetValueConverter().Should().BeOfType<UtcDateTimeOffsetConverter>());
    }

    [Fact]
    public void UtcDateTimeOffsetConverter_StoresTheSameInstantWithAZeroOffset()
    {
        var converter = new UtcDateTimeOffsetConverter();
        var pragueMorning = new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.FromHours(2));

        var stored = (DateTimeOffset)converter.ConvertToProvider(pragueMorning)!;

        stored.Offset.Should().Be(TimeSpan.Zero);
        stored.Should().Be(pragueMorning);
        stored.UtcDateTime.Should().Be(new DateTime(2026, 10, 6, 8, 0, 0, DateTimeKind.Utc));
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options.Name, options.Version);
        public void Dispose() { }
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false 2>&1 | grep -E "error CS" | head -5`
Expected: `error CS0246: The type or namespace name 'AdsDbContext' could not be found`.

- [ ] **Step 5: Create the entities** (namespace `Anela.Heblo.Persistence.Ads.Entities`, one per file under `backend/src/Anela.Heblo.Persistence.Ads/Entities/`)

`AdAccount.cs`:
```csharp
namespace Anela.Heblo.Persistence.Ads.Entities;

/// <summary>
/// One ad account on one platform. <see cref="Platform"/> holds the AdPlatform enum name
/// ("GoogleAds", "MetaAds", "Sklik"). <see cref="IsManaged"/> is true only for Anela's own accounts;
/// the limits engine (C3) refuses actions on unmanaged ones. Unique (platform, external_id).
/// </summary>
public class AdAccount
{
    public long Id { get; set; }
    public string Platform { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Currency { get; set; } = "";
    public string TimeZone { get; set; } = "";
    public bool IsManaged { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

`AdEntity.cs`:
```csharp
namespace Anela.Heblo.Persistence.Ads.Entities;

/// <summary>
/// Campaign / ad group / keyword / negative keyword / ad. <see cref="Level"/> and <see cref="Status"/>
/// hold the AdEntityLevel / AdEntityStatus enum names. <see cref="AttributesJson"/> is jsonb with the
/// platform-specific fields. Unique (account_id, level, external_id).
/// </summary>
public class AdEntity
{
    public long Id { get; set; }
    public long AccountId { get; set; }
    public string Level { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public long? ParentId { get; set; }
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string AttributesJson { get; set; } = "{}";
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

`AdDailyFact.cs`:
```csharp
namespace Anela.Heblo.Persistence.Ads.Entities;

/// <summary>
/// Metrics for one entity on one day, PK (entity_id, date). Stored at every level the platform
/// reports — queries must aggregate a single level. Cost is net of VAT, in account currency.
/// </summary>
public class AdDailyFact
{
    public long EntityId { get; set; }
    public DateOnly Date { get; set; }
    public long Impressions { get; set; }
    public long Clicks { get; set; }
    public decimal Cost { get; set; }
    public decimal Conversions { get; set; }
    public decimal ConversionValue { get; set; }
    public string Currency { get; set; } = "";
    public DateTimeOffset SyncedAt { get; set; }
}
```

`AdSearchTermDaily.cs`:
```csharp
namespace Anela.Heblo.Persistence.Ads.Entities;

/// <summary>
/// Search-term metrics (Google, Sklik), PK (ad_group_entity_id, date, search_term, match_type).
/// <see cref="MatchType"/> is a KeywordMatchType name, or <see cref="UnknownMatchType"/> when the
/// platform reports none — a primary-key column cannot be null.
/// </summary>
public class AdSearchTermDaily
{
    public const string UnknownMatchType = "Unknown";

    public long AdGroupEntityId { get; set; }
    public DateOnly Date { get; set; }
    public string SearchTerm { get; set; } = "";
    public string MatchType { get; set; } = UnknownMatchType;
    public long Impressions { get; set; }
    public long Clicks { get; set; }
    public decimal Cost { get; set; }
    public decimal Conversions { get; set; }
    public decimal ConversionValue { get; set; }
    public string Currency { get; set; } = "";
    public DateTimeOffset SyncedAt { get; set; }
}
```

`AdChangeEvent.cs`:
```csharp
namespace Anela.Heblo.Persistence.Ads.Entities;

/// <summary>
/// One change to an ad account, from the platform change log or the snapshot-diff fallback (C2).
/// <see cref="ActorKind"/> holds the AdChangeActorKind name; <see cref="Source"/> an
/// <see cref="AdChangeSources"/> value; <see cref="Origin"/> an <see cref="AdChangeOrigins"/> value.
/// <see cref="MatchedExecutionId"/> references the C3 execution record (ApplicationDbContext, public
/// schema) as text — no cross-context foreign key. Unique (account_id, source, external_event_id).
/// </summary>
public class AdChangeEvent
{
    public long Id { get; set; }
    public long AccountId { get; set; }
    public string ExternalEventId { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
    public string? Actor { get; set; }
    public string ActorKind { get; set; } = "";
    public long? EntityId { get; set; }
    public string? EntityExternalRef { get; set; }
    public string ChangeType { get; set; } = "";
    public string? OldValueJson { get; set; }
    public string? NewValueJson { get; set; }
    public string Source { get; set; } = "";
    public string Origin { get; set; } = "";
    public string? MatchedExecutionId { get; set; }
    public DateTimeOffset SyncedAt { get; set; }
}
```

`AdSyncState.cs`:
```csharp
namespace Anela.Heblo.Persistence.Ads.Entities;

/// <summary>Watermark per (platform, account, stream). <see cref="Stream"/> is an <see cref="AdSyncStreams"/> value.</summary>
public class AdSyncState
{
    public string Platform { get; set; } = "";
    public string AccountExternalId { get; set; } = "";
    public string Stream { get; set; } = "";
    public DateTimeOffset? Watermark { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

`AdSyncStreams.cs`:
```csharp
namespace Anela.Heblo.Persistence.Ads.Entities;

public static class AdSyncStreams
{
    public const string Entities = "Entities";
    public const string DailyFacts = "DailyFacts";
    public const string SearchTerms = "SearchTerms";
    public const string ChangeEvents = "ChangeEvents";
}
```

`AdChangeSources.cs`:
```csharp
namespace Anela.Heblo.Persistence.Ads.Entities;

public static class AdChangeSources
{
    public const string PlatformChangeLog = "PlatformChangeLog";
    public const string SnapshotDiff = "SnapshotDiff";
}
```

`AdChangeOrigins.cs`:
```csharp
namespace Anela.Heblo.Persistence.Ads.Entities;

public static class AdChangeOrigins
{
    public const string Heblo = "Heblo";
    public const string OutOfBand = "OutOfBand";
}
```

- [ ] **Step 6: Create `UtcDateTimeOffsetConverter.cs`**

```csharp
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Anela.Heblo.Persistence.Ads;

/// <summary>
/// Npgsql only writes DateTimeOffset values with a zero offset to timestamptz. Ad platforms report
/// local offsets, so every DateTimeOffset in the ads schema is normalised to the same instant in UTC.
/// </summary>
public sealed class UtcDateTimeOffsetConverter : ValueConverter<DateTimeOffset, DateTimeOffset>
{
    public UtcDateTimeOffsetConverter()
        : base(value => value.ToUniversalTime(), value => value)
    {
    }
}
```

- [ ] **Step 7: Create `AdsDbContext.cs`**

```csharp
using Anela.Heblo.Persistence.Ads.Entities;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Ads;

/// <summary>
/// The <c>ads</c> schema: normalised Google Ads / Meta Ads / Sklik data at management granularity
/// (ADR-008). Lives in Heblo_V3 next to the other reporting schemas (ADR-007) so Metabase can join
/// ad cost to shoptet_raw revenue. Holds no customer PII.
/// </summary>
public class AdsDbContext : DbContext
{
    public const string SchemaName = "ads";
    public const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    public DbSet<AdAccount> Accounts => Set<AdAccount>();
    public DbSet<AdEntity> Entities => Set<AdEntity>();
    public DbSet<AdDailyFact> DailyFacts => Set<AdDailyFact>();
    public DbSet<AdSearchTermDaily> SearchTermsDaily => Set<AdSearchTermDaily>();
    public DbSet<AdChangeEvent> ChangeEvents => Set<AdChangeEvent>();
    public DbSet<AdSyncState> SyncStates => Set<AdSyncState>();

    public AdsDbContext(DbContextOptions<AdsDbContext> options) : base(options) { }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(SchemaName);
        ConfigureAccounts(builder);
        ConfigureEntities(builder);
        ConfigureDailyFacts(builder);
        ConfigureSearchTerms(builder);
        ConfigureChangeEvents(builder);
        ConfigureSyncState(builder);
    }

    private static void ConfigureAccounts(ModelBuilder builder) =>
        builder.Entity<AdAccount>(e =>
        {
            e.ToTable("ad_accounts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            e.Property(x => x.Platform).HasColumnName("platform").IsRequired();
            e.Property(x => x.ExternalId).HasColumnName("external_id").IsRequired();
            e.Property(x => x.Name).HasColumnName("name").IsRequired();
            e.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
            e.Property(x => x.TimeZone).HasColumnName("time_zone").IsRequired();
            e.Property(x => x.IsManaged).HasColumnName("is_managed");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.Platform, x.ExternalId })
                .IsUnique()
                .HasDatabaseName("ux_ad_accounts_platform_external_id");
        });

    private static void ConfigureEntities(ModelBuilder builder) =>
        builder.Entity<AdEntity>(e =>
        {
            e.ToTable("ad_entities");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            e.Property(x => x.AccountId).HasColumnName("account_id");
            e.Property(x => x.Level).HasColumnName("level").IsRequired();
            e.Property(x => x.ExternalId).HasColumnName("external_id").IsRequired();
            e.Property(x => x.ParentId).HasColumnName("parent_id");
            e.Property(x => x.Name).HasColumnName("name").IsRequired();
            e.Property(x => x.Status).HasColumnName("status").IsRequired();
            e.Property(x => x.AttributesJson).HasColumnName("attributes").HasColumnType("jsonb").IsRequired();
            e.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at");
            e.Property(x => x.LastSeenAt).HasColumnName("last_seen_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasOne<AdAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AdEntity>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AccountId, x.Level, x.ExternalId })
                .IsUnique()
                .HasDatabaseName("ux_ad_entities_account_level_external_id");
            e.HasIndex(x => x.ParentId).HasDatabaseName("ix_ad_entities_parent_id");
        });

    private static void ConfigureDailyFacts(ModelBuilder builder) =>
        builder.Entity<AdDailyFact>(e =>
        {
            e.ToTable("ad_daily_facts");
            e.HasKey(x => new { x.EntityId, x.Date });
            e.Property(x => x.EntityId).HasColumnName("entity_id");
            e.Property(x => x.Date).HasColumnName("date");
            e.Property(x => x.Impressions).HasColumnName("impressions");
            e.Property(x => x.Clicks).HasColumnName("clicks");
            e.Property(x => x.Cost).HasColumnName("cost").HasColumnType("numeric(18,4)");
            e.Property(x => x.Conversions).HasColumnName("conversions").HasColumnType("numeric(18,4)");
            e.Property(x => x.ConversionValue).HasColumnName("conversion_value").HasColumnType("numeric(18,4)");
            e.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
            e.Property(x => x.SyncedAt).HasColumnName("synced_at");
            e.HasOne<AdEntity>().WithMany().HasForeignKey(x => x.EntityId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.Date).HasDatabaseName("ix_ad_daily_facts_date");
        });

    private static void ConfigureSearchTerms(ModelBuilder builder) =>
        builder.Entity<AdSearchTermDaily>(e =>
        {
            e.ToTable("ad_search_term_daily");
            e.HasKey(x => new { x.AdGroupEntityId, x.Date, x.SearchTerm, x.MatchType });
            e.Property(x => x.AdGroupEntityId).HasColumnName("ad_group_entity_id");
            e.Property(x => x.Date).HasColumnName("date");
            e.Property(x => x.SearchTerm).HasColumnName("search_term");
            e.Property(x => x.MatchType).HasColumnName("match_type");
            e.Property(x => x.Impressions).HasColumnName("impressions");
            e.Property(x => x.Clicks).HasColumnName("clicks");
            e.Property(x => x.Cost).HasColumnName("cost").HasColumnType("numeric(18,4)");
            e.Property(x => x.Conversions).HasColumnName("conversions").HasColumnType("numeric(18,4)");
            e.Property(x => x.ConversionValue).HasColumnName("conversion_value").HasColumnType("numeric(18,4)");
            e.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
            e.Property(x => x.SyncedAt).HasColumnName("synced_at");
            e.HasOne<AdEntity>().WithMany().HasForeignKey(x => x.AdGroupEntityId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.Date).HasDatabaseName("ix_ad_search_term_daily_date");
        });

    private static void ConfigureChangeEvents(ModelBuilder builder) =>
        builder.Entity<AdChangeEvent>(e =>
        {
            e.ToTable("ad_change_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            e.Property(x => x.AccountId).HasColumnName("account_id");
            e.Property(x => x.ExternalEventId).HasColumnName("external_event_id").IsRequired();
            e.Property(x => x.OccurredAt).HasColumnName("occurred_at");
            e.Property(x => x.Actor).HasColumnName("actor");
            e.Property(x => x.ActorKind).HasColumnName("actor_kind").IsRequired();
            e.Property(x => x.EntityId).HasColumnName("entity_id");
            e.Property(x => x.EntityExternalRef).HasColumnName("entity_external_ref");
            e.Property(x => x.ChangeType).HasColumnName("change_type").IsRequired();
            e.Property(x => x.OldValueJson).HasColumnName("old_value").HasColumnType("jsonb");
            e.Property(x => x.NewValueJson).HasColumnName("new_value").HasColumnType("jsonb");
            e.Property(x => x.Source).HasColumnName("source").IsRequired();
            e.Property(x => x.Origin).HasColumnName("origin").IsRequired();
            e.Property(x => x.MatchedExecutionId).HasColumnName("matched_execution_id");
            e.Property(x => x.SyncedAt).HasColumnName("synced_at");
            e.HasOne<AdAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AdEntity>().WithMany().HasForeignKey(x => x.EntityId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AccountId, x.Source, x.ExternalEventId })
                .IsUnique()
                .HasDatabaseName("ux_ad_change_events_account_source_external_event_id");
            e.HasIndex(x => x.OccurredAt).HasDatabaseName("ix_ad_change_events_occurred_at");
            e.HasIndex(x => x.EntityId).HasDatabaseName("ix_ad_change_events_entity_id");
        });

    private static void ConfigureSyncState(ModelBuilder builder) =>
        builder.Entity<AdSyncState>(e =>
        {
            e.ToTable("sync_state");
            e.HasKey(x => new { x.Platform, x.AccountExternalId, x.Stream });
            e.Property(x => x.Platform).HasColumnName("platform");
            e.Property(x => x.AccountExternalId).HasColumnName("account_external_id");
            e.Property(x => x.Stream).HasColumnName("stream");
            e.Property(x => x.Watermark).HasColumnName("watermark");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.LastSuccessAt).HasColumnName("last_success_at");
            e.Property(x => x.LastError).HasColumnName("last_error");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });
}
```

- [ ] **Step 8: Create `AdsDbContextFactory.cs`** (design-time; same pin as the runtime path)

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Anela.Heblo.Persistence.Ads;

/// <summary>
/// Design-time factory for <c>dotnet ef</c>. The runtime connection string comes from
/// AdsDatabase:ConnectionString; this fallback only has to be parseable.
/// </summary>
public class AdsDbContextFactory : IDesignTimeDbContextFactory<AdsDbContext>
{
    public AdsDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("AdsDatabase__ConnectionString")
            ?? "Host=localhost;Database=Heblo_V3;Username=postgres";

        var options = new DbContextOptionsBuilder<AdsDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable(AdsDbContext.MigrationsHistoryTableName, AdsDbContext.SchemaName))
            .Options;
        return new AdsDbContext(options);
    }
}
```

- [ ] **Step 9: Create `AdsPersistenceModule.cs`** (mirrors `Ga4PersistenceModule`)

```csharp
using Anela.Heblo.Persistence.Infrastructure.Resilience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Anela.Heblo.Persistence.Ads;

public static class AdsPersistenceModule
{
    public const string ServiceKey = "ads";

    /// <summary>
    /// Registers <see cref="AdsDbContext"/> against its own small connection pool and its own
    /// resilience pipeline, exactly as Ga4PersistenceModule does: the daily ad sync SaveChangesAsync's
    /// whole batches and can legitimately outrun the request-serving pipeline's short per-attempt
    /// timeout, and a separate pool keeps a long sync from starving request-path connections on the
    /// single-vCore heblosql server. The data source is keyed so DI disposes it without shadowing the
    /// main NpgsqlDataSource singleton.
    /// </summary>
    public static IServiceCollection AddAdsPersistenceServices(
        this IServiceCollection services,
        string connectionString,
        int maxPoolSize)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.ConnectionStringBuilder.KeepAlive = 30;
        dataSourceBuilder.ConnectionStringBuilder.ConnectionLifetime = 600;
        dataSourceBuilder.ConnectionStringBuilder.MaxPoolSize = maxPoolSize;
        var dataSource = dataSourceBuilder.Build();

        services.AddKeyedSingleton<NpgsqlDataSource>(ServiceKey, dataSource);

        services.AddKeyedSingleton<IDbResiliencePipelineProvider>(ServiceKey, (sp, _) =>
            new DbResiliencePipelineProvider(
                Options.Create(new DbResilienceOptions()),
                sp.GetRequiredService<DbResilienceMetrics>(),
                sp.GetService<ILogger<DbResiliencePipelineProvider>>()
                    ?? NullLogger<DbResiliencePipelineProvider>.Instance));

        services.AddDbContext<AdsDbContext>((sp, options) =>
        {
            options.UseNpgsql(dataSource, npgsql =>
            {
                npgsql.MigrationsHistoryTable(AdsDbContext.MigrationsHistoryTableName, AdsDbContext.SchemaName);
                npgsql.ExecutionStrategy(deps =>
                    new PollyExecutionStrategy(
                        deps,
                        sp.GetRequiredKeyedService<IDbResiliencePipelineProvider>(ServiceKey),
                        sp.GetRequiredService<DbResilienceMetrics>(),
                        sp.GetRequiredService<ILogger<PollyExecutionStrategy>>()));
            });
            options.AddInterceptors(sp.GetRequiredService<NpgsqlConnectionInterceptor>());
        });

        return services;
    }
}
```

- [ ] **Step 10: Run the tests to verify they pass**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Persistence.MarketingAds"
```
Expected: `Passed!  - Failed:     0, Passed:     6`.

If `EveryDateTimeOffsetProperty_IncludingNullableOnes_IsNormalisedToUtc` fails because the nullable properties (`Watermark`, `LastSuccessAt`) have no converter, add this line to `ConfigureConventions` below the existing one and re-run:

```csharp
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<UtcDateTimeOffsetConverter>();
```

- [ ] **Step 11: Commit**

```bash
git add Anela.Heblo.sln backend/src/Anela.Heblo.Persistence.Ads \
        backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
        backend/test/Anela.Heblo.Tests/Persistence/MarketingAds/AdsPersistenceModuleTests.cs
git commit -m "$(cat <<'EOF'
feat: add Persistence.Ads project with AdsDbContext for the ads schema

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Initial `ads` migration + real-Postgres integration tests

**Files:**
- Create (generated): `backend/src/Anela.Heblo.Persistence.Ads/Migrations/<timestamp>_InitialAdsSchema.cs`, `<timestamp>_InitialAdsSchema.Designer.cs`, `AdsDbContextModelSnapshot.cs`
- Test: `backend/test/Anela.Heblo.Tests/Persistence/MarketingAds/AdsDbContextMigrationIntegrationTests.cs`

**Interfaces:**
- Consumes: everything Task 2 produces; `Anela.Heblo.Tests.Common.PostgresSharedContainerFixture` (`CreateDatabaseAsync(string nameHint) : Task<string>`) via `[Collection("PostgresIntegration")]`.
- Produces: migration `InitialAdsSchema` — the schema C2 upserts into and the manual rollout applies.

- [ ] **Step 1: Write the failing integration tests** `backend/test/Anela.Heblo.Tests/Persistence/MarketingAds/AdsDbContextMigrationIntegrationTests.cs`. Everything is asserted through a **second** `AdsDbContext` — reading through the writing context would return its tracked instances and prove nothing.

```csharp
using System.Text.Json;
using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Persistence.Ads.Entities;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anela.Heblo.Tests.Persistence.MarketingAds;

/// <summary>
/// Runs the real InitialAdsSchema migration against Postgres: schema placement, the history-table
/// pin, jsonb, numeric precision, unique natural keys and timestamptz handling are all invisible to
/// the InMemory provider.
/// </summary>
[Collection("PostgresIntegration")]
[Trait("Category", "Integration")]
public class AdsDbContextMigrationIntegrationTests : IAsyncLifetime
{
    private static readonly DateOnly FactDate = new(2026, 10, 6);
    private static readonly DateTimeOffset SyncedAt = new(2026, 10, 7, 3, 30, 0, TimeSpan.Zero);

    private readonly PostgresSharedContainerFixture _fixture;
    private string _connectionString = null!;

    public AdsDbContextMigrationIntegrationTests(PostgresSharedContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _connectionString = await _fixture.CreateDatabaseAsync("ads");
        await using var context = NewContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private AdsDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AdsDbContext>()
            .UseNpgsql(_connectionString, npgsql =>
                npgsql.MigrationsHistoryTable(AdsDbContext.MigrationsHistoryTableName, AdsDbContext.SchemaName))
            .Options);

    [Fact]
    public async Task Migrate_creates_the_six_tables_and_the_history_table_inside_the_ads_schema()
    {
        // Act
        var adsTables = await ReadTableNamesAsync("ads");
        var publicTables = await ReadTableNamesAsync("public");

        // Assert
        adsTables.Should().BeEquivalentTo(
            "__EFMigrationsHistory", "ad_accounts", "ad_entities", "ad_daily_facts",
            "ad_search_term_daily", "ad_change_events", "sync_state");
        publicTables.Should().NotContain("__EFMigrationsHistory");
    }

    [Fact]
    public async Task Rows_written_by_one_context_read_back_identically_through_a_second_context()
    {
        // Arrange
        long campaignId;
        long adGroupId;
        await using (var write = NewContext())
        {
            var account = NewAccount("GoogleAds", "123-456-7890");
            write.Accounts.Add(account);
            await write.SaveChangesAsync();

            var campaign = NewEntity(account.Id, "Campaign", "campaign-1", parentId: null,
                attributesJson: "{\"dailyBudget\": \"500\", \"biddingStrategy\": \"MaximizeConversions\"}");
            write.Entities.Add(campaign);
            await write.SaveChangesAsync();

            var adGroup = NewEntity(account.Id, "AdGroup", "adgroup-1", campaign.Id, "{}");
            write.Entities.Add(adGroup);
            await write.SaveChangesAsync();

            write.DailyFacts.Add(new AdDailyFact
            {
                EntityId = campaign.Id, Date = FactDate, Impressions = 1200, Clicks = 48,
                Cost = 312.5012m, Conversions = 2.5m, ConversionValue = 2150.75m, Currency = "CZK", SyncedAt = SyncedAt,
            });
            write.SearchTermsDaily.Add(new AdSearchTermDaily
            {
                AdGroupEntityId = adGroup.Id, Date = FactDate, SearchTerm = "krém na obličej",
                MatchType = AdSearchTermDaily.UnknownMatchType, Impressions = 300, Clicks = 12,
                Cost = 80.10m, Conversions = 1m, ConversionValue = 640m, Currency = "CZK", SyncedAt = SyncedAt,
            });
            write.ChangeEvents.Add(new AdChangeEvent
            {
                AccountId = account.Id, ExternalEventId = "event-1", OccurredAt = SyncedAt.AddHours(-3),
                Actor = "agency@example.com", ActorKind = "User", EntityId = adGroup.Id, EntityExternalRef = "adgroup-1",
                ChangeType = "StatusChanged", OldValueJson = "{\"status\": \"Enabled\"}", NewValueJson = "{\"status\": \"Paused\"}",
                Source = AdChangeSources.PlatformChangeLog, Origin = AdChangeOrigins.OutOfBand, SyncedAt = SyncedAt,
            });
            write.SyncStates.Add(new AdSyncState
            {
                Platform = "GoogleAds", AccountExternalId = "123-456-7890", Stream = AdSyncStreams.DailyFacts,
                Watermark = SyncedAt, Status = "Succeeded", LastSuccessAt = SyncedAt, UpdatedAt = SyncedAt,
            });
            await write.SaveChangesAsync();
            campaignId = campaign.Id;
            adGroupId = adGroup.Id;
        }

        // Act
        await using var read = NewContext();
        var account = await read.Accounts.SingleAsync();
        var storedCampaign = await read.Entities.SingleAsync(e => e.ExternalId == "campaign-1");
        var storedAdGroup = await read.Entities.SingleAsync(e => e.ExternalId == "adgroup-1");
        var fact = await read.DailyFacts.SingleAsync();
        var searchTerm = await read.SearchTermsDaily.SingleAsync();
        var change = await read.ChangeEvents.SingleAsync();
        var syncState = await read.SyncStates.SingleAsync();

        // Assert
        account.IsManaged.Should().BeTrue();
        account.Currency.Should().Be("CZK");
        storedAdGroup.ParentId.Should().Be(campaignId);
        using (var attributes = JsonDocument.Parse(storedCampaign.AttributesJson))
        {
            attributes.RootElement.GetProperty("dailyBudget").GetString().Should().Be("500");
        }
        fact.EntityId.Should().Be(campaignId);
        fact.Date.Should().Be(FactDate);
        fact.Cost.Should().Be(312.5012m);
        fact.Conversions.Should().Be(2.5m);
        fact.ConversionValue.Should().Be(2150.75m);
        searchTerm.AdGroupEntityId.Should().Be(adGroupId);
        searchTerm.SearchTerm.Should().Be("krém na obličej");
        searchTerm.MatchType.Should().Be(AdSearchTermDaily.UnknownMatchType);
        change.EntityId.Should().Be(adGroupId);
        change.OccurredAt.Should().Be(SyncedAt.AddHours(-3));
        using (var newValue = JsonDocument.Parse(change.NewValueJson!))
        {
            newValue.RootElement.GetProperty("status").GetString().Should().Be("Paused");
        }
        syncState.Watermark.Should().Be(SyncedAt);
        syncState.LastSuccessAt.Should().Be(SyncedAt);
    }

    [Fact]
    public async Task A_second_account_with_the_same_platform_and_external_id_is_rejected()
    {
        // Arrange
        await using (var first = NewContext())
        {
            first.Accounts.Add(NewAccount("Sklik", "sklik-1"));
            await first.SaveChangesAsync();
        }

        // Act — a fresh context, so a failed SaveChanges cannot poison the arrange step's tracker
        await using var second = NewContext();
        second.Accounts.Add(NewAccount("Sklik", "sklik-1"));
        var act = () => second.SaveChangesAsync();

        // Assert
        (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task A_second_change_event_with_the_same_account_source_and_external_id_is_rejected()
    {
        // Arrange
        long accountId;
        await using (var first = NewContext())
        {
            var account = NewAccount("MetaAds", "act_1");
            first.Accounts.Add(account);
            await first.SaveChangesAsync();
            first.ChangeEvents.Add(NewChangeEvent(account.Id, "activity-1"));
            await first.SaveChangesAsync();
            accountId = account.Id;
        }

        // Act
        await using var second = NewContext();
        second.ChangeEvents.Add(NewChangeEvent(accountId, "activity-1"));
        var act = () => second.SaveChangesAsync();

        // Assert
        (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Timestamps_with_a_local_offset_are_stored_as_the_same_instant_in_utc()
    {
        // Arrange — Meta and Sklik report Prague-local offsets; Npgsql alone would refuse these.
        var pragueMorning = new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.FromHours(2));
        await using (var write = NewContext())
        {
            write.SyncStates.Add(new AdSyncState
            {
                Platform = "MetaAds", AccountExternalId = "act_1", Stream = AdSyncStreams.ChangeEvents,
                Watermark = pragueMorning, Status = "Succeeded", LastSuccessAt = pragueMorning, UpdatedAt = pragueMorning,
            });
            await write.SaveChangesAsync();
        }

        // Act
        await using var read = NewContext();
        var state = await read.SyncStates.SingleAsync();

        // Assert
        state.Watermark.Should().Be(pragueMorning);
        state.Watermark!.Value.Offset.Should().Be(TimeSpan.Zero);
        state.LastSuccessAt!.Value.UtcDateTime.Should().Be(new DateTime(2026, 10, 6, 8, 0, 0, DateTimeKind.Utc));
        state.UpdatedAt.Should().Be(pragueMorning);
    }

    private static AdAccount NewAccount(string platform, string externalId) => new()
    {
        Platform = platform, ExternalId = externalId, Name = "Anela", Currency = "CZK",
        TimeZone = "Europe/Prague", IsManaged = true, CreatedAt = SyncedAt, UpdatedAt = SyncedAt,
    };

    private static AdEntity NewEntity(long accountId, string level, string externalId, long? parentId, string attributesJson) => new()
    {
        AccountId = accountId, Level = level, ExternalId = externalId, ParentId = parentId, Name = externalId,
        Status = "Enabled", AttributesJson = attributesJson, FirstSeenAt = SyncedAt, LastSeenAt = SyncedAt, UpdatedAt = SyncedAt,
    };

    private static AdChangeEvent NewChangeEvent(long accountId, string externalEventId) => new()
    {
        AccountId = accountId, ExternalEventId = externalEventId, OccurredAt = SyncedAt, ActorKind = "Unknown",
        ChangeType = "StatusChanged", Source = AdChangeSources.PlatformChangeLog, Origin = AdChangeOrigins.OutOfBand,
        SyncedAt = SyncedAt,
    };

    private async Task<IReadOnlyList<string>> ReadTableNamesAsync(string schema)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = @schema";
        command.Parameters.AddWithValue("schema", schema);
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));
        return names;
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

```bash
podman machine start   # ignore "already running"
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build \
  --filter "FullyQualifiedName~AdsDbContextMigrationIntegrationTests"
```
Expected: all 5 fail — `Migrate_creates…` reports the `ads` schema has no tables (no migration exists, `MigrateAsync` applies nothing), the others fail with `42P01: relation "ads.ad_accounts" does not exist` (or `ads.sync_state`).

- [ ] **Step 3: Generate the migration**

```bash
dotnet ef migrations add InitialAdsSchema \
  --project backend/src/Anela.Heblo.Persistence.Ads/Anela.Heblo.Persistence.Ads.csproj \
  --startup-project backend/src/Anela.Heblo.Persistence.Ads/Anela.Heblo.Persistence.Ads.csproj \
  --context AdsDbContext --output-dir Migrations
```
Expected: `Done. To undo this action, use 'ef migrations remove'` (a tools-version warning about 8.0.11 vs 8.0.8 is harmless).

- [ ] **Step 4: Inspect the generated migration and script**

```bash
grep -n 'EnsureSchema' backend/src/Anela.Heblo.Persistence.Ads/Migrations/*_InitialAdsSchema.cs
grep -c 'schema: "ads"' backend/src/Anela.Heblo.Persistence.Ads/Migrations/*_InitialAdsSchema.cs
dotnet ef migrations script --idempotent \
  --project backend/src/Anela.Heblo.Persistence.Ads/Anela.Heblo.Persistence.Ads.csproj \
  --startup-project backend/src/Anela.Heblo.Persistence.Ads/Anela.Heblo.Persistence.Ads.csproj \
  --context AdsDbContext --output "${TMPDIR:-/tmp}/ads_schema.sql"
grep -c 'ads."__EFMigrationsHistory"' "${TMPDIR:-/tmp}/ads_schema.sql"
grep -c 'public."__EFMigrationsHistory"' "${TMPDIR:-/tmp}/ads_schema.sql"
grep -n 'jsonb\|numeric(18,4)\|timestamp with time zone' "${TMPDIR:-/tmp}/ads_schema.sql" | head -20
```
Expected: `EnsureSchema(name: "ads")`; the `schema: "ads"` count is ≥ 6; the `ads."__EFMigrationsHistory"` count is ≥ 1 and the `public."__EFMigrationsHistory"` count is `0`; `attributes`, `old_value`, `new_value` are `jsonb`, money columns `numeric(18,4)`, timestamps `timestamp with time zone`. If any expectation fails, fix `AdsDbContext`, run `dotnet ef migrations remove` with the same `--project/--startup-project/--context` arguments, and regenerate.

- [ ] **Step 5: Run the integration tests to verify they pass**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Persistence.MarketingAds"
```
Expected: `Passed!  - Failed:     0, Passed:    11` (6 unit + 5 integration).

If `Timestamps_with_a_local_offset…` fails with `Cannot write DateTimeOffset with Offset=02:00:00 to PostgreSQL type 'timestamp with time zone'`, the nullable properties lack the converter — apply the `Properties<DateTimeOffset?>()` line from Task 2 Step 10 (no new migration needed: converters do not change the schema).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence.Ads/Migrations \
        backend/test/Anela.Heblo.Tests/Persistence/MarketingAds/AdsDbContextMigrationIntegrationTests.cs
git commit -m "$(cat <<'EOF'
feat: add InitialAdsSchema migration for the ads schema

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: `MarketingAdsModule` — gated registration and wiring

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingAds/MarketingAdsModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` (ProjectReference block, lines ~44–46), `backend/src/Anela.Heblo.Application/ApplicationModule.cs` (usings + after `services.AddMarketingPerformanceModule(configuration);`), `backend/src/Anela.Heblo.API/appsettings.json` (after the `"Ga4Database"` section, ~line 653)
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/MarketingAdsModuleTests.cs`

**Interfaces:**
- Consumes: `AdSettingsGuard.IsConfigured` (Task 1), `AdsPersistenceModule.AddAdsPersistenceServices` (Task 2).
- Produces: `MarketingAdsModule.AddMarketingAdsModule(this IServiceCollection services, IConfiguration configuration) : IServiceCollection`, constants `MarketingAdsModule.ConnectionStringKey = "AdsDatabase:ConnectionString"`, `MarketingAdsModule.MaxPoolSizeKey = "AdsDatabase:MaxPoolSize"`. C2 and C3 add their registrations inside this method.

- [ ] **Step 1: Write the failing test** `backend/test/Anela.Heblo.Tests/Features/MarketingAds/MarketingAdsModuleTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds;
using Anela.Heblo.Persistence.Ads;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Tests.Features.MarketingAds;

public class MarketingAdsModuleTests
{
    private static IServiceCollection Register(string? connectionString)
    {
        var settings = new Dictionary<string, string?>();
        if (connectionString is not null)
            settings[MarketingAdsModule.ConnectionStringKey] = connectionString;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddMarketingAdsModule(configuration);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-- stored in Key Vault --")]
    [InlineData("not a connection string")]
    [InlineData("InMemory")]
    [InlineData("Database=Heblo_V3;Username=heblo")]
    public void AddMarketingAdsModule_leaves_the_ads_schema_unregistered_when_the_connection_string_is_not_usable(string? connectionString)
    {
        // Act
        var services = Register(connectionString);

        // Assert — and, implicitly, registration did not throw (a throw here would stop the API booting)
        services.Should().NotContain(d => d.ServiceType == typeof(AdsDbContext));
    }

    [Fact]
    public void AddMarketingAdsModule_registers_AdsDbContext_when_a_real_connection_string_is_configured()
    {
        var services = Register("Host=localhost;Database=Heblo_V3;Username=heblo");

        services.Should().Contain(d => d.ServiceType == typeof(AdsDbContext));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false 2>&1 | grep -E "error CS" | head -3`
Expected: `error CS0103: The name 'MarketingAdsModule' does not exist in the current context` (or CS0234 on the using).

- [ ] **Step 3: Reference Persistence.Ads from Application**

In `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`, change the ProjectReference group to:

```xml
  <ItemGroup>
    <ProjectReference Include="../Anela.Heblo.Domain/Anela.Heblo.Domain.csproj" />
    <ProjectReference Include="../Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj" />
    <ProjectReference Include="../Anela.Heblo.Persistence.Ads/Anela.Heblo.Persistence.Ads.csproj" />
    <ProjectReference Include="../Anela.Heblo.Xcc/Anela.Heblo.Xcc.csproj" />
  </ItemGroup>
```

- [ ] **Step 4: Create `MarketingAdsModule.cs`**

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Persistence.Ads;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Anela.Heblo.Application.Features.MarketingAds;

/// <summary>
/// Composition root of the MarketingAds module (spec docs/superpowers/specs/2026-10-07-marketing-agents-platform-design.md).
/// PR C1 registers only the ads schema; the sync jobs (C2) and the proposal layer (C3) add theirs here.
/// </summary>
public static class MarketingAdsModule
{
    public const string ConnectionStringKey = "AdsDatabase:ConnectionString";
    public const string MaxPoolSizeKey = "AdsDatabase:MaxPoolSize";
    private const int DefaultMaxPoolSize = 5;

    public static IServiceCollection AddMarketingAdsModule(this IServiceCollection services, IConfiguration configuration)
    {
        AddAdsPersistenceWhenConfigured(services, configuration);
        return services;
    }

    /// <summary>
    /// The ads schema stays unregistered — and the environment inert — unless the connection string
    /// is real. A Key Vault placeholder or a typo'd secret resolving to prose clears a blank check and
    /// then throws inside NpgsqlDataSourceBuilder during registration, which would take the whole API
    /// down at boot (same reasoning as ShoptetOrdersAnalyticsServiceCollectionExtensions).
    /// </summary>
    private static void AddAdsPersistenceWhenConfigured(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration[ConnectionStringKey];
        if (!AdSettingsGuard.IsConfigured(connectionString))
            return;

        if (!IsParseableConnectionString(connectionString!, out var parseError))
        {
            LogStartupWarning(
                $"{ConnectionStringKey} is set but is not a valid Npgsql connection string ({parseError}); "
                + "the ads schema stays unregistered. Check the Key Vault secret AdsDatabase--ConnectionString.");
            return;
        }

        var maxPoolSize = configuration.GetValue<int?>(MaxPoolSizeKey) ?? DefaultMaxPoolSize;
        services.AddAdsPersistenceServices(connectionString!, maxPoolSize);
    }

    private static bool IsParseableConnectionString(string value, out string error)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(value);
            if (string.IsNullOrWhiteSpace(builder.Host))
            {
                error = "no Host";
                return false;
            }

            error = "";
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Registration runs before the logging pipeline exists, so the warning goes straight to stderr.</summary>
    private static void LogStartupWarning(string message) =>
        Console.Error.WriteLine($"[MarketingAds] {message}");
}
```

- [ ] **Step 5: Wire it into `ApplicationModule.cs`**

Add the using next to the other feature usings:

```csharp
using Anela.Heblo.Application.Features.MarketingAds;
```

and directly below `services.AddMarketingPerformanceModule(configuration);`:

```csharp
        services.AddMarketingAdsModule(configuration);
```

- [ ] **Step 6: Add the config section to `backend/src/Anela.Heblo.API/appsettings.json`** right after the `"Ga4Database": { "MaxPoolSize": 5 },` block:

```json
  "AdsDatabase": {
    "ConnectionString": "",
    "MaxPoolSize": 5
  },
```

The real value is the Key Vault secret `AdsDatabase--ConnectionString` (set during rollout, see the end of this plan). Never put it in App Service settings.

- [ ] **Step 7: Run the tests to verify they pass**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.MarketingAds|FullyQualifiedName~ApplicationStartup"
```
Expected: `Failed: 0`; the 7 `MarketingAdsModuleTests` cases pass and the existing startup tests still pass (with no `AdsDatabase` value the module registers nothing).

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj \
        backend/src/Anela.Heblo.Application/ApplicationModule.cs \
        backend/src/Anela.Heblo.Application/Features/MarketingAds/MarketingAdsModule.cs \
        backend/src/Anela.Heblo.API/appsettings.json \
        backend/test/Anela.Heblo.Tests/Features/MarketingAds/MarketingAdsModuleTests.cs
git commit -m "$(cat <<'EOF'
feat: register the ads schema only when AdsDatabase is really configured

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Test kit project, `FakeAdPlatformReadSource`, `AdPlatformReadSourceContractTests`

**Files:**
- Create: `backend/test/Anela.Heblo.MarketingAds.TestKit/Anela.Heblo.MarketingAds.TestKit.csproj`, `FakeAdPlatformReadSource.cs`, `AdPlatformReadSourceContractTests.cs`
- Modify: `Anela.Heblo.sln`, `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/TestKit/FakeAdPlatformReadSourceContractTests.cs`, `FakeAdPlatformReadSourceTests.cs`, `AdPlatformReadSourceContractSelfTests.cs`

**Interfaces:**
- Consumes: Task 1 contracts.
- Produces (C2 tests and WS1–WS3 platform test projects use these):
  - `public abstract class AdPlatformReadSourceContractTests` with `protected abstract IAdPlatformReadSource CreateSource()`, `protected abstract string AccountExternalId { get; }`, `protected abstract DateOnly FixtureDate { get; }`, `public const int ChangeEventsLookbackDays = 7`, `protected DateTimeOffset ChangeEventsSince` (FixtureDate − 7 days, 00:00 UTC — recorded change-history fixtures must answer that `since`).
  - `public sealed class FakeAdPlatformReadSource : IAdPlatformReadSource` — ctor `(AdPlatform platform, AdSourceCapabilities? capabilities = null)` (default `FullCapabilities`); chainable `WithAccount`, `WithEntities`, `WithDailyFacts`, `WithSearchTerms`, `WithChangeEvents` (each `(string accountExternalId, params TRow[] rows)` except `WithAccount(AdAccountSnapshot)`), `FailWith(Exception)`; `IReadOnlyList<string> Calls` (`"GetAccounts"`, `"GetEntities:{acc}"`, `"GetDailyFacts:{acc}:{yyyy-MM-dd}"`, `"GetSearchTerms:{acc}:{yyyy-MM-dd}"`, `"GetChangeEvents:{acc}:{since:O}"`); `static CreateSample(AdPlatform, string accountExternalId, DateOnly date, AdSourceCapabilities? = null)`; constants `SampleCampaignExternalId`, `SampleAdGroupExternalId`, `SampleKeywordExternalId`, `SampleAdExternalId`, `SampleCurrency`, `static readonly FullCapabilities`.
- **Platform test projects must use xUnit 2.9.2** (`xunit` and `xunit.runner.visualstudio` 2.8.2+): the test kit depends on `xunit.extensibility.core` 2.9.2, and `xunit` 2.5.3 pins its core to an exact version.

- [ ] **Step 1: Create the test-kit project** `backend/test/Anela.Heblo.MarketingAds.TestKit/Anela.Heblo.MarketingAds.TestKit.csproj` — a class library, not a test project (it has no tests of its own to run; its `[Fact]`s run in derived classes):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>false</IsTestProject>
  </PropertyGroup>

  <!-- Platform test projects inheriting the contract suites must reference xunit 2.9.2:
       xunit 2.5.3 pins xunit.core (and so xunit.extensibility.core) to an exact version. -->
  <ItemGroup>
    <PackageReference Include="xunit.extensibility.core" Version="2.9.2" />
    <PackageReference Include="xunit.assert" Version="2.9.2" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Anela.Heblo.Application\Anela.Heblo.Application.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Add it to the solution and to `Anela.Heblo.Tests`**

```bash
dotnet sln Anela.Heblo.sln add backend/test/Anela.Heblo.MarketingAds.TestKit/Anela.Heblo.MarketingAds.TestKit.csproj
dotnet add backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj reference backend/test/Anela.Heblo.MarketingAds.TestKit/Anela.Heblo.MarketingAds.TestKit.csproj
grep -n "Anela.Heblo.MarketingAds.TestKit" Anela.Heblo.sln
```
Confirm the new GUID is nested under the existing `test` folder: `grep -n "<GUID> = " Anela.Heblo.sln` → `{23FE24B3-CD9D-4576-A7C8-85D5B012F43D}` (fix `NestedProjects` by hand if not, as in Task 2 Step 2).

- [ ] **Step 3: Write the failing tests** (namespace `Anela.Heblo.Tests.Features.MarketingAds.TestKit`)

`FakeAdPlatformReadSourceContractTests.cs` — runs every inherited contract fact against the fake, once with all capabilities and once Meta-like without search terms or change log:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.MarketingAds.TestKit;

namespace Anela.Heblo.Tests.Features.MarketingAds.TestKit;

public class FakeAdPlatformReadSourceContractTests : AdPlatformReadSourceContractTests
{
    private const string Account = "123-456-7890";
    private static readonly DateOnly Date = new(2026, 10, 6);

    protected override IAdPlatformReadSource CreateSource() =>
        FakeAdPlatformReadSource.CreateSample(AdPlatform.GoogleAds, Account, Date);

    protected override string AccountExternalId => Account;
    protected override DateOnly FixtureDate => Date;
}

public class FakeAdPlatformReadSourceWithoutOptionalCapabilitiesContractTests : AdPlatformReadSourceContractTests
{
    private const string Account = "act_1";
    private static readonly DateOnly Date = new(2026, 10, 6);

    protected override IAdPlatformReadSource CreateSource() =>
        FakeAdPlatformReadSource.CreateSample(
            AdPlatform.MetaAds, Account, Date,
            new AdSourceCapabilities(SearchTerms: false, ChangeLog: false, ChangeLogMaxAge: null));

    protected override string AccountExternalId => Account;
    protected override DateOnly FixtureDate => Date;
}
```

`FakeAdPlatformReadSourceTests.cs` — behaviour C2 will rely on:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.MarketingAds.TestKit;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.MarketingAds.TestKit;

public class FakeAdPlatformReadSourceTests
{
    private const string Account = "123";
    private static readonly DateOnly Date = new(2026, 10, 6);

    [Fact]
    public async Task An_unknown_account_yields_empty_lists()
    {
        var source = FakeAdPlatformReadSource.CreateSample(AdPlatform.GoogleAds, Account, Date);

        (await source.GetEntitiesAsync("other", CancellationToken.None)).Should().BeEmpty();
        (await source.GetDailyFactsAsync("other", Date, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Daily_facts_are_filtered_to_the_requested_date()
    {
        var source = FakeAdPlatformReadSource.CreateSample(AdPlatform.GoogleAds, Account, Date);

        (await source.GetDailyFactsAsync(Account, Date, CancellationToken.None)).Should().NotBeEmpty();
        (await source.GetDailyFactsAsync(Account, Date.AddDays(-1), CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Change_events_older_than_since_are_filtered_out()
    {
        var source = FakeAdPlatformReadSource.CreateSample(AdPlatform.GoogleAds, Account, Date);
        var afterTheSampleEvent = new DateTimeOffset(Date.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        (await source.GetChangeEventsAsync(Account, afterTheSampleEvent, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Disabled_capabilities_return_empty_lists_even_when_rows_were_added()
    {
        var source = new FakeAdPlatformReadSource(AdPlatform.MetaAds, new AdSourceCapabilities(false, false, null))
            .WithSearchTerms(Account, new AdSearchTermRow("ag", Date, "term", null, 1, 1, 1m, 0m, 0m, "CZK"))
            .WithChangeEvents(Account, new AdChangeEventRow("e1", DateTimeOffset.UnixEpoch.AddYears(56), null,
                AdChangeActorKind.Unknown, null, null, "X", null, null));

        (await source.GetSearchTermsAsync(Account, Date, CancellationToken.None)).Should().BeEmpty();
        (await source.GetChangeEventsAsync(Account, DateTimeOffset.UnixEpoch, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task FailWith_makes_every_call_throw_the_given_exception_after_recording_it()
    {
        var source = new FakeAdPlatformReadSource(AdPlatform.Sklik).FailWith(new HttpRequestException("401"));

        var act = () => source.GetAccountsAsync(CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("401");
        source.Calls.Should().Equal("GetAccounts");
    }

    [Fact]
    public async Task Calls_record_each_request_in_order()
    {
        var source = FakeAdPlatformReadSource.CreateSample(AdPlatform.GoogleAds, Account, Date);

        await source.GetAccountsAsync(CancellationToken.None);
        await source.GetDailyFactsAsync(Account, Date, CancellationToken.None);

        source.Calls.Should().Equal("GetAccounts", "GetDailyFacts:123:2026-10-06");
    }

    [Fact]
    public async Task A_cancelled_token_throws_before_returning_data()
    {
        var source = FakeAdPlatformReadSource.CreateSample(AdPlatform.GoogleAds, Account, Date);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => source.GetAccountsAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
```

`AdPlatformReadSourceContractSelfTests.cs` — proves the suite is not vacuous: each deliberately broken source must fail the matching inherited fact. The `Suite` subclass is `private`, so xUnit does not discover and run it on its own.

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.MarketingAds.TestKit;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.MarketingAds.TestKit;

public class AdPlatformReadSourceContractSelfTests
{
    private const string Account = "123";
    private static readonly DateOnly Date = new(2026, 10, 6);

    private static FakeAdPlatformReadSource Sample() =>
        FakeAdPlatformReadSource.CreateSample(AdPlatform.Sklik, Account, Date);

    [Fact]
    public async Task Contract_fails_a_source_reporting_negative_cost()
    {
        var suite = new Suite(Sample().WithDailyFacts(Account, new AdDailyFactRow(
            AdEntityLevel.Campaign, FakeAdPlatformReadSource.SampleCampaignExternalId, Date, 1, 1, -5m, 0m, 0m, "CZK")));

        var act = () => suite.GetDailyFactsAsync_returns_rows_for_the_requested_date_with_non_negative_metrics_and_a_currency();

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Contract_fails_a_source_reporting_a_lowercase_currency()
    {
        var suite = new Suite(new FakeAdPlatformReadSource(AdPlatform.Sklik)
            .WithAccount(new AdAccountSnapshot(Account, "Anela", "czk", "Europe/Prague")));

        var act = () => suite.GetAccountsAsync_returns_the_fixture_account_with_every_identity_field_set();

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Contract_fails_a_source_whose_ad_group_points_at_a_missing_campaign()
    {
        var suite = new Suite(Sample().WithEntities(Account, new AdEntitySnapshot(
            AdEntityLevel.AdGroup, "orphan", AdEntityLevel.Campaign, "campaign-missing", "Orphan",
            AdEntityStatus.Enabled, new Dictionary<string, string?>())));

        var act = () => suite.GetEntitiesAsync_campaigns_have_no_parent_and_every_other_entity_has_a_parent_in_the_same_snapshot();

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Contract_fails_a_source_whose_facts_reference_an_unknown_entity()
    {
        var suite = new Suite(Sample().WithDailyFacts(Account, new AdDailyFactRow(
            AdEntityLevel.Ad, "ad-not-in-snapshot", Date, 1, 1, 1m, 0m, 0m, "CZK")));

        var act = () => suite.GetDailyFactsAsync_rows_reference_entities_returned_by_GetEntitiesAsync();

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Contract_fails_a_source_returning_duplicate_change_event_ids()
    {
        var occurredAt = new DateTimeOffset(Date.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);
        var suite = new Suite(Sample().WithChangeEvents(Account, new AdChangeEventRow(
            "change-1", occurredAt, null, AdChangeActorKind.Unknown, null, null, "StatusChanged", null, null)));

        var act = () => suite.GetChangeEventsAsync_rows_are_unique_well_formed_and_not_older_than_since();

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Contract_fails_a_source_returning_change_values_that_are_not_json()
    {
        var occurredAt = new DateTimeOffset(Date.ToDateTime(new TimeOnly(11, 0)), TimeSpan.Zero);
        var suite = new Suite(Sample().WithChangeEvents(Account, new AdChangeEventRow(
            "change-2", occurredAt, null, AdChangeActorKind.Unknown, null, null, "StatusChanged", "Enabled", null)));

        var act = () => suite.GetChangeEventsAsync_rows_are_unique_well_formed_and_not_older_than_since();

        await act.Should().ThrowAsync<Exception>();
    }

    private sealed class Suite(IAdPlatformReadSource source) : AdPlatformReadSourceContractTests
    {
        protected override IAdPlatformReadSource CreateSource() => source;
        protected override string AccountExternalId => Account;
        protected override DateOnly FixtureDate => Date;
    }
}
```

- [ ] **Step 4: Run them to verify they fail**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false 2>&1 | grep -E "error CS" | head -3`
Expected: `error CS0246: The type or namespace name 'AdPlatformReadSourceContractTests' could not be found`.

- [ ] **Step 5: Implement `FakeAdPlatformReadSource.cs`**

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.MarketingAds.TestKit;

/// <summary>
/// In-memory IAdPlatformReadSource so the core (C2 sync, C3/C4 handlers) is built and tested with no
/// platform present. Data is settable per account; disabled capabilities return empty lists like a
/// real source; <see cref="FailWith"/> simulates a transport/auth failure; <see cref="Calls"/>
/// records every request (recorded before a simulated failure is thrown).
/// </summary>
public sealed class FakeAdPlatformReadSource : IAdPlatformReadSource
{
    public const string SampleCampaignExternalId = "campaign-1";
    public const string SampleAdGroupExternalId = "adgroup-1";
    public const string SampleKeywordExternalId = "adgroup-1~keyword-1";
    public const string SampleAdExternalId = "adgroup-1~ad-1";
    public const string SampleCurrency = "CZK";

    public static readonly AdSourceCapabilities FullCapabilities =
        new(SearchTerms: true, ChangeLog: true, ChangeLogMaxAge: TimeSpan.FromDays(30));

    private readonly List<AdAccountSnapshot> _accounts = [];
    private readonly Dictionary<string, List<AdEntitySnapshot>> _entities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<AdDailyFactRow>> _dailyFacts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<AdSearchTermRow>> _searchTerms = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<AdChangeEventRow>> _changeEvents = new(StringComparer.Ordinal);
    private readonly List<string> _calls = [];
    private Exception? _failure;

    public FakeAdPlatformReadSource(AdPlatform platform, AdSourceCapabilities? capabilities = null)
    {
        Platform = platform;
        Capabilities = capabilities ?? FullCapabilities;
    }

    public AdPlatform Platform { get; }
    public AdSourceCapabilities Capabilities { get; }
    public IReadOnlyList<string> Calls => _calls;

    /// <summary>
    /// One account with campaign → ad group → (keyword, ad), facts at campaign and ad level for
    /// <paramref name="date"/>, one search term (when supported) and one change event at 09:00 UTC.
    /// </summary>
    public static FakeAdPlatformReadSource CreateSample(
        AdPlatform platform, string accountExternalId, DateOnly date, AdSourceCapabilities? capabilities = null)
    {
        var noAttributes = new Dictionary<string, string?>();
        var source = new FakeAdPlatformReadSource(platform, capabilities)
            .WithAccount(new AdAccountSnapshot(accountExternalId, "Anela sample account", SampleCurrency, "Europe/Prague"))
            .WithEntities(accountExternalId,
                new AdEntitySnapshot(AdEntityLevel.Campaign, SampleCampaignExternalId, null, null, "Brand",
                    AdEntityStatus.Enabled, new Dictionary<string, string?> { ["dailyBudget"] = "500" }),
                new AdEntitySnapshot(AdEntityLevel.AdGroup, SampleAdGroupExternalId, AdEntityLevel.Campaign,
                    SampleCampaignExternalId, "Pleťové krémy", AdEntityStatus.Enabled, noAttributes),
                new AdEntitySnapshot(AdEntityLevel.Keyword, SampleKeywordExternalId, AdEntityLevel.AdGroup,
                    SampleAdGroupExternalId, "pleťový krém", AdEntityStatus.Enabled,
                    new Dictionary<string, string?> { ["text"] = "pleťový krém", ["matchType"] = nameof(KeywordMatchType.Phrase) }),
                new AdEntitySnapshot(AdEntityLevel.Ad, SampleAdExternalId, AdEntityLevel.AdGroup,
                    SampleAdGroupExternalId, "Responsive ad 1", AdEntityStatus.Enabled, noAttributes))
            .WithDailyFacts(accountExternalId,
                new AdDailyFactRow(AdEntityLevel.Campaign, SampleCampaignExternalId, date, 1200, 48, 312.50m, 3m, 2150m, SampleCurrency),
                new AdDailyFactRow(AdEntityLevel.Ad, SampleAdExternalId, date, 1200, 48, 312.50m, 3m, 2150m, SampleCurrency))
            .WithSearchTerms(accountExternalId,
                new AdSearchTermRow(SampleAdGroupExternalId, date, "krém na obličej", KeywordMatchType.Phrase,
                    300, 12, 80.10m, 1m, 640m, SampleCurrency))
            .WithChangeEvents(accountExternalId,
                new AdChangeEventRow("change-1", new DateTimeOffset(date.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero),
                    "agency@example.com", AdChangeActorKind.User, AdEntityLevel.Ad, SampleAdExternalId, "StatusChanged",
                    "{\"status\":\"Enabled\"}", "{\"status\":\"Paused\"}"));
        return source;
    }

    public FakeAdPlatformReadSource WithAccount(AdAccountSnapshot account)
    {
        _accounts.Add(account);
        return this;
    }

    public FakeAdPlatformReadSource WithEntities(string accountExternalId, params AdEntitySnapshot[] entities) =>
        AddTo(_entities, accountExternalId, entities);

    public FakeAdPlatformReadSource WithDailyFacts(string accountExternalId, params AdDailyFactRow[] rows) =>
        AddTo(_dailyFacts, accountExternalId, rows);

    public FakeAdPlatformReadSource WithSearchTerms(string accountExternalId, params AdSearchTermRow[] rows) =>
        AddTo(_searchTerms, accountExternalId, rows);

    public FakeAdPlatformReadSource WithChangeEvents(string accountExternalId, params AdChangeEventRow[] rows) =>
        AddTo(_changeEvents, accountExternalId, rows);

    public FakeAdPlatformReadSource FailWith(Exception failure)
    {
        _failure = failure;
        return this;
    }

    public Task<IReadOnlyList<AdAccountSnapshot>> GetAccountsAsync(CancellationToken ct)
    {
        Record(ct, "GetAccounts");
        return Result(_accounts);
    }

    public Task<IReadOnlyList<AdEntitySnapshot>> GetEntitiesAsync(string accountExternalId, CancellationToken ct)
    {
        Record(ct, $"GetEntities:{accountExternalId}");
        return Result(RowsOf(_entities, accountExternalId));
    }

    public Task<IReadOnlyList<AdDailyFactRow>> GetDailyFactsAsync(string accountExternalId, DateOnly date, CancellationToken ct)
    {
        Record(ct, $"GetDailyFacts:{accountExternalId}:{date:yyyy-MM-dd}");
        return Result(RowsOf(_dailyFacts, accountExternalId).Where(r => r.Date == date));
    }

    public Task<IReadOnlyList<AdSearchTermRow>> GetSearchTermsAsync(string accountExternalId, DateOnly date, CancellationToken ct)
    {
        Record(ct, $"GetSearchTerms:{accountExternalId}:{date:yyyy-MM-dd}");
        return Capabilities.SearchTerms
            ? Result(RowsOf(_searchTerms, accountExternalId).Where(r => r.Date == date))
            : Result(Enumerable.Empty<AdSearchTermRow>());
    }

    public Task<IReadOnlyList<AdChangeEventRow>> GetChangeEventsAsync(string accountExternalId, DateTimeOffset since, CancellationToken ct)
    {
        Record(ct, $"GetChangeEvents:{accountExternalId}:{since:O}");
        return Capabilities.ChangeLog
            ? Result(RowsOf(_changeEvents, accountExternalId).Where(r => r.OccurredAt >= since))
            : Result(Enumerable.Empty<AdChangeEventRow>());
    }

    private FakeAdPlatformReadSource AddTo<T>(Dictionary<string, List<T>> store, string accountExternalId, T[] rows)
    {
        if (!store.TryGetValue(accountExternalId, out var bucket))
        {
            bucket = [];
            store[accountExternalId] = bucket;
        }

        bucket.AddRange(rows);
        return this;
    }

    private void Record(CancellationToken ct, string call)
    {
        ct.ThrowIfCancellationRequested();
        _calls.Add(call);
        if (_failure is not null)
            throw _failure;
    }

    private static IEnumerable<T> RowsOf<T>(Dictionary<string, List<T>> store, string accountExternalId) =>
        store.TryGetValue(accountExternalId, out var rows) ? rows : Enumerable.Empty<T>();

    private static Task<IReadOnlyList<T>> Result<T>(IEnumerable<T> rows) =>
        Task.FromResult<IReadOnlyList<T>>(rows.ToList());
}
```

- [ ] **Step 6: Implement `AdPlatformReadSourceContractTests.cs`**

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;

namespace Anela.Heblo.MarketingAds.TestKit;

/// <summary>
/// Cross-platform semantics every IAdPlatformReadSource must satisfy (spec 4.2, 4.4, 12.3). A
/// platform test project derives from this class, wires CreateSource() to recorded JSON fixtures and
/// inherits every [Fact]. Never override or skip a fact: a platform that cannot meet one is a design
/// question for the core, not a test to silence. The fixture must contain at least one entity, one
/// daily fact on FixtureDate, and — when advertised — one search term on FixtureDate and one change
/// event on or after <see cref="ChangeEventsSince"/>.
/// </summary>
public abstract class AdPlatformReadSourceContractTests
{
    public const int ChangeEventsLookbackDays = 7;
    private const string CurrencyPattern = "^[A-Z]{3}$";

    protected abstract IAdPlatformReadSource CreateSource();
    protected abstract string AccountExternalId { get; }
    protected abstract DateOnly FixtureDate { get; }

    protected DateTimeOffset ChangeEventsSince =>
        new(FixtureDate.AddDays(-ChangeEventsLookbackDays).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    [Fact]
    public void Platform_is_a_defined_AdPlatform()
    {
        Enum.IsDefined(CreateSource().Platform).Should().BeTrue();
    }

    [Fact]
    public void Capabilities_report_a_change_log_max_age_only_with_a_change_log_and_only_a_positive_one()
    {
        var capabilities = CreateSource().Capabilities;

        capabilities.Should().NotBeNull();
        if (!capabilities.ChangeLog)
        {
            capabilities.ChangeLogMaxAge.Should().BeNull();
            return;
        }

        if (capabilities.ChangeLogMaxAge is { } maxAge)
            maxAge.Should().BePositive();
    }

    [Fact]
    public async Task GetAccountsAsync_returns_the_fixture_account_with_every_identity_field_set()
    {
        var accounts = await CreateSource().GetAccountsAsync(CancellationToken.None);

        accounts.Should().Contain(a => a.ExternalId == AccountExternalId);
        accounts.Should().AllSatisfy(a =>
        {
            a.ExternalId.Should().NotBeNullOrWhiteSpace();
            a.Name.Should().NotBeNullOrWhiteSpace();
            a.TimeZone.Should().NotBeNullOrWhiteSpace();
            a.Currency.Should().MatchRegex(CurrencyPattern);
        });
    }

    [Fact]
    public async Task GetEntitiesAsync_returns_entities_with_defined_levels_statuses_and_non_empty_ids()
    {
        var entities = await CreateSource().GetEntitiesAsync(AccountExternalId, CancellationToken.None);

        entities.Should().NotBeEmpty();
        entities.Should().AllSatisfy(e =>
        {
            Enum.IsDefined(e.Level).Should().BeTrue();
            Enum.IsDefined(e.Status).Should().BeTrue();
            e.ExternalId.Should().NotBeNullOrWhiteSpace();
            e.Name.Should().NotBeNull();
            e.Attributes.Should().NotBeNull();
        });
    }

    [Fact]
    public async Task GetEntitiesAsync_external_ids_are_unique_within_each_level()
    {
        // ads.ad_entities is unique on (account_id, level, external_id): platforms whose ids are only
        // unique within a parent (Google keyword criteria) must emit composite ids.
        var entities = await CreateSource().GetEntitiesAsync(AccountExternalId, CancellationToken.None);

        entities.Select(e => (e.Level, e.ExternalId)).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task GetEntitiesAsync_campaigns_have_no_parent_and_every_other_entity_has_a_parent_in_the_same_snapshot()
    {
        var entities = await CreateSource().GetEntitiesAsync(AccountExternalId, CancellationToken.None);
        var keys = entities.Select(e => (e.Level, e.ExternalId)).ToHashSet();

        entities.Should().AllSatisfy(e =>
        {
            if (e.Level == AdEntityLevel.Campaign)
            {
                e.ParentLevel.Should().BeNull();
                e.ParentExternalId.Should().BeNull();
                return;
            }

            e.ParentLevel.Should().NotBeNull();
            e.ParentExternalId.Should().NotBeNullOrWhiteSpace();
            keys.Should().Contain((e.ParentLevel!.Value, e.ParentExternalId!));
        });
    }

    [Fact]
    public async Task GetDailyFactsAsync_returns_rows_for_the_requested_date_with_non_negative_metrics_and_a_currency()
    {
        var facts = await CreateSource().GetDailyFactsAsync(AccountExternalId, FixtureDate, CancellationToken.None);

        facts.Should().NotBeEmpty();
        facts.Should().AllSatisfy(f =>
        {
            Enum.IsDefined(f.Level).Should().BeTrue();
            f.EntityExternalId.Should().NotBeNullOrWhiteSpace();
            f.Date.Should().Be(FixtureDate);
            f.Impressions.Should().BeGreaterThanOrEqualTo(0);
            f.Clicks.Should().BeGreaterThanOrEqualTo(0);
            f.Cost.Should().BeGreaterThanOrEqualTo(0m);
            f.Conversions.Should().BeGreaterThanOrEqualTo(0m);
            f.ConversionValue.Should().BeGreaterThanOrEqualTo(0m);
            f.Currency.Should().MatchRegex(CurrencyPattern);
        });
    }

    [Fact]
    public async Task GetDailyFactsAsync_rows_reference_entities_returned_by_GetEntitiesAsync()
    {
        // The core maps (level, external id) to ad_entities.id; a fact with no entity cannot be stored.
        var source = CreateSource();
        var entities = await source.GetEntitiesAsync(AccountExternalId, CancellationToken.None);
        var keys = entities.Select(e => (e.Level, e.ExternalId)).ToHashSet();

        var facts = await source.GetDailyFactsAsync(AccountExternalId, FixtureDate, CancellationToken.None);

        facts.Should().AllSatisfy(f => keys.Should().Contain((f.Level, f.EntityExternalId)));
    }

    [Fact]
    public async Task GetSearchTermsAsync_honours_the_SearchTerms_capability()
    {
        var source = CreateSource();

        var rows = await source.GetSearchTermsAsync(AccountExternalId, FixtureDate, CancellationToken.None);

        if (source.Capabilities.SearchTerms)
            rows.Should().NotBeEmpty("a source advertising search terms must deliver them for the fixture date");
        else
            rows.Should().BeEmpty("an unsupported capability returns an empty list, never throws");
    }

    [Fact]
    public async Task GetSearchTermsAsync_rows_belong_to_known_ad_groups_and_carry_non_negative_metrics()
    {
        var source = CreateSource();
        var adGroups = (await source.GetEntitiesAsync(AccountExternalId, CancellationToken.None))
            .Where(e => e.Level == AdEntityLevel.AdGroup)
            .Select(e => e.ExternalId)
            .ToHashSet();

        var rows = await source.GetSearchTermsAsync(AccountExternalId, FixtureDate, CancellationToken.None);

        rows.Should().AllSatisfy(r =>
        {
            adGroups.Should().Contain(r.AdGroupExternalId);
            r.Date.Should().Be(FixtureDate);
            r.SearchTerm.Should().NotBeNullOrWhiteSpace();
            if (r.MatchType is { } matchType)
                Enum.IsDefined(matchType).Should().BeTrue();
            r.Impressions.Should().BeGreaterThanOrEqualTo(0);
            r.Clicks.Should().BeGreaterThanOrEqualTo(0);
            r.Cost.Should().BeGreaterThanOrEqualTo(0m);
            r.Conversions.Should().BeGreaterThanOrEqualTo(0m);
            r.ConversionValue.Should().BeGreaterThanOrEqualTo(0m);
            r.Currency.Should().MatchRegex(CurrencyPattern);
        });
    }

    [Fact]
    public async Task GetChangeEventsAsync_honours_the_ChangeLog_capability()
    {
        var source = CreateSource();

        var rows = await source.GetChangeEventsAsync(AccountExternalId, ChangeEventsSince, CancellationToken.None);

        if (source.Capabilities.ChangeLog)
            rows.Should().NotBeEmpty("a source advertising a change log must deliver the fixture's events");
        else
            rows.Should().BeEmpty("an unsupported capability returns an empty list, never throws");
    }

    [Fact]
    public async Task GetChangeEventsAsync_rows_are_unique_well_formed_and_not_older_than_since()
    {
        var rows = await CreateSource().GetChangeEventsAsync(AccountExternalId, ChangeEventsSince, CancellationToken.None);

        rows.Select(r => r.ExternalEventId).Should().OnlyHaveUniqueItems();
        rows.Should().AllSatisfy(r =>
        {
            r.ExternalEventId.Should().NotBeNullOrWhiteSpace();
            r.ChangeType.Should().NotBeNullOrWhiteSpace();
            r.OccurredAt.Should().BeOnOrAfter(ChangeEventsSince);
            Enum.IsDefined(r.ActorKind).Should().BeTrue();
            (r.EntityLevel is null).Should().Be(r.EntityExternalId is null,
                "an entity reference needs both its level and its external id");
            ShouldBeJsonOrNull(r.OldValueJson);
            ShouldBeJsonOrNull(r.NewValueJson);
        });
    }

    private static void ShouldBeJsonOrNull(string? json)
    {
        if (json is null)
            return;

        var parse = () => JsonDocument.Parse(json).Dispose();
        parse.Should().NotThrow("old/new values are stored in jsonb columns");
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.MarketingAds.TestKit"
```
Expected: `Passed!  - Failed:     0, Passed:    37` (2 × 12 inherited contract facts + 7 fake tests + 6 self-tests). A self-test that unexpectedly passes its broken source means the matching contract fact is vacuous — fix the fact, not the self-test.

- [ ] **Step 8: Commit**

```bash
git add Anela.Heblo.sln backend/test/Anela.Heblo.MarketingAds.TestKit \
        backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
        backend/test/Anela.Heblo.Tests/Features/MarketingAds/TestKit
git commit -m "$(cat <<'EOF'
test: add marketing ads test kit with read-source fake and contract suite

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: `FakeAdActionExecutor` and `AdActionExecutorContractTests`

**Files:**
- Create: `backend/test/Anela.Heblo.MarketingAds.TestKit/FakeAdActionExecutor.cs`, `backend/test/Anela.Heblo.MarketingAds.TestKit/AdActionExecutorContractTests.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/TestKit/FakeAdActionExecutorContractTests.cs`, `FakeAdActionExecutorTests.cs`, `AdActionExecutorContractSelfTests.cs`

**Interfaces:**
- Consumes: Task 1 contracts; the test-kit project from Task 5.
- Produces (C3 execution-pipeline tests and WS1–WS3 executor tests use these):
  - `public abstract class AdActionExecutorContractTests` with `protected abstract IAdActionExecutor CreateExecutor()` (a **fresh, stateful** fake transport per call), `protected abstract AdAction SamplePauseAd()`, `protected abstract AdAction? SampleAddNegativeKeyword()` (null when the platform lacks negatives), `public const string MissingTargetExternalId = "heblo-contract-missing-target"` (the platform's fake transport must answer "not found" for it).
  - `public sealed class FakeAdActionExecutor : IAdActionExecutor` — ctor `(AdPlatform platform = AdPlatform.GoogleAds, params AdActionType[] supportedActions)` (empty → all types); chainable `SeedAd(string accountExternalId, string adExternalId, string status = AdActionValues.Enabled)`, `SeedNegativeKeywordTarget(string accountExternalId, AdEntityLevel level, string targetExternalId)`, `RejectNextExecute(string error)` (platform-side rejection → `Failed`), `FailWith(Exception)` (transport failure → throws); `IReadOnlyList<AdAction> ExecutedActions`, `IReadOnlyList<AdAction> RevertedActions` (successful calls only). Created negative keywords get `PlatformResourceId = "fake-negative-{n}"`.

- [ ] **Step 1: Write the failing tests** (namespace `Anela.Heblo.Tests.Features.MarketingAds.TestKit`)

`FakeAdActionExecutorContractTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.MarketingAds.TestKit;

namespace Anela.Heblo.Tests.Features.MarketingAds.TestKit;

public class FakeAdActionExecutorContractTests : AdActionExecutorContractTests
{
    private const string Account = "123-456-7890";

    protected override IAdActionExecutor CreateExecutor() =>
        new FakeAdActionExecutor(AdPlatform.GoogleAds)
            .SeedAd(Account, "ad-1")
            .SeedNegativeKeywordTarget(Account, AdEntityLevel.AdGroup, "adgroup-1");

    protected override AdAction SamplePauseAd() =>
        new(AdActionType.PauseAd, AdPlatform.GoogleAds, Account, AdEntityLevel.Ad, "ad-1",
            AdActionValues.Enabled, AdActionValues.Paused, new Dictionary<string, string>());

    protected override AdAction? SampleAddNegativeKeyword() =>
        new(AdActionType.AddNegativeKeyword, AdPlatform.GoogleAds, Account, AdEntityLevel.AdGroup, "adgroup-1",
            AdActionValues.Absent, AdActionValues.Present,
            new Dictionary<string, string>
            {
                [AdActionPayloadKeys.Text] = "zdarma",
                [AdActionPayloadKeys.MatchType] = nameof(KeywordMatchType.Phrase),
            });
}

/// <summary>Meta-like: PauseAd only, no negative keywords.</summary>
public class FakeAdActionExecutorPauseOnlyContractTests : AdActionExecutorContractTests
{
    private const string Account = "act_1";

    protected override IAdActionExecutor CreateExecutor() =>
        new FakeAdActionExecutor(AdPlatform.MetaAds, AdActionType.PauseAd).SeedAd(Account, "ad-9");

    protected override AdAction SamplePauseAd() =>
        new(AdActionType.PauseAd, AdPlatform.MetaAds, Account, AdEntityLevel.Ad, "ad-9",
            AdActionValues.Enabled, AdActionValues.Paused, new Dictionary<string, string>());

    protected override AdAction? SampleAddNegativeKeyword() => null;
}
```

`FakeAdActionExecutorTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.MarketingAds.TestKit;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.MarketingAds.TestKit;

public class FakeAdActionExecutorTests
{
    private const string Account = "123";

    private static AdAction PauseAd(AdPlatform platform = AdPlatform.GoogleAds) =>
        new(AdActionType.PauseAd, platform, Account, AdEntityLevel.Ad, "ad-1",
            AdActionValues.Enabled, AdActionValues.Paused, new Dictionary<string, string>());

    private static AdAction AddNegative(string text = "zdarma") =>
        new(AdActionType.AddNegativeKeyword, AdPlatform.GoogleAds, Account, AdEntityLevel.Campaign, "campaign-1",
            AdActionValues.Absent, AdActionValues.Present,
            new Dictionary<string, string>
            {
                [AdActionPayloadKeys.Text] = text,
                [AdActionPayloadKeys.MatchType] = nameof(KeywordMatchType.Exact),
            });

    [Fact]
    public async Task RejectNextExecute_returns_Failed_once_and_then_executes_normally()
    {
        var executor = new FakeAdActionExecutor().SeedAd(Account, "ad-1").RejectNextExecute("POLICY_VIOLATION");

        var rejected = await executor.ExecuteAsync(PauseAd(), CancellationToken.None);
        var executed = await executor.ExecuteAsync(PauseAd(), CancellationToken.None);

        rejected.Outcome.Should().Be(AdExecutionOutcome.Failed);
        rejected.Error.Should().Be("POLICY_VIOLATION");
        executed.Outcome.Should().Be(AdExecutionOutcome.Succeeded);
        executor.ExecutedActions.Should().ContainSingle();
    }

    [Fact]
    public async Task FailWith_makes_calls_throw_like_a_transport_failure()
    {
        var executor = new FakeAdActionExecutor().SeedAd(Account, "ad-1").FailWith(new HttpRequestException("timeout"));

        var act = () => executor.ExecuteAsync(PauseAd(), CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
        executor.ExecutedActions.Should().BeEmpty();
    }

    [Fact]
    public async Task An_action_for_another_platform_is_rejected_as_a_programming_error()
    {
        var executor = new FakeAdActionExecutor(AdPlatform.GoogleAds).SeedAd(Account, "ad-1");

        var act = () => executor.ExecuteAsync(PauseAd(AdPlatform.Sklik), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task An_unsupported_action_type_returns_Failed()
    {
        var executor = new FakeAdActionExecutor(AdPlatform.GoogleAds, AdActionType.PauseAd)
            .SeedNegativeKeywordTarget(Account, AdEntityLevel.Campaign, "campaign-1");

        var result = await executor.ExecuteAsync(AddNegative(), CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        result.Error.Should().Contain("not supported");
    }

    [Fact]
    public async Task Reverting_a_failed_execution_returns_Failed_and_changes_nothing()
    {
        var executor = new FakeAdActionExecutor().SeedAd(Account, "ad-1");
        var failed = new AdExecutionResult(AdExecutionOutcome.Failed, null, null, null, null, "boom");

        var result = await executor.RevertAsync(PauseAd(), failed, CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        (await executor.ReadCurrentAsync(PauseAd(), CancellationToken.None)).CurrentValue.Should().Be(AdActionValues.Enabled);
        executor.RevertedActions.Should().BeEmpty();
    }

    [Fact]
    public async Task Adding_the_same_negative_keyword_twice_fails_the_second_time()
    {
        var executor = new FakeAdActionExecutor().SeedNegativeKeywordTarget(Account, AdEntityLevel.Campaign, "campaign-1");

        var first = await executor.ExecuteAsync(AddNegative(), CancellationToken.None);
        var second = await executor.ExecuteAsync(AddNegative(), CancellationToken.None);

        first.Outcome.Should().Be(AdExecutionOutcome.Succeeded);
        first.PlatformResourceId.Should().Be("fake-negative-1");
        second.Outcome.Should().Be(AdExecutionOutcome.Failed);
    }

    [Fact]
    public async Task A_negative_keyword_without_text_in_the_payload_returns_Failed()
    {
        var executor = new FakeAdActionExecutor().SeedNegativeKeywordTarget(Account, AdEntityLevel.Campaign, "campaign-1");

        var result = await executor.ExecuteAsync(AddNegative(text: " "), CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
    }

    [Fact]
    public async Task Successful_execute_and_revert_are_recorded()
    {
        var executor = new FakeAdActionExecutor().SeedAd(Account, "ad-1");

        var executed = await executor.ExecuteAsync(PauseAd(), CancellationToken.None);
        await executor.RevertAsync(PauseAd(), executed, CancellationToken.None);

        executor.ExecutedActions.Should().ContainSingle().Which.TargetExternalId.Should().Be("ad-1");
        executor.RevertedActions.Should().ContainSingle();
    }
}
```

`AdActionExecutorContractSelfTests.cs` — broken executors must fail the inherited facts (private nested classes are not discovered by xUnit):

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.MarketingAds.TestKit;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.MarketingAds.TestKit;

public class AdActionExecutorContractSelfTests
{
    private const string Account = "123";

    [Fact]
    public async Task Contract_fails_an_executor_that_throws_on_a_missing_target()
    {
        var suite = new Suite(() => new ThrowingOnMissingTarget(NewFake()));

        var act = () => suite.Execute_on_a_missing_target_returns_Failed_instead_of_throwing();

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Contract_fails_an_executor_whose_revert_does_not_restore_the_ad()
    {
        var suite = new Suite(() => new RevertDoesNothing(NewFake()));

        var act = () => suite.PauseAd_Revert_restores_the_value_ReadCurrent_reported_before_Execute();

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Contract_fails_an_executor_that_does_not_return_a_resource_id_for_a_new_negative_keyword()
    {
        var suite = new Suite(() => new NoResourceId(NewFake()));

        var act = () => suite.AddNegativeKeyword_Execute_creates_the_criterion_and_returns_its_resource_id();

        await act.Should().ThrowAsync<Exception>();
    }

    private static FakeAdActionExecutor NewFake() =>
        new FakeAdActionExecutor(AdPlatform.GoogleAds)
            .SeedAd(Account, "ad-1")
            .SeedNegativeKeywordTarget(Account, AdEntityLevel.AdGroup, "adgroup-1");

    private sealed class Suite(Func<IAdActionExecutor> create) : AdActionExecutorContractTests
    {
        protected override IAdActionExecutor CreateExecutor() => create();

        protected override AdAction SamplePauseAd() =>
            new(AdActionType.PauseAd, AdPlatform.GoogleAds, Account, AdEntityLevel.Ad, "ad-1",
                AdActionValues.Enabled, AdActionValues.Paused, new Dictionary<string, string>());

        protected override AdAction? SampleAddNegativeKeyword() =>
            new(AdActionType.AddNegativeKeyword, AdPlatform.GoogleAds, Account, AdEntityLevel.AdGroup, "adgroup-1",
                AdActionValues.Absent, AdActionValues.Present,
                new Dictionary<string, string>
                {
                    [AdActionPayloadKeys.Text] = "zdarma",
                    [AdActionPayloadKeys.MatchType] = nameof(KeywordMatchType.Exact),
                });
    }

    private abstract class Decorator(IAdActionExecutor inner) : IAdActionExecutor
    {
        public AdPlatform Platform => inner.Platform;
        public IReadOnlySet<AdActionType> SupportedActions => inner.SupportedActions;
        public virtual Task<AdTargetState> ReadCurrentAsync(AdAction action, CancellationToken ct) => inner.ReadCurrentAsync(action, ct);
        public virtual Task<AdExecutionResult> ExecuteAsync(AdAction action, CancellationToken ct) => inner.ExecuteAsync(action, ct);
        public virtual Task<AdExecutionResult> RevertAsync(AdAction action, AdExecutionResult original, CancellationToken ct) =>
            inner.RevertAsync(action, original, ct);
    }

    private sealed class ThrowingOnMissingTarget(IAdActionExecutor inner) : Decorator(inner)
    {
        public override Task<AdExecutionResult> ExecuteAsync(AdAction action, CancellationToken ct) =>
            action.TargetExternalId == AdActionExecutorContractTests.MissingTargetExternalId
                ? throw new InvalidOperationException("404 from the platform")
                : base.ExecuteAsync(action, ct);
    }

    private sealed class RevertDoesNothing(IAdActionExecutor inner) : Decorator(inner)
    {
        public override Task<AdExecutionResult> RevertAsync(AdAction action, AdExecutionResult original, CancellationToken ct) =>
            Task.FromResult(new AdExecutionResult(AdExecutionOutcome.Succeeded, null, null, null, null, null));
    }

    private sealed class NoResourceId(IAdActionExecutor inner) : Decorator(inner)
    {
        public override async Task<AdExecutionResult> ExecuteAsync(AdAction action, CancellationToken ct) =>
            (await base.ExecuteAsync(action, ct)) with { PlatformResourceId = null };
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false 2>&1 | grep -E "error CS" | head -3`
Expected: `error CS0246: The type or namespace name 'AdActionExecutorContractTests' could not be found`.

- [ ] **Step 3: Implement `FakeAdActionExecutor.cs`**

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.MarketingAds.TestKit;

/// <summary>
/// In-memory IAdActionExecutor for the core's execution pipeline (C3) and the executor contract
/// self-tests. Holds ad statuses and negative keywords per account; never talks to a platform.
/// Follows spec 12.2: platform-side rejections come back as Failed; only <see cref="FailWith"/>
/// (a simulated transport/auth failure) and a wrong-platform action (a programming error) throw.
/// It does not compare OldValue itself — the core does.
/// </summary>
public sealed class FakeAdActionExecutor : IAdActionExecutor
{
    private readonly Dictionary<AdKey, string> _adStatuses = new();
    private readonly HashSet<TargetKey> _negativeKeywordTargets = new();
    private readonly Dictionary<string, NegativeKeyword> _negativeKeywords = new(StringComparer.Ordinal);
    private readonly List<AdAction> _executedActions = [];
    private readonly List<AdAction> _revertedActions = [];
    private int _nextResourceNumber = 1;
    private string? _nextExecuteRejection;
    private Exception? _failure;

    public FakeAdActionExecutor(AdPlatform platform = AdPlatform.GoogleAds, params AdActionType[] supportedActions)
    {
        Platform = platform;
        SupportedActions = (supportedActions.Length == 0 ? Enum.GetValues<AdActionType>() : supportedActions).ToHashSet();
    }

    public AdPlatform Platform { get; }
    public IReadOnlySet<AdActionType> SupportedActions { get; }
    public IReadOnlyList<AdAction> ExecutedActions => _executedActions;
    public IReadOnlyList<AdAction> RevertedActions => _revertedActions;

    public FakeAdActionExecutor SeedAd(string accountExternalId, string adExternalId, string status = AdActionValues.Enabled)
    {
        _adStatuses[new AdKey(accountExternalId, adExternalId)] = status;
        return this;
    }

    public FakeAdActionExecutor SeedNegativeKeywordTarget(string accountExternalId, AdEntityLevel level, string targetExternalId)
    {
        _negativeKeywordTargets.Add(new TargetKey(accountExternalId, level, targetExternalId));
        return this;
    }

    public FakeAdActionExecutor RejectNextExecute(string error)
    {
        _nextExecuteRejection = error;
        return this;
    }

    public FakeAdActionExecutor FailWith(Exception failure)
    {
        _failure = failure;
        return this;
    }

    public Task<AdTargetState> ReadCurrentAsync(AdAction action, CancellationToken ct)
    {
        Guard(action, ct);
        var state = action.Type switch
        {
            AdActionType.PauseAd => ReadAd(action),
            AdActionType.AddNegativeKeyword => ReadNegativeKeyword(action),
            _ => new AdTargetState(false, null, null),
        };
        return Task.FromResult(state);
    }

    public Task<AdExecutionResult> ExecuteAsync(AdAction action, CancellationToken ct)
    {
        Guard(action, ct);
        if (!SupportedActions.Contains(action.Type))
            return Task.FromResult(Failed($"Action {action.Type} is not supported by this executor."));

        if (_nextExecuteRejection is { } rejection)
        {
            _nextExecuteRejection = null;
            return Task.FromResult(Failed(rejection));
        }

        var result = action.Type == AdActionType.PauseAd ? PauseAd(action) : AddNegativeKeyword(action);
        if (result.Outcome == AdExecutionOutcome.Succeeded)
            _executedActions.Add(action);
        return Task.FromResult(result);
    }

    public Task<AdExecutionResult> RevertAsync(AdAction action, AdExecutionResult original, CancellationToken ct)
    {
        Guard(action, ct);
        if (original.Outcome != AdExecutionOutcome.Succeeded)
            return Task.FromResult(Failed("Only a succeeded execution can be reverted."));

        var result = action.Type == AdActionType.PauseAd ? RestoreAd(action, original) : RemoveNegativeKeyword(original);
        if (result.Outcome == AdExecutionOutcome.Succeeded)
            _revertedActions.Add(action);
        return Task.FromResult(result);
    }

    private void Guard(AdAction action, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_failure is not null)
            throw _failure;
        if (action.Platform != Platform)
            throw new ArgumentException($"A {action.Platform} action was sent to the {Platform} executor.", nameof(action));
    }

    private AdTargetState ReadAd(AdAction action) =>
        _adStatuses.TryGetValue(AdKeyOf(action), out var status)
            ? new AdTargetState(true, status, JsonSerializer.Serialize(new { adId = action.TargetExternalId, status }))
            : new AdTargetState(false, null, null);

    private AdTargetState ReadNegativeKeyword(AdAction action)
    {
        if (!_negativeKeywordTargets.Contains(TargetKeyOf(action)))
            return new AdTargetState(false, null, null);

        var current = FindNegativeKeyword(action) is null ? AdActionValues.Absent : AdActionValues.Present;
        return new AdTargetState(true, current, null);
    }

    private AdExecutionResult PauseAd(AdAction action)
    {
        var key = AdKeyOf(action);
        if (!_adStatuses.TryGetValue(key, out var before))
            return Failed($"Ad {action.TargetExternalId} does not exist in account {action.AccountExternalId}.");

        _adStatuses[key] = AdActionValues.Paused;
        return Succeeded(before, AdActionValues.Paused, action.TargetExternalId);
    }

    private AdExecutionResult RestoreAd(AdAction action, AdExecutionResult original)
    {
        var key = AdKeyOf(action);
        if (!_adStatuses.TryGetValue(key, out var current))
            return Failed($"Ad {action.TargetExternalId} does not exist in account {action.AccountExternalId}.");

        var restored = original.BeforeValue ?? AdActionValues.Enabled;
        _adStatuses[key] = restored;
        return Succeeded(current, restored, action.TargetExternalId);
    }

    private AdExecutionResult AddNegativeKeyword(AdAction action)
    {
        if (!_negativeKeywordTargets.Contains(TargetKeyOf(action)))
            return Failed($"{action.TargetLevel} {action.TargetExternalId} does not exist in account {action.AccountExternalId}.");
        if (!TryReadKeyword(action, out var text, out var matchType))
            return Failed("The payload must carry non-empty 'text' and 'matchType'.");
        if (FindNegativeKeyword(action) is not null)
            return Failed($"Negative keyword '{text}' ({matchType}) already exists.");

        var resourceId = $"fake-negative-{_nextResourceNumber++}";
        _negativeKeywords[resourceId] = new NegativeKeyword(TargetKeyOf(action), text, matchType);
        return Succeeded(AdActionValues.Absent, AdActionValues.Present, resourceId);
    }

    private AdExecutionResult RemoveNegativeKeyword(AdExecutionResult original)
    {
        if (original.PlatformResourceId is null || !_negativeKeywords.Remove(original.PlatformResourceId))
            return Failed($"Negative keyword {original.PlatformResourceId} does not exist.");

        return Succeeded(AdActionValues.Present, AdActionValues.Absent, original.PlatformResourceId);
    }

    private string? FindNegativeKeyword(AdAction action)
    {
        if (!TryReadKeyword(action, out var text, out var matchType))
            return null;

        var wanted = new NegativeKeyword(TargetKeyOf(action), text, matchType);
        return _negativeKeywords.FirstOrDefault(entry => entry.Value == wanted).Key;
    }

    private static bool TryReadKeyword(AdAction action, out string text, out string matchType)
    {
        action.Payload.TryGetValue(AdActionPayloadKeys.Text, out var rawText);
        action.Payload.TryGetValue(AdActionPayloadKeys.MatchType, out var rawMatchType);
        text = rawText?.Trim() ?? "";
        matchType = rawMatchType?.Trim() ?? "";
        return text.Length > 0 && matchType.Length > 0;
    }

    private static AdKey AdKeyOf(AdAction action) => new(action.AccountExternalId, action.TargetExternalId);

    private static TargetKey TargetKeyOf(AdAction action) =>
        new(action.AccountExternalId, action.TargetLevel, action.TargetExternalId);

    private static AdExecutionResult Succeeded(string before, string after, string resourceId) =>
        new(AdExecutionOutcome.Succeeded, before, after, resourceId,
            JsonSerializer.Serialize(new { resourceId, before, after }), null);

    private static AdExecutionResult Failed(string error) =>
        new(AdExecutionOutcome.Failed, null, null, null, null, error);

    private sealed record AdKey(string Account, string AdId);

    private sealed record TargetKey(string Account, AdEntityLevel Level, string TargetId);

    private sealed record NegativeKeyword(TargetKey Target, string Text, string MatchType);
}
```

- [ ] **Step 4: Implement `AdActionExecutorContractTests.cs`**

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;

namespace Anela.Heblo.MarketingAds.TestKit;

/// <summary>
/// Cross-platform semantics every IAdActionExecutor must satisfy (spec 4.3, 4.4, 12.2, 12.3). A
/// platform test project derives from this class with CreateExecutor() backed by a stateful fake
/// transport (a fresh one per call) that knows the sample targets and answers "not found" for
/// <see cref="MissingTargetExternalId"/>. Never override or skip a fact.
/// </summary>
public abstract class AdActionExecutorContractTests
{
    public const string MissingTargetExternalId = "heblo-contract-missing-target";

    protected abstract IAdActionExecutor CreateExecutor();
    protected abstract AdAction SamplePauseAd();
    protected abstract AdAction? SampleAddNegativeKeyword();

    [Fact]
    public void Platform_is_defined_and_SupportedActions_match_the_samples()
    {
        var executor = CreateExecutor();

        Enum.IsDefined(executor.Platform).Should().BeTrue();
        executor.SupportedActions.Should().Contain(AdActionType.PauseAd);
        executor.SupportedActions.Contains(AdActionType.AddNegativeKeyword)
            .Should().Be(SampleAddNegativeKeyword() is not null,
                "a platform supports AddNegativeKeyword exactly when it provides a sample for it");
    }

    [Fact]
    public void SamplePauseAd_follows_the_PauseAd_conventions()
    {
        var executor = CreateExecutor();
        var action = SamplePauseAd();

        action.Type.Should().Be(AdActionType.PauseAd);
        action.Platform.Should().Be(executor.Platform);
        action.AccountExternalId.Should().NotBeNullOrWhiteSpace();
        action.TargetLevel.Should().Be(AdEntityLevel.Ad);
        action.TargetExternalId.Should().NotBeNullOrWhiteSpace();
        action.TargetExternalId.Should().NotBe(MissingTargetExternalId);
        action.OldValue.Should().Be(AdActionValues.Enabled);
        action.NewValue.Should().Be(AdActionValues.Paused);
        action.Payload.Count.Should().Be(0);
    }

    [Fact]
    public void SampleAddNegativeKeyword_follows_the_AddNegativeKeyword_conventions()
    {
        var action = SampleAddNegativeKeyword();
        if (action is null)
            return; // the platform has no negative keywords (Meta)

        action.Type.Should().Be(AdActionType.AddNegativeKeyword);
        action.Platform.Should().Be(CreateExecutor().Platform);
        new[] { AdEntityLevel.Campaign, AdEntityLevel.AdGroup }.Should().Contain(action.TargetLevel);
        action.OldValue.Should().Be(AdActionValues.Absent);
        action.NewValue.Should().Be(AdActionValues.Present);
        action.Payload.TryGetValue(AdActionPayloadKeys.Text, out var text).Should().BeTrue();
        text.Should().NotBeNullOrWhiteSpace();
        action.Payload.TryGetValue(AdActionPayloadKeys.MatchType, out var matchType).Should().BeTrue();
        Enum.GetNames<KeywordMatchType>().Should().Contain(matchType);
    }

    [Fact]
    public async Task PauseAd_ReadCurrent_reports_an_existing_enabled_ad_before_Execute()
    {
        var state = await CreateExecutor().ReadCurrentAsync(SamplePauseAd(), CancellationToken.None);

        state.Exists.Should().BeTrue();
        state.CurrentValue.Should().Be(AdActionValues.Enabled);
    }

    [Fact]
    public async Task PauseAd_Execute_pauses_the_ad_and_reports_the_before_and_after_values()
    {
        var executor = CreateExecutor();
        var action = SamplePauseAd();

        var result = await executor.ExecuteAsync(action, CancellationToken.None);
        var after = await executor.ReadCurrentAsync(action, CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Succeeded, "the executor reported '{0}'", result.Error);
        result.BeforeValue.Should().Be(AdActionValues.Enabled);
        result.AfterValue.Should().Be(AdActionValues.Paused);
        result.Error.Should().BeNull();
        after.CurrentValue.Should().Be(AdActionValues.Paused);
    }

    [Fact]
    public async Task PauseAd_Revert_restores_the_value_ReadCurrent_reported_before_Execute()
    {
        var executor = CreateExecutor();
        var action = SamplePauseAd();
        var before = await executor.ReadCurrentAsync(action, CancellationToken.None);

        var executed = await executor.ExecuteAsync(action, CancellationToken.None);
        var reverted = await executor.RevertAsync(action, executed, CancellationToken.None);
        var after = await executor.ReadCurrentAsync(action, CancellationToken.None);

        reverted.Outcome.Should().Be(AdExecutionOutcome.Succeeded, "the executor reported '{0}'", reverted.Error);
        after.CurrentValue.Should().Be(before.CurrentValue);
    }

    [Fact]
    public async Task AddNegativeKeyword_ReadCurrent_reports_Absent_before_Execute()
    {
        var action = SampleAddNegativeKeyword();
        if (action is null)
            return;

        var state = await CreateExecutor().ReadCurrentAsync(action, CancellationToken.None);

        state.Exists.Should().BeTrue();
        state.CurrentValue.Should().Be(AdActionValues.Absent);
    }

    [Fact]
    public async Task AddNegativeKeyword_Execute_creates_the_criterion_and_returns_its_resource_id()
    {
        var action = SampleAddNegativeKeyword();
        if (action is null)
            return;
        var executor = CreateExecutor();

        var result = await executor.ExecuteAsync(action, CancellationToken.None);
        var after = await executor.ReadCurrentAsync(action, CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Succeeded, "the executor reported '{0}'", result.Error);
        result.BeforeValue.Should().Be(AdActionValues.Absent);
        result.AfterValue.Should().Be(AdActionValues.Present);
        result.PlatformResourceId.Should().NotBeNullOrWhiteSpace("revert removes the criterion by this id");
        after.CurrentValue.Should().Be(AdActionValues.Present);
    }

    [Fact]
    public async Task AddNegativeKeyword_Revert_restores_the_value_ReadCurrent_reported_before_Execute()
    {
        var action = SampleAddNegativeKeyword();
        if (action is null)
            return;
        var executor = CreateExecutor();
        var before = await executor.ReadCurrentAsync(action, CancellationToken.None);

        var executed = await executor.ExecuteAsync(action, CancellationToken.None);
        var reverted = await executor.RevertAsync(action, executed, CancellationToken.None);
        var after = await executor.ReadCurrentAsync(action, CancellationToken.None);

        reverted.Outcome.Should().Be(AdExecutionOutcome.Succeeded, "the executor reported '{0}'", reverted.Error);
        after.CurrentValue.Should().Be(before.CurrentValue);
    }

    [Fact]
    public async Task ReadCurrent_on_a_missing_target_reports_that_it_does_not_exist()
    {
        var action = SamplePauseAd() with { TargetExternalId = MissingTargetExternalId };

        var state = await CreateExecutor().ReadCurrentAsync(action, CancellationToken.None);

        state.Exists.Should().BeFalse();
    }

    [Fact]
    public async Task Execute_on_a_missing_target_returns_Failed_instead_of_throwing()
    {
        var action = SamplePauseAd() with { TargetExternalId = MissingTargetExternalId };

        var result = await CreateExecutor().ExecuteAsync(action, CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        result.Error.Should().NotBeNullOrWhiteSpace();
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.MarketingAds.TestKit"
```
Expected: `Passed!  - Failed:     0, Passed:    70` (Task 5's 37 + 2 × 11 inherited executor facts + 8 fake tests + 3 self-tests).

- [ ] **Step 6: Commit**

```bash
git add backend/test/Anela.Heblo.MarketingAds.TestKit \
        backend/test/Anela.Heblo.Tests/Features/MarketingAds/TestKit
git commit -m "$(cat <<'EOF'
test: add action-executor fake and contract suite to the marketing ads test kit

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: ADR-008 and the Metabase schema table

**Files:**
- Modify: `docs/architecture/development_guidelines.md` (append after ADR-007, before the `---` that precedes `## ⚠️ Common Pitfalls to Avoid`)
- Modify: `docs/architecture/metabase.md` (table under `## Reporting schemas in \`Heblo_V3\``)

**Interfaces:**
- Consumes: the names fixed in Tasks 1–4.
- Produces: the recorded decision later PRs cite (C2 views/grants, C3 proposal tables in `public`).

- [ ] **Step 1: Insert ADR-008.** Find the end of ADR-007:

```markdown
- **Supersedes**: `docs/superpowers/plans/2026-05-20-flexi-analytics-sync.md`, whose separate-database
  premise is the thing being reversed.
```

and insert directly below it (one blank line before, keeping the existing `---` after):

```markdown

### ADR-008: Ad-Platform Data Shares One `ads` Schema and May Be Read by MediatR Handlers
- **Status**: Accepted (2026-10-07)
- **Context**: The marketing agents platform
  (`docs/superpowers/specs/2026-10-07-marketing-agents-platform-design.md`) makes Heblo "the eyes and
  the hands" of AI marketing agents running outside Heblo. It syncs Google Ads, Meta Ads and Sklik
  at management granularity (campaign → ad group → keyword / search term → ad, daily), records every
  change made to the accounts, and lets agents read that data over MCP to propose changes. ADR-007
  says reporting data lands in `Heblo_V3` with **one schema per source** and that **no MediatR
  handlers** are written for it. Applied literally that would mean three ad schemas with three
  shapes, and agents unable to read the data they are meant to manage.
- **Decision**:
  1. **One `ads` schema for all three ad platforms**, not one per platform. The backbone's value is a
     single normalised model (`ad_accounts`, `ad_entities`, `ad_daily_facts`, `ad_search_term_daily`,
     `ad_change_events`, `sync_state`) that an agent queries the same way for every platform and that
     the limits engine can reason about. Platform-specific fields go to the `jsonb` `attributes`
     column. The schema is owned by `AdsDbContext` in `Anela.Heblo.Persistence.Ads`, following the
     ADR-007 mechanics unchanged: own keyed `NpgsqlDataSource` (`"ads"`), own Polly pipeline,
     `MigrationsHistoryTable` pinned to `ads` on the runtime **and** the design-time path, manual
     migrations, and registration gated on `AdsDatabase:ConnectionString` passing
     `AdSettingsGuard.IsConfigured` and parsing as an Npgsql connection string with a Host.
  2. **MediatR read handlers over `ads` are allowed**, because agents consume them through MCP to
     *manage* the accounts. This is operational use, not reporting. Human-facing performance
     *reports* still live in Metabase only; the Heblo UI shows proposals, audit and settings, never
     charts.
  3. **Operational workflow data does not go to `ads`.** Proposals, versions, approvals, the audit
     log, agent runs, autonomy settings, limits and the kill switch live in `public` via
     `ApplicationDbContext`.
- **Consequences**:
  - Every query over `ad_daily_facts` must aggregate a **single** `level`; facts are stored at every
    level a platform reports, so summing across levels double-counts.
  - Enum-valued columns (`platform`, `level`, `status`, `actor_kind`, `match_type`) store the
    contract enum **name** as text (`GoogleAds`, `AdGroup`, …): `Persistence.Ads` cannot reference
    the Application contracts, and names keep the Metabase views readable.
  - Every `DateTimeOffset` in `ads` is normalised to UTC by `UtcDateTimeOffsetConverter`; platforms
    report local offsets, which Npgsql otherwise refuses to write to `timestamptz`.
  - Metabase reads only `v_ads_*` views granted to `metabase_ro` (added with the sync, PR C2); the raw
    tables are never granted. `ads` holds no customer PII.
  - `AdsDatabase--ConnectionString` duplicates `ConnectionStrings--Production` /
    `ConnectionStrings--Staging` in Key Vault, like `AnalyticsDatabase--ConnectionString`; the
    secrets must be rotated together.
  - Moving `ads` to its own database later stays a connection-string change.
- **Deviates from**: ADR-007 decision points "one schema per source" and "no MediatR handlers", for ad
  platforms only. ADR-007 stays in force for every other reporting source.
```

- [ ] **Step 2: Add the `ads` row to `docs/architecture/metabase.md`.** In the table under `## Reporting schemas in \`Heblo_V3\``, after the `ga4_agg` row, add:

```markdown
| `ads` | Google Ads, Meta Ads, Sklik — entities, daily facts, search terms, change history | `AdsDbContext` (ADR-008); views and grants arrive with the sync (PR C2) |
```

- [ ] **Step 3: Verify**

```bash
grep -n "^### ADR-00[78]" docs/architecture/development_guidelines.md
grep -n '^| `ads`' docs/architecture/metabase.md
```
Expected: ADR-007 and ADR-008 headings, in that order, both above `## ⚠️ Common Pitfalls`; one `ads` row.

- [ ] **Step 4: Commit**

```bash
git add docs/architecture/development_guidelines.md docs/architecture/metabase.md
git commit -m "$(cat <<'EOF'
docs: add ADR-008 for the shared ads schema

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 8: Full verification, format, PR

**Files:** none new (format fixes only).

**Interfaces:** consumes everything; produces the PR.

- [ ] **Step 1: Build the whole solution**

```bash
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
```
Expected: `Build succeeded.` with `0 Error(s)`. No new warnings in `Persistence.Ads`, `MarketingAds` or the test kit (`dotnet build … 2>&1 | grep -E "warning" | grep -E "Persistence.Ads|MarketingAds"` prints nothing).

- [ ] **Step 2: Format**

```bash
dotnet format Anela.Heblo.sln --include \
  backend/src/Anela.Heblo.Persistence.Ads \
  backend/src/Anela.Heblo.Application/Features/MarketingAds \
  backend/src/Anela.Heblo.Application/ApplicationModule.cs \
  backend/test/Anela.Heblo.MarketingAds.TestKit \
  backend/test/Anela.Heblo.Tests/Features/MarketingAds \
  backend/test/Anela.Heblo.Tests/Persistence/MarketingAds
git status --short
```
If files changed, rebuild (Step 1) and commit them: `git commit -am` is not safe with untracked files, so `git add` the listed paths, then commit as `style: dotnet format marketing ads core` with the Co-Authored-By trailer.

- [ ] **Step 3: Run the unit suite the way CI does** (excludes Integration and Playwright)

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build \
  --filter "Category!=Playwright&Category!=Integration"
```
Expected: `Failed: 0`. This covers the new tests plus the guards that could trip on new code: `ModuleBoundariesTests`, `ErrorHandlingTests` (no new `*Response` types were added), `ReflectionValidationTests`, startup tests.

- [ ] **Step 4: Run the integration tests** (needs Podman)

```bash
podman machine start
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Persistence.MarketingAds"
```
Expected: `Passed!  - Failed:     0, Passed:    11`. If Podman cannot start, say so in the PR description instead of claiming the integration tests passed.

- [ ] **Step 5: Confirm nothing out of scope slipped in**

```bash
git diff --stat origin/main...HEAD
git diff origin/main...HEAD --name-only | grep -E "Program.cs|frontend/|Hangfire|IRecurringJob|MCP/" || echo "scope ok"
```
Expected: `scope ok`; the stat lists only the files in *File Structure*.

- [ ] **Step 6: Push and open the PR**

```bash
git push -u origin feature/marketing-core-c1-contracts
gh pr create --base main --title "feat: marketing ads core contracts, ads schema and test kit (C1)" --body "$(cat <<'EOF'
## Summary
Core PR **C1** of the marketing agents platform (spec `docs/superpowers/specs/2026-10-07-marketing-agents-platform-design.md`, sections 2.2, 3, 4, 12).

- Contract types in `Application/Features/MarketingAds/Contracts` exactly as spec 12.2 (`AdPlatform`, value records, `IAdPlatformReadSource`, `IAdActionExecutor`, `AdAction`, `AdSettingsGuard`, …).
- New `Anela.Heblo.Persistence.Ads`: `AdsDbContext` (schema `ads`, own data source + Polly pipeline, history table pinned on runtime and design-time paths), six tables, migration `InitialAdsSchema`.
- `MarketingAdsModule` registers the schema only when `AdsDatabase:ConnectionString` is real and parseable; otherwise the environment stays inert.
- New `Anela.Heblo.MarketingAds.TestKit`: `FakeAdPlatformReadSource`, `FakeAdActionExecutor`, abstract `AdPlatformReadSourceContractTests` / `AdActionExecutorContractTests` (run here against the fakes, with self-tests proving they catch broken implementations).
- ADR-008 in `development_guidelines.md`; `ads` row in `metabase.md`.

No sync jobs, proposals, MCP tools or UI (C2–C5). Nothing reads the schema yet, so no process doc.

## Rollout (manual, after merge — can wait until C2)
1. `az keyvault secret set --vault-name kv-heblo-stg --name "AdsDatabase--ConnectionString" --value "<value of ConnectionStrings--Staging>"`
2. `dotnet ef migrations script --idempotent --project backend/src/Anela.Heblo.Persistence.Ads/Anela.Heblo.Persistence.Ads.csproj --startup-project backend/src/Anela.Heblo.Persistence.Ads/Anela.Heblo.Persistence.Ads.csproj --context AdsDbContext --output ads_schema.sql`, then `psql "$CONNECTION_STRING" -v ON_ERROR_STOP=1 -f ads_schema.sql` against `Heblo_TST`.
3. Restart `heblo-test`; repeat for `kv-heblo-prod` / `Heblo_V3` / `heblo` after staging is verified.

## Test plan
- [ ] `dotnet build Anela.Heblo.sln`
- [ ] Unit suite (`Category!=Playwright&Category!=Integration`) green
- [ ] `Persistence.MarketingAds` integration tests green on Postgres (Podman)
- [ ] Platform workspaces (WS1–WS3) can inherit the contract suites (xUnit 2.9.2)

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 7: Check CI once it registers** — `gh pr checks` right after `create` can print "no checks reported" before the runs register; wait a minute and re-run `gh pr checks --watch`. BE, FE and Docker run on every PR.

---

## Rollout (manual, not part of the executor's work)

Nothing reads `ads` until C2, so applying the schema can wait. When it happens: Key Vault secret `AdsDatabase--ConnectionString` (value = the environment's main connection string) in `kv-heblo-stg`, then `kv-heblo-prod`; idempotent script applied with `psql`; restart the Web App (Key Vault is read at startup). Never set it in App Service settings.

---

## Spec deviations and decisions

1. **Registration lives in `MarketingAdsModule` (Application), called from `ApplicationModule`; `Program.cs` is untouched.** That is how every feature module is wired. GA4 registers its persistence from its adapter and reuses `ConnectionStrings:{Environment}`; the spec (12.1) fixes a dedicated `AdsDatabase:ConnectionString`, so the gate copies `ShoptetOrdersAnalyticsServiceCollectionExtensions` (blank/placeholder check + Npgsql parse check + stderr warning) rather than GA4's.
2. **Enum-valued columns are text holding the contract enum name.** `Persistence.Ads` cannot reference `Application` (Application references it — a cycle), so entities use `string`; C2 maps with `Enum.ToString()` / `Enum.Parse`.
3. **`ad_change_events.matched_execution_id` is `text`.** It points at the C3 execution record in `public`; spec 12 does not fix that record's id type, and no cross-context FK is possible. C2 writes `id.ToString()`.
4. **`ad_search_term_daily.match_type` is NOT NULL with `"Unknown"`** when the platform reports none — it is part of the primary key, while `AdSearchTermRow.MatchType` is nullable.
5. **Columns beyond the spec's "key columns":** `created_at`/`updated_at` on `ad_accounts`, `synced_at` on facts/search terms/change events, `updated_at` on `sync_state`.
6. **`UtcDateTimeOffsetConverter` on every `DateTimeOffset`** — not in the spec; needed because Npgsql rejects non-UTC offsets for `timestamptz`.
7. **Additive members on the 12.3 bases:** `AdPlatformReadSourceContractTests.ChangeEventsLookbackDays` (7) and `ChangeEventsSince`; `AdActionExecutorContractTests.MissingTargetExternalId`. The abstract members are exactly as specified.
8. **Contract pins beyond the spec's list** (all derivable from 4.1/4.2/12.2): external ids unique per level, parents present in the same snapshot, facts/search terms reference returned entities, change values are JSON, missing targets → `Exists=false` / `Failed` without throwing.
9. **`AdSettingsGuard.IsConfigured()` with no arguments returns false.** Spec is silent; "nothing to check" must not read as "configured". Per spec, any value containing `XXX` counts as a placeholder — a real secret containing `XXX` would read as unconfigured.
10. **The test kit pins xUnit 2.9.2**; platform test projects must use it (the GA4 test project's 2.5.3 would not resolve against the kit).
11. **Tests live in `Anela.Heblo.Tests`** (`Features/MarketingAds/`, `Persistence/MarketingAds/`); the spec names no project for core persistence tests.
