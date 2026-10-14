# Marketing Core C3 — Proposals, Approval, Limits, Execution, Audit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give Heblo a versioned, append-only-audited proposal & approval layer for ad-account changes: agents/people submit structured `AdAction` proposals, a pure limits engine and autonomy settings decide what may be approved through which channel, approved proposals execute sequentially through the platform `IAdActionExecutor`s behind a persisted kill switch, and every write is revertible from stored before-state.

**Architecture:** Entities live in `Anela.Heblo.Domain/Features/MarketingAds/` (public schema, `ApplicationDbContext`), EF configuration + repositories in `Anela.Heblo.Persistence/MarketingAds/`, the vertical slice (handlers, limits engine, execution job, recurring expiry job) in `Anela.Heblo.Application/Features/MarketingAds/Proposals/`, REST controllers in `Anela.Heblo.API/Controllers/`. The approval *channel* (Web/Mcp/System) is resolved by `IApprovalChannelResolver`; C3 registers a deny-all default so nothing can be approved until PR C4 ships the claims-based resolver. Append-only rules on audit and versions are enforced by a Postgres trigger.

**Tech Stack:** .NET 8, EF Core 8 + Npgsql, MediatR 12, Hangfire 1.8, xUnit + FluentAssertions + Moq, Testcontainers.PostgreSql (postgres:16), `Microsoft.Extensions.Time.Testing.FakeTimeProvider`.

**Spec:** `docs/superpowers/specs/2026-10-07-marketing-agents-platform-design.md` (sections 4.3, 6, 10, 12 are binding; section 12 names are binding) and its context `docs/handoff/marketing-agents-platform.md` (sections 2 and 3 binding).

## Global Constraints

- Contract types come from PR C1, namespace `Anela.Heblo.Application.Features.MarketingAds.Contracts`: `AdPlatform { GoogleAds = 1, MetaAds = 2, Sklik = 3 }`, `AdEntityLevel`, `AdEntityStatus`, `KeywordMatchType`, `AdActionType { AddNegativeKeyword = 1, PauseAd = 2 }`, `AdExecutionOutcome { Succeeded = 1, Failed = 2, StaleState = 3 }`, records `AdAction`, `AdTargetState`, `AdExecutionResult`, constants `AdActionValues` (`Absent`, `Present`, `Enabled`, `Paused`), `AdActionPayloadKeys` (`text`, `matchType`), `AdSettingsGuard`, interface `IAdActionExecutor`. Never redefine them.
- Action conventions (spec 12.2): `AddNegativeKeyword` → `TargetLevel` Campaign|AdGroup, `OldValue = Absent`, `NewValue = Present`, payload `text` + `matchType` (enum name). `PauseAd` → `TargetLevel = Ad`, `OldValue = Enabled`, `NewValue = Paused`, empty payload. Executors never compare old values — the core does.
- Proposal: 1–50 actions of a single type and single account; `expires_at = created + 72 h` (`Ads:ProposalTtlHours`). Versions are immutable; a revision inserts a new version and voids earlier approvals.
- Approve/reject must name the **current** version, otherwise HTTP 409.
- Limits defaults (spec 6.3): `AddNegativeKeyword ≤ 50`, `PauseAd ≤ 10` per proposal; ≤ 20 paused ads per account per rolling 7 days; MCP may approve ≤ 20 negatives / ≤ 3 paused ads per proposal, above that `NeedsWebApproval`; pausing an ad that empties its ad group → `NeedsWebApproval`; over the second-approver threshold → `NeedsSecondApprover` (a different principal on the same version).
- Autonomy seeded `ProposeOnly` everywhere. Kill switch default after deployment: **execution disabled**.
- Audit is append-only (Postgres trigger rejects `UPDATE`/`DELETE`, covered by an integration test). Events: `Submitted`, `Revised`, `Approved`, `Rejected`, `Expired`, `ExecutionStarted`, `ActionExecuted`, `ActionFailed`, `Reverted`, `SettingChanged`, `KillSwitchChanged`; channel `Web | Mcp | System`.
- Revert: web UI only, `Marketing_AdApprovals` Admin, uses stored before-state per action.
- Human-readable diff is rendered from `AdAction`s, never from `reasoning`.
- DTOs are classes, never records. Every Application `*Response` inherits `BaseResponse`.
- All `DateTime` columns are `timestamp` (without time zone) via `AsUtcTimestamp()`; the global converter in `ApplicationDbContext` forces `Kind=Unspecified` on write, so `timestamptz` columns would fail.
- No `BeginTransaction`/`UseTransaction` anywhere in `backend/src` (CI grep `scripts/check-no-managed-tx.sh`). Each state change is one `SaveChangesAsync`.
- Long Hangfire jobs: `[AutomaticRetry(Attempts = 0)]`; Hangfire `WorkerCount = 1`.
- Identity is resolved inside handlers via `ICurrentUserService` (ADR-005); controllers never resolve identity.
- Conventional commits; every commit message ends with a blank line and `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **Two people act on the same proposal at once** (approve vs. revise, or two approvers) — exactly one write wins, the other gets 409 `MarketingAdsConcurrentChange`; never two approvals of different versions, never a lost revision. Pinned by the `Revision` concurrency-token integration test (Task 3) and the handler concurrency test (Task 10).
2. **The execution job is delivered twice** (Hangfire re-enqueue, stuck-Approved re-enqueue by the expiry job) — the platform is called once. Pinned by `RunAsync_RunTwice_ExecutesOnce` (Task 11).
3. **The kill switch is flipped off while a 50-action proposal is running** — the remaining actions are skipped, not executed. Pinned by `RunAsync_KillSwitchTurnedOffMidRun_SkipsRemainingActions` (Task 11).
4. **A keyword text or entity name containing newlines / fake "approved" lines** — the rendered diff stays one line per action. Pinned by `RenderLine_ControlCharactersCannotForgeExtraLines` (Task 5).
5. **An HTTP timeout inside an executor** (`TaskCanceledException` while the job token is not cancelled) — recorded as a failed action and the proposal is finalised, not left `Executing`. Pinned by `RunAsync_HttpTimeout_IsRecordedAsFailureNotCancellation` (Task 11).

---

## Before you start

- [ ] **Read the binding documents.** Read in full: the spec above, `docs/handoff/marketing-agents-platform.md`, `CLAUDE.md`, `docs/architecture/development_guidelines.md` (DTO rules, ADR-004/005/007/008), `docs/architecture/testing-strategy.md`, `docs/development/setup.md` (migrations), `docs/processes/_TEMPLATE.md`.

- [ ] **Create the branch from origin/main (in your worktree).**

```bash
git fetch origin
git switch -c feature/marketing-ads-c3-proposals origin/main
```

- [ ] **Verify PR C1 and C2 are merged.** Run from the repo root:

```bash
grep -rn "interface IAdActionExecutor" backend/src/Anela.Heblo.Application/Features/MarketingAds/Contracts/
grep -rn "public sealed record AdAction(" backend/src/Anela.Heblo.Application/Features/MarketingAds/Contracts/
grep -rn "class AdSettingsGuard" backend/src/Anela.Heblo.Application/Features/MarketingAds/Contracts/
grep -rn "class AdsDbContext" backend/src/Anela.Heblo.Persistence.Ads/
grep -rn "class FakeAdActionExecutor" backend/test/Anela.Heblo.MarketingAds.TestKit/
grep -rn "interface IAdExecutionLookup" backend/src/
grep -rln "IAdExecutionLookup" backend/src/
grep -n "MarketingAds" backend/src/Anela.Heblo.Application/ApplicationModule.cs
```

Expected: every command prints at least one match. If any C1 line is empty, or `IAdExecutionLookup` is absent, **stop** — the prerequisites are not on main. Write down, for later tasks:
1. the namespace of `AdsDbContext` (this plan assumes `Anela.Heblo.Persistence.Ads`) and its `SchemaName` constant;
2. the full declaration of `IAdExecutionLookup` and its record `AdExecutedAction` (C2's plan: `Contracts/IAdExecutionLookup.cs`, `Contracts/AdExecutedAction.cs`; Task 16 adapts to any difference);
3. the file and line where C2 registers `NoOpAdExecutionLookup` (C2's plan: `Sync/NoOpAdExecutionLookup.cs`; Task 16 deletes both);
4. the public members of `FakeAdActionExecutor` (C1's plan: ctor `(AdPlatform platform = GoogleAds, params AdActionType[])`, `SeedAd(account, adId, status)`, `ExecutedActions`; Task 11 uses them).

- [ ] **Check the next free ApplicationDbContext error-code range.**

```bash
grep -n "XX)" backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs | tail -5
```

Expected: the last module bucket is `// Pricing simulator module errors (38XX)`. This plan uses **39XX** for MarketingAds. If a `MarketingAds` 39XX bucket already exists, append after its last value and shift this plan's numbers accordingly; if 39XX is taken by another module, use the next free hundred and shift.

- [ ] **Know the build/test commands** (memory gotchas: concurrent builds hang; integration tests need podman):

```bash
dotnet build-server shutdown
MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_DISABLE_BUILD_SERVERS=1 dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "<filter>"
# integration (Postgres) tests:
podman machine start
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "Category=Integration&FullyQualifiedName~MarketingAds"
```

If `dotnet test --no-build` reports a stack trace that contradicts the source, the binaries are stale: touch the test file and rebuild.

## File structure

```
backend/src/Anela.Heblo.Domain/Features/MarketingAds/
  AdProposalStatus.cs AdActorKind.cs ApprovalChannel.cs ApprovalDecision.cs AdAuditEventType.cs
  AdAutonomyMode.cs AgentRunStatus.cs AdActionExecutionState.cs AdActionRevertState.cs
  AdProposalStateMachine.cs AdProposalDraft.cs AdProposal.cs AdProposalVersion.cs AdProposalApproval.cs
  AdProposalActionExecution.cs AdAuditEvent.cs AgentRun.cs AdAutonomySetting.cs AdLimitSetting.cs
  AdKillSwitch.cs AdKillSwitchPolicy.cs
  IAdProposalRepository.cs IAdGovernanceRepository.cs IAgentRunRepository.cs IAdAuditLog.cs
backend/src/Anela.Heblo.Persistence/MarketingAds/
  *Configuration.cs (9) MarketingAdsSeedData.cs MarketingAdsSql.cs
  AdProposalRepository.cs AdGovernanceRepository.cs AgentRunRepository.cs AdAuditLog.cs
backend/src/Anela.Heblo.Persistence/Migrations/<ts>_AddMarketingAdsProposals.cs
backend/src/Anela.Heblo.Application/Features/MarketingAds/Proposals/
  AdProposalOptions.cs MarketingAdsProposalsModule.cs AdSystemPrincipals.cs AdProposalGuards.cs
  AdProposalErrors.cs AdProposalDtoMapper.cs
  Actions/  AdActionJson.cs AdActionFactory.cs AdProposalShapeValidator.cs AdProposalDiffRenderer.cs
  Contracts/ AdActionInputDto.cs AdProposalDtos.cs AdSettingsDtos.cs AdActivityDtos.cs
  Limits/   AdLimitKeys.cs AdLimitReasons.cs AdLimitsDecision.cs AdLimitsEvaluation.cs AdLimitsRequest.cs
            IAdLimitsEvaluator.cs AdLimitsEvaluator.cs IAdLimitsContextBuilder.cs AdLimitsContextBuilder.cs
  ReadModel/ AdGroupOccupancy.cs IAdsReadModel.cs AdsDbReadModel.cs UnavailableAdsReadModel.cs
  Approval/ ApprovalChannelResolution.cs IApprovalChannelResolver.cs DenyAllApprovalChannelResolver.cs
  Autonomy/ IAdAutonomyService.cs AdAutonomyService.cs
  Audit/    AdAuditEvents.cs
  Execution/ IAdProposalExecutionEnqueuer.cs HangfireAdProposalExecutionEnqueuer.cs AdProposalExecutionJob.cs AdExecutionLookup.cs
  Jobs/     AdProposalExpiryJob.cs
  UseCases/<UseCase>/{Request,Response,Handler}.cs  (16 use cases; StartAgentRun/ also holds AgentRunScopes.cs)
backend/src/Anela.Heblo.API/Controllers/
  MarketingAdsProposalsController.cs MarketingAdsSettingsController.cs MarketingAdsActivityController.cs
backend/test/Anela.Heblo.Tests/Features/MarketingAds/Proposals/   (unit + handler + integration tests)
docs/processes/flow-ad-proposal-approval.md
```

---

### Task 1: Feature entries for ads, approvals and MCP approvals

**Ordering decision.** The spec lists the new `Feature` values under C4, but C3's controllers must reference them and `Feature` is generated, so a C3 reference to a missing value does not compile. **C3 adds the three feature entries to `access-matrix.json` and regenerates.** C3 does **not** touch `seedGroups`, the agent identity, the `azp` channel check or MCP tools — those remain C4. Until C4, no group holds these permissions except via the `super_user` app role, and the deny-all channel resolver blocks every approval regardless.

**Files:**
- Modify: `access-matrix.json` (features array, after `Marketing_Performance`)
- Regenerated: `backend/src/Anela.Heblo.Domain/Features/Authorization/Feature.generated.cs`, `AccessMatrix.generated.cs`, `AccessRoles.generated.cs`, `frontend/src/auth/accessMatrix.generated.ts`, `access-matrix-entra.generated.json`
- Test: `backend/test/Anela.Heblo.Tests/Authorization/MarketingAdsFeaturesTests.cs`

**Interfaces:**
- Produces: `Feature.Marketing_Ads` (Read/Write), `Feature.Marketing_AdApprovals` (Read/Write/Admin), `Feature.Marketing_AdApprovalsViaMcp` (Read). Role strings `marketing.ads.*`, `marketing.ad_approvals.*`, `marketing.ad_approvals_via_mcp.read`.

- [ ] **Step 1: Write the failing test**

```csharp
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class MarketingAdsFeaturesTests
{
    [Theory]
    [InlineData(Feature.Marketing_Ads, "Reklamní kampaně", true, false)]
    [InlineData(Feature.Marketing_AdApprovals, "Schvalování kampaní", true, true)]
    [InlineData(Feature.Marketing_AdApprovalsViaMcp, "Schvalování přes Claude", false, false)]
    public void Feature_IsDeclaredWithTheLevelsFromSpecSection72(Feature feature, string label, bool hasWrite, bool hasAdmin)
    {
        var definition = AccessMatrix.Features.Single(f => f.Key == feature);

        definition.Label.Should().Be(label);
        definition.HasWrite.Should().Be(hasWrite);
        definition.HasAdmin.Should().Be(hasAdmin);
    }

    [Theory]
    [InlineData(Feature.Marketing_Ads, AccessLevel.Read, "marketing.ads.read")]
    [InlineData(Feature.Marketing_Ads, AccessLevel.Write, "marketing.ads.write")]
    [InlineData(Feature.Marketing_AdApprovals, AccessLevel.Write, "marketing.ad_approvals.write")]
    [InlineData(Feature.Marketing_AdApprovals, AccessLevel.Admin, "marketing.ad_approvals.admin")]
    [InlineData(Feature.Marketing_AdApprovalsViaMcp, AccessLevel.Read, "marketing.ad_approvals_via_mcp.read")]
    public void Feature_MapsToAStableRoleString(Feature feature, AccessLevel level, string expected)
    {
        AccessRoles.For(feature, level).Should().Be(expected);
    }
}
```

- [ ] **Step 2: Run the build to verify it fails**

Run: `MSBUILDDISABLENODEREUSE=1 dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false`
Expected: FAIL — `error CS0117: 'Feature' does not contain a definition for 'Marketing_Ads'`.

- [ ] **Step 3: Add the three features** — in `access-matrix.json`, directly after the line `{ "key": "Marketing_Performance", "label": "Analýzy", "hasWrite": true },` insert:

```json
    { "key": "Marketing_Ads", "label": "Reklamní kampaně", "hasWrite": true },
    { "key": "Marketing_AdApprovals", "label": "Schvalování kampaní", "hasWrite": true, "hasAdmin": true },
    { "key": "Marketing_AdApprovalsViaMcp", "label": "Schvalování přes Claude" },
```

Do **not** add these roles to any `seedGroups` entry (C4 owns grants; `JsonGroupSeeder` never updates existing prod groups anyway). Do **not** add `menuPaths` (C5 adds pages).

- [ ] **Step 4: Regenerate the access-matrix artifacts** (run from repo root; the tool's defaults are repo-root-relative):

```bash
dotnet run --project backend/tools/Anela.Heblo.AccessMatrixGen
git status --short
```

Expected: the five generated files listed under **Files** are modified; `Feature.generated.cs` contains `Marketing_Ads,`, `Marketing_AdApprovals,`, `Marketing_AdApprovalsViaMcp,`.

- [ ] **Step 5: Build and run the tests**

```bash
MSBUILDDISABLENODEREUSE=1 dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~Anela.Heblo.Tests.Authorization"
```

Expected: PASS (includes `AccessMatrixJsonTests`, `AccessMatrixTests` and the new `MarketingAdsFeaturesTests`).

- [ ] **Step 6: Commit**

```bash
git add access-matrix.json access-matrix-entra.generated.json frontend/src/auth/accessMatrix.generated.ts backend/src/Anela.Heblo.Domain/Features/Authorization/*.generated.cs backend/test/Anela.Heblo.Tests/Authorization/MarketingAdsFeaturesTests.cs
git commit -m "feat: add Marketing_Ads, Marketing_AdApprovals and Marketing_AdApprovalsViaMcp features

C3 needs the feature values to gate its controllers; seed groups and grants stay in C4.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 2: Domain model — proposal aggregate, lifecycle, settings, audit, agent runs

The Domain project cannot reference Application, so the C1 enums `AdPlatform` / `AdActionType` / `AdEntityLevel` are stored on entities as their **enum names** (`string`). Application parses them with `Enum.Parse<T>`. Domain-owned enums are stored as strings too (readable in Metabase).

**Files:**
- Create (all in `backend/src/Anela.Heblo.Domain/Features/MarketingAds/`): the 9 enum files, `AdProposalStateMachine.cs`, `AdProposalDraft.cs`, `AdProposal.cs`, `AdProposalVersion.cs`, `AdProposalApproval.cs`, `AdProposalActionExecution.cs`, `AdAuditEvent.cs`, `AgentRun.cs`, `AdAutonomySetting.cs`, `AdLimitSetting.cs`, `AdKillSwitch.cs`, `AdKillSwitchPolicy.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/Proposals/Domain/AdProposalStateMachineTests.cs`, `AdProposalTests.cs`, `AdKillSwitchPolicyTests.cs`

**Interfaces:**
- Produces (namespace `Anela.Heblo.Domain.Features.MarketingAds`):
  - enums `AdProposalStatus { Pending=1, Approved=2, Executing=3, Executed=4, PartiallyExecuted=5, Failed=6, Rejected=7, Expired=8, Reverted=9 }`, `AdActorKind { Human=1, Agent=2, System=3 }`, `ApprovalChannel { Web=1, Mcp=2, System=3 }`, `ApprovalDecision { Approved=1, Rejected=2 }`, `AdAuditEventType { Submitted=1 … KillSwitchChanged=11 }`, `AdAutonomyMode { ProposeOnly=1, AutoWithinLimits=2, Auto=3 }`, `AgentRunStatus { Running=1, Succeeded=2, Failed=3 }`, `AdActionExecutionState { Succeeded=1, Failed=2, StaleState=3, Skipped=4 }`, `AdActionRevertState { NotReverted=0, Reverted=1, RevertFailed=2 }`
  - `AdProposalStateMachine.CanTransition(AdProposalStatus from, AdProposalStatus to) : bool`
  - `record AdProposalDraft(string Platform, string AccountExternalId, string ActionType, string CreatedByPrincipal, AdActorKind CreatedByKind, Guid? AgentRunId, string ActionsJson, int ActionCount, string Reasoning)`
  - `AdProposal.Create(AdProposalDraft, DateTime now, TimeSpan ttl)`, `GetCurrentVersion()`, `GetCurrentApprovers() : IReadOnlyList<string>`, `IsExpiredAt(DateTime)`, `Revise(string actionsJson, int actionCount, string reasoning, string principal, DateTime now, TimeSpan ttl)`, `RecordApproval(string principal, ApprovalChannel channel, DateTime now, bool isFinal)`, `Reject(string principal, ApprovalChannel channel, string reason, DateTime now)`, `Expire(DateTime)`, `StartExecution(DateTime)`, `RecordExecution(AdProposalActionExecution)`, `FinishExecution(DateTime)`, `MarkReverted(DateTime)`, `Touch(DateTime)`; property `Revision` (concurrency token)
  - `AdProposalActionExecution.MarkReverted(DateTime now, string? responseJson)`, `MarkRevertFailed(DateTime now, string error)`
  - `AgentRun.Start(Guid id, string agentName, string principal, string dataScopesJson, DateTime now)`, `Finish(AgentRunStatus status, string? summary, string dataScopesJson, int proposalsCreated, long? inputTokens, long? outputTokens, decimal? costCzk, DateTime now)`
  - `AdKillSwitch.GlobalScope = "Global"`, `AdKillSwitchPolicy.IsExecutionEnabled(IEnumerable<AdKillSwitch> switches, string platform) : bool`

- [ ] **Step 1: Write the failing tests**

`AdProposalStateMachineTests.cs`:

```csharp
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Domain;

public class AdProposalStateMachineTests
{
    private static readonly HashSet<(AdProposalStatus, AdProposalStatus)> Allowed = new()
    {
        (AdProposalStatus.Pending, AdProposalStatus.Approved),
        (AdProposalStatus.Pending, AdProposalStatus.Rejected),
        (AdProposalStatus.Pending, AdProposalStatus.Expired),
        (AdProposalStatus.Approved, AdProposalStatus.Executing),
        (AdProposalStatus.Executing, AdProposalStatus.Executed),
        (AdProposalStatus.Executing, AdProposalStatus.PartiallyExecuted),
        (AdProposalStatus.Executing, AdProposalStatus.Failed),
        (AdProposalStatus.Executed, AdProposalStatus.Reverted),
        (AdProposalStatus.PartiallyExecuted, AdProposalStatus.Reverted),
    };

    public static TheoryData<AdProposalStatus, AdProposalStatus> AllPairs()
    {
        var data = new TheoryData<AdProposalStatus, AdProposalStatus>();
        foreach (var from in Enum.GetValues<AdProposalStatus>())
            foreach (var to in Enum.GetValues<AdProposalStatus>())
                data.Add(from, to);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void CanTransition_MatchesTheSpecLifecycle(AdProposalStatus from, AdProposalStatus to)
    {
        AdProposalStateMachine.CanTransition(from, to).Should().Be(Allowed.Contains((from, to)));
    }
}
```

`AdProposalTests.cs`:

```csharp
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Domain;

public class AdProposalTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(72);

    private static AdProposal NewProposal(int actionCount = 2) => AdProposal.Create(
        new AdProposalDraft("GoogleAds", "acc-1", "PauseAd", "agent-1", AdActorKind.Agent, null, "[]", actionCount, "why"),
        Now, Ttl);

    [Fact]
    public void Create_StartsPendingAtVersionOneWithTtl()
    {
        var p = NewProposal();

        p.Status.Should().Be(AdProposalStatus.Pending);
        p.CurrentVersion.Should().Be(1);
        p.Versions.Should().ContainSingle(v => v.Version == 1 && v.Reasoning == "why" && v.CreatedBy == "agent-1");
        p.ExpiresAt.Should().Be(Now + Ttl);
        p.Revision.Should().Be(1);
    }

    [Fact]
    public void Revise_AddsANewVersionAndVoidsEarlierApprovals()
    {
        var p = NewProposal();
        p.RecordApproval("chief", ApprovalChannel.Web, Now, isFinal: false);

        p.Revise("[]", 1, "better", "agent-1", Now.AddHours(1), Ttl);

        p.CurrentVersion.Should().Be(2);
        p.Versions.Select(v => v.Version).Should().BeEquivalentTo(new[] { 1, 2 });
        p.GetCurrentApprovers().Should().BeEmpty("approvals of version 1 do not carry over");
        p.ExpiresAt.Should().Be(Now.AddHours(1) + Ttl);
        p.Status.Should().Be(AdProposalStatus.Pending);
    }

    [Fact]
    public void RecordApproval_NotFinal_StaysPending_Final_Approves()
    {
        var p = NewProposal();

        p.RecordApproval("chief", ApprovalChannel.Web, Now, isFinal: false);
        p.Status.Should().Be(AdProposalStatus.Pending);

        p.RecordApproval("specialist", ApprovalChannel.Web, Now, isFinal: true);
        p.Status.Should().Be(AdProposalStatus.Approved);
        p.GetCurrentApprovers().Should().BeEquivalentTo(new[] { "chief", "specialist" });
    }

    [Fact]
    public void Reject_StoresReasonAndMovesToRejected()
    {
        var p = NewProposal();
        p.Reject("chief", ApprovalChannel.Mcp, "too broad", Now);

        p.Status.Should().Be(AdProposalStatus.Rejected);
        p.Approvals.Should().ContainSingle(a => a.Decision == ApprovalDecision.Rejected && a.Reason == "too broad" && a.Channel == ApprovalChannel.Mcp);
    }

    [Fact]
    public void Revise_WhenNotPending_Throws()
    {
        var p = NewProposal();
        p.Reject("chief", ApprovalChannel.Web, "no", Now);

        var act = () => p.Revise("[]", 1, "x", "agent-1", Now, Ttl);

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(2, 2, AdProposalStatus.Executed)]
    [InlineData(2, 1, AdProposalStatus.PartiallyExecuted)]
    [InlineData(2, 0, AdProposalStatus.Failed)]
    public void FinishExecution_DerivesStatusFromSucceededActions(int actions, int succeeded, AdProposalStatus expected)
    {
        var p = NewProposal(actions);
        p.RecordApproval("chief", ApprovalChannel.Web, Now, isFinal: true);
        p.StartExecution(Now);
        for (var i = 0; i < actions; i++)
        {
            p.RecordExecution(new AdProposalActionExecution
            {
                Version = 1,
                ActionIndex = i,
                State = i < succeeded ? AdActionExecutionState.Succeeded : AdActionExecutionState.Failed,
            });
        }

        p.FinishExecution(Now.AddMinutes(1));

        p.Status.Should().Be(expected);
        p.ExecutionFinishedAt.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public void IsExpiredAt_OnlyForPendingPastExpiry()
    {
        var p = NewProposal();

        p.IsExpiredAt(Now + Ttl - TimeSpan.FromSeconds(1)).Should().BeFalse();
        p.IsExpiredAt(Now + Ttl).Should().BeTrue();

        p.RecordApproval("chief", ApprovalChannel.Web, Now, isFinal: true);
        p.IsExpiredAt(Now + Ttl).Should().BeFalse("an approved proposal no longer expires");
    }

    [Fact]
    public void EveryStateChange_BumpsRevision()
    {
        var p = NewProposal();
        var before = p.Revision;

        p.RecordApproval("chief", ApprovalChannel.Web, Now, isFinal: false);

        p.Revision.Should().Be(before + 1);
    }
}
```

`AdKillSwitchPolicyTests.cs`:

```csharp
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Domain;

public class AdKillSwitchPolicyTests
{
    private static AdKillSwitch S(string scope, bool enabled) => new() { Scope = scope, ExecutionEnabled = enabled, UpdatedBy = "t" };

    [Fact]
    public void NoRows_FailsClosed() =>
        AdKillSwitchPolicy.IsExecutionEnabled(Array.Empty<AdKillSwitch>(), "GoogleAds").Should().BeFalse();

    [Fact]
    public void GlobalOff_DisablesEveryPlatform() =>
        AdKillSwitchPolicy.IsExecutionEnabled(new[] { S("Global", false), S("GoogleAds", true) }, "GoogleAds").Should().BeFalse();

    [Fact]
    public void GlobalOn_NoOverride_Enables() =>
        AdKillSwitchPolicy.IsExecutionEnabled(new[] { S("Global", true) }, "Sklik").Should().BeTrue();

    [Fact]
    public void GlobalOn_PlatformOverrideOff_DisablesThatPlatformOnly()
    {
        var switches = new[] { S("Global", true), S("MetaAds", false) };

        AdKillSwitchPolicy.IsExecutionEnabled(switches, "MetaAds").Should().BeFalse();
        AdKillSwitchPolicy.IsExecutionEnabled(switches, "GoogleAds").Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `MSBUILDDISABLENODEREUSE=1 dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false`
Expected: FAIL — `The type or namespace name 'MarketingAds' does not exist in the namespace 'Anela.Heblo.Domain.Features'`.

- [ ] **Step 3: Create the enums** (one file each, namespace `Anela.Heblo.Domain.Features.MarketingAds`):

```csharp
namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>Lifecycle of an ad proposal (spec 6.2). Transitions are guarded by <see cref="AdProposalStateMachine"/>.</summary>
public enum AdProposalStatus
{
    Pending = 1,
    Approved = 2,
    Executing = 3,
    Executed = 4,
    PartiallyExecuted = 5,
    Failed = 6,
    Rejected = 7,
    Expired = 8,
    Reverted = 9,
}
```

```csharp
namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>Who performed an action recorded on a proposal or in the audit log.</summary>
public enum AdActorKind
{
    Human = 1,
    Agent = 2,
    System = 3,
}
```

```csharp
namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>Where an approval decision came from. Resolved server-side by IApprovalChannelResolver, never by the client.</summary>
public enum ApprovalChannel
{
    Web = 1,
    Mcp = 2,
    System = 3,
}
```

```csharp
namespace Anela.Heblo.Domain.Features.MarketingAds;

public enum ApprovalDecision
{
    Approved = 1,
    Rejected = 2,
}
```

```csharp
namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>Event types of the append-only ad audit log (spec 6.5).</summary>
public enum AdAuditEventType
{
    Submitted = 1,
    Revised = 2,
    Approved = 3,
    Rejected = 4,
    Expired = 5,
    ExecutionStarted = 6,
    ActionExecuted = 7,
    ActionFailed = 8,
    Reverted = 9,
    SettingChanged = 10,
    KillSwitchChanged = 11,
}
```

```csharp
namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>Per platform × action type autonomy (spec 6.4). Seeded ProposeOnly everywhere.</summary>
public enum AdAutonomyMode
{
    ProposeOnly = 1,
    AutoWithinLimits = 2,
    Auto = 3,
}
```

```csharp
namespace Anela.Heblo.Domain.Features.MarketingAds;

public enum AgentRunStatus
{
    Running = 1,
    Succeeded = 2,
    Failed = 3,
}
```

```csharp
namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>Outcome of one action of an executed proposal version.</summary>
public enum AdActionExecutionState
{
    Succeeded = 1,
    Failed = 2,
    StaleState = 3,
    Skipped = 4,
}
```

```csharp
namespace Anela.Heblo.Domain.Features.MarketingAds;

public enum AdActionRevertState
{
    NotReverted = 0,
    Reverted = 1,
    RevertFailed = 2,
}
```

- [ ] **Step 4: Create the state machine and the draft**

`AdProposalStateMachine.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>Allowed status transitions (spec 6.2). A revision keeps the proposal Pending and is not a transition.</summary>
public static class AdProposalStateMachine
{
    private static readonly IReadOnlyDictionary<AdProposalStatus, AdProposalStatus[]> Allowed =
        new Dictionary<AdProposalStatus, AdProposalStatus[]>
        {
            [AdProposalStatus.Pending] = new[] { AdProposalStatus.Approved, AdProposalStatus.Rejected, AdProposalStatus.Expired },
            [AdProposalStatus.Approved] = new[] { AdProposalStatus.Executing },
            [AdProposalStatus.Executing] = new[] { AdProposalStatus.Executed, AdProposalStatus.PartiallyExecuted, AdProposalStatus.Failed },
            [AdProposalStatus.Executed] = new[] { AdProposalStatus.Reverted },
            [AdProposalStatus.PartiallyExecuted] = new[] { AdProposalStatus.Reverted },
        };

    public static bool CanTransition(AdProposalStatus from, AdProposalStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);
}
```

`AdProposalDraft.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>Everything needed to create a proposal. Platform/ActionType are the C1 enum names.</summary>
public sealed record AdProposalDraft(
    string Platform,
    string AccountExternalId,
    string ActionType,
    string CreatedByPrincipal,
    AdActorKind CreatedByKind,
    Guid? AgentRunId,
    string ActionsJson,
    int ActionCount,
    string Reasoning);
```

- [ ] **Step 5: Create the child entities**

Children use a surrogate identity key that code never sets (gotcha: setting the PK of a child added to a tracked parent's collection makes EF issue `UPDATE … 0 rows` instead of `INSERT`).

`AdProposalVersion.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>Immutable snapshot of a proposal's actions. Never updated or deleted (Postgres trigger).</summary>
public class AdProposalVersion
{
    public long Id { get; set; }
    public int ProposalId { get; set; }
    public int Version { get; set; }
    /// <summary>JSON array of C1 AdAction records (AdActionJson in Application).</summary>
    public string ActionsJson { get; set; } = null!;
    public int ActionCount { get; set; }
    /// <summary>Free text from the proposer. Shown separately; never used to render the diff.</summary>
    public string Reasoning { get; set; } = null!;
    public string CreatedBy { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}
```

`AdProposalApproval.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>One approve/reject decision on a specific proposal version.</summary>
public class AdProposalApproval
{
    public long Id { get; set; }
    public int ProposalId { get; set; }
    public int Version { get; set; }
    public string Principal { get; set; } = null!;
    public ApprovalChannel Channel { get; set; }
    public ApprovalDecision Decision { get; set; }
    public string? Reason { get; set; }
    public DateTime At { get; set; }
}
```

`AdProposalActionExecution.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>
/// What happened to one action when its proposal version executed: the before-state read from the
/// platform, the platform response and, after a revert, the revert result. Platform/account/type are
/// denormalised so the out-of-band matcher and the weekly pause limit can query without joins.
/// </summary>
public class AdProposalActionExecution
{
    public long Id { get; set; }
    public int ProposalId { get; set; }
    public int Version { get; set; }
    public int ActionIndex { get; set; }
    public string Platform { get; set; } = null!;
    public string AccountExternalId { get; set; } = null!;
    public string ActionType { get; set; } = null!;
    public string TargetLevel { get; set; } = null!;
    public string TargetExternalId { get; set; } = null!;
    /// <summary>The action's payload (keyword text, match type) as JSON — needed by the out-of-band matcher.</summary>
    public string PayloadJson { get; set; } = "{}";
    public AdActionExecutionState State { get; set; }
    public string? BeforeValue { get; set; }
    public string? BeforeRawJson { get; set; }
    public string? AfterValue { get; set; }
    public string? PlatformResourceId { get; set; }
    public string? PlatformResponseJson { get; set; }
    public string? Error { get; set; }
    public DateTime ExecutedAt { get; set; }
    public AdActionRevertState RevertState { get; set; }
    public DateTime? RevertedAt { get; set; }
    public string? RevertResponseJson { get; set; }
    public string? RevertError { get; set; }

    public void MarkReverted(DateTime now, string? responseJson)
    {
        RevertState = AdActionRevertState.Reverted;
        RevertedAt = now;
        RevertResponseJson = responseJson;
        RevertError = null;
    }

    public void MarkRevertFailed(DateTime now, string error)
    {
        RevertState = AdActionRevertState.RevertFailed;
        RevertedAt = now;
        RevertError = error;
    }
}
```

- [ ] **Step 6: Create the aggregate root** — `AdProposal.cs`:

```csharp
using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>
/// A structured, versioned request to change a live ad account (spec 6.1). Rows are never deleted.
/// Platform and ActionType hold the C1 enum names (AdPlatform / AdActionType) because the Domain
/// cannot reference the Application contracts.
/// </summary>
public class AdProposal : IEntity<int>
{
    public int Id { get; set; }
    public int CurrentVersion { get; set; }
    public AdProposalStatus Status { get; set; }
    public string Platform { get; set; } = null!;
    public string AccountExternalId { get; set; } = null!;
    public string ActionType { get; set; } = null!;
    public string CreatedByPrincipal { get; set; } = null!;
    public AdActorKind CreatedByKind { get; set; }
    public Guid? AgentRunId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ExecutionStartedAt { get; set; }
    public DateTime? ExecutionFinishedAt { get; set; }
    public DateTime? RevertedAt { get; set; }

    /// <summary>Optimistic concurrency token; every state change bumps it via <see cref="Touch"/>.</summary>
    public int Revision { get; set; }

    public List<AdProposalVersion> Versions { get; set; } = new();
    public List<AdProposalApproval> Approvals { get; set; } = new();
    public List<AdProposalActionExecution> Executions { get; set; } = new();

    public static AdProposal Create(AdProposalDraft draft, DateTime now, TimeSpan ttl)
    {
        var proposal = new AdProposal
        {
            Status = AdProposalStatus.Pending,
            Platform = draft.Platform,
            AccountExternalId = draft.AccountExternalId,
            ActionType = draft.ActionType,
            CreatedByPrincipal = draft.CreatedByPrincipal,
            CreatedByKind = draft.CreatedByKind,
            AgentRunId = draft.AgentRunId,
            CreatedAt = now,
        };
        proposal.AppendVersion(draft.ActionsJson, draft.ActionCount, draft.Reasoning, draft.CreatedByPrincipal, now, ttl);
        return proposal;
    }

    public AdProposalVersion GetCurrentVersion() => Versions.Single(v => v.Version == CurrentVersion);

    public IReadOnlyList<string> GetCurrentApprovers() => Approvals
        .Where(a => a.Version == CurrentVersion && a.Decision == ApprovalDecision.Approved)
        .Select(a => a.Principal)
        .ToList();

    public bool IsExpiredAt(DateTime now) => Status == AdProposalStatus.Pending && now >= ExpiresAt;

    public void Revise(string actionsJson, int actionCount, string reasoning, string principal, DateTime now, TimeSpan ttl)
    {
        EnsureStatus(AdProposalStatus.Pending);
        AppendVersion(actionsJson, actionCount, reasoning, principal, now, ttl);
    }

    public void RecordApproval(string principal, ApprovalChannel channel, DateTime now, bool isFinal)
    {
        EnsureStatus(AdProposalStatus.Pending);
        Approvals.Add(new AdProposalApproval
        {
            Version = CurrentVersion,
            Principal = principal,
            Channel = channel,
            Decision = ApprovalDecision.Approved,
            At = now,
        });
        if (isFinal)
        {
            TransitionTo(AdProposalStatus.Approved, now);
        }
        else
        {
            Touch(now);
        }
    }

    public void Reject(string principal, ApprovalChannel channel, string reason, DateTime now)
    {
        EnsureStatus(AdProposalStatus.Pending);
        Approvals.Add(new AdProposalApproval
        {
            Version = CurrentVersion,
            Principal = principal,
            Channel = channel,
            Decision = ApprovalDecision.Rejected,
            Reason = reason,
            At = now,
        });
        TransitionTo(AdProposalStatus.Rejected, now);
    }

    public void Expire(DateTime now) => TransitionTo(AdProposalStatus.Expired, now);

    public void StartExecution(DateTime now)
    {
        TransitionTo(AdProposalStatus.Executing, now);
        ExecutionStartedAt = now;
    }

    public void RecordExecution(AdProposalActionExecution execution) => Executions.Add(execution);

    public void FinishExecution(DateTime now)
    {
        var succeeded = Executions.Count(e => e.Version == CurrentVersion && e.State == AdActionExecutionState.Succeeded);
        var total = GetCurrentVersion().ActionCount;
        var target = succeeded == total
            ? AdProposalStatus.Executed
            : succeeded > 0 ? AdProposalStatus.PartiallyExecuted : AdProposalStatus.Failed;
        TransitionTo(target, now);
        ExecutionFinishedAt = now;
    }

    public void MarkReverted(DateTime now)
    {
        TransitionTo(AdProposalStatus.Reverted, now);
        RevertedAt = now;
    }

    public void Touch(DateTime now)
    {
        UpdatedAt = now;
        Revision++;
    }

    private void AppendVersion(string actionsJson, int actionCount, string reasoning, string principal, DateTime now, TimeSpan ttl)
    {
        CurrentVersion++;
        Versions.Add(new AdProposalVersion
        {
            Version = CurrentVersion,
            ActionsJson = actionsJson,
            ActionCount = actionCount,
            Reasoning = reasoning,
            CreatedBy = principal,
            CreatedAt = now,
        });
        ExpiresAt = now + ttl;
        Touch(now);
    }

    private void EnsureStatus(AdProposalStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"Ad proposal {Id} is {Status}; the operation requires {expected}.");
        }
    }

    private void TransitionTo(AdProposalStatus target, DateTime now)
    {
        if (!AdProposalStateMachine.CanTransition(Status, target))
        {
            throw new InvalidOperationException($"Ad proposal {Id} cannot move from {Status} to {target}.");
        }
        Status = target;
        Touch(now);
    }
}
```

- [ ] **Step 7: Create audit, agent-run and settings entities**

`AdAuditEvent.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>Append-only audit row (spec 6.5). A Postgres trigger rejects UPDATE, DELETE and TRUNCATE.</summary>
public class AdAuditEvent
{
    public long Id { get; set; }
    public int? ProposalId { get; set; }
    /// <summary>Set for proposal events so EF fills ProposalId when the proposal is inserted in the same SaveChanges.</summary>
    public AdProposal? Proposal { get; set; }
    public int? Version { get; set; }
    public AdAuditEventType EventType { get; set; }
    public string ActorPrincipal { get; set; } = null!;
    public AdActorKind ActorKind { get; set; }
    public ApprovalChannel Channel { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTime OccurredAt { get; set; }
}
```

`AgentRun.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>Observability of an external agent run, reported by the agent itself (spec 6.5).</summary>
public class AgentRun
{
    public Guid Id { get; set; }
    public string AgentName { get; set; } = null!;
    public string Principal { get; set; } = null!;
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public AgentRunStatus Status { get; set; }
    public string? Summary { get; set; }
    public string DataScopesReadJson { get; set; } = "[]";
    public int ProposalsCreated { get; set; }
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }
    public decimal? CostCzk { get; set; }

    public static AgentRun Start(Guid id, string agentName, string principal, string dataScopesJson, DateTime now) => new()
    {
        Id = id,
        AgentName = agentName,
        Principal = principal,
        StartedAt = now,
        Status = AgentRunStatus.Running,
        DataScopesReadJson = dataScopesJson,
    };

    public void Finish(AgentRunStatus status, string? summary, string dataScopesJson, int proposalsCreated,
        long? inputTokens, long? outputTokens, decimal? costCzk, DateTime now)
    {
        if (Status != AgentRunStatus.Running)
        {
            throw new InvalidOperationException($"Agent run {Id} is already {Status}.");
        }
        if (status == AgentRunStatus.Running)
        {
            throw new ArgumentException("A run cannot finish as Running.", nameof(status));
        }
        Status = status;
        Summary = summary;
        DataScopesReadJson = dataScopesJson;
        ProposalsCreated = proposalsCreated;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        CostCzk = costCzk;
        FinishedAt = now;
    }
}
```

`AdAutonomySetting.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>Autonomy mode per platform × action type (both C1 enum names). PK (Platform, ActionType).</summary>
public class AdAutonomySetting
{
    public string Platform { get; set; } = null!;
    public string ActionType { get; set; } = null!;
    public AdAutonomyMode Mode { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = null!;
}
```

`AdLimitSetting.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>One limit value (spec 6.3) per platform × action type × key. Keys: AdLimitKeys in Application.</summary>
public class AdLimitSetting
{
    public string Platform { get; set; } = null!;
    public string ActionType { get; set; } = null!;
    public string Key { get; set; } = null!;
    public int Value { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = null!;
}
```

`AdKillSwitch.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>Persisted execution switch. Scope is "Global" or a platform name (an override that can only disable).</summary>
public class AdKillSwitch
{
    public const string GlobalScope = "Global";

    public string Scope { get; set; } = null!;
    public bool ExecutionEnabled { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = null!;
}
```

`AdKillSwitchPolicy.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.MarketingAds;

public static class AdKillSwitchPolicy
{
    /// <summary>
    /// Execution is allowed only when the global row exists and is enabled, and the platform has no
    /// disabling override. A missing global row fails closed.
    /// </summary>
    public static bool IsExecutionEnabled(IEnumerable<AdKillSwitch> switches, string platform)
    {
        var list = switches.ToList();
        var global = list.FirstOrDefault(s => s.Scope == AdKillSwitch.GlobalScope);
        if (global is null || !global.ExecutionEnabled)
        {
            return false;
        }
        var platformOverride = list.FirstOrDefault(s => s.Scope == platform);
        return platformOverride is null || platformOverride.ExecutionEnabled;
    }
}
```

- [ ] **Step 8: Run the tests to verify they pass**

```bash
MSBUILDDISABLENODEREUSE=1 dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~MarketingAds.Proposals.Domain"
```

Expected: PASS (81 state-machine cases + aggregate + kill-switch tests).

- [ ] **Step 9: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/MarketingAds backend/test/Anela.Heblo.Tests/Features/MarketingAds/Proposals/Domain
git commit -m "feat: ad proposal domain model, lifecycle and kill switch policy

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 3: EF configuration, seeds, migration and the append-only trigger

**Files:**
- Create (in `backend/src/Anela.Heblo.Persistence/MarketingAds/`): `AdProposalConfiguration.cs`, `AdProposalVersionConfiguration.cs`, `AdProposalApprovalConfiguration.cs`, `AdProposalActionExecutionConfiguration.cs`, `AdAuditEventConfiguration.cs`, `AgentRunConfiguration.cs`, `AdAutonomySettingConfiguration.cs`, `AdLimitSettingConfiguration.cs`, `AdKillSwitchConfiguration.cs`, `MarketingAdsSeedData.cs`, `MarketingAdsSql.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` (using + 9 DbSets)
- Create (generated, then edited): `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddMarketingAdsProposals.cs` (+ `.Designer.cs`, snapshot update)
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/Proposals/Persistence/MarketingAdsProposalsMigrationIntegrationTests.cs`

**Interfaces:**
- Consumes: Task 2 entities.
- Produces: `ApplicationDbContext.AdProposals`, `.AdProposalVersions`, `.AdProposalApprovals`, `.AdProposalActionExecutions`, `.AdAuditEvents`, `.AgentRuns`, `.AdAutonomySettings`, `.AdLimitSettings`, `.AdKillSwitches`; tables of the same names in `public`; `MarketingAdsSeedData` (`Platforms`, `ActionTypes`, `Autonomy()`, `Limits()`, `KillSwitches()`); `MarketingAdsSql.AppendOnlyTriggersUp/Down`.

Table names follow the codebase convention (PascalCase in `public`), not the spec's snake_case — see Spec deviations.

- [ ] **Step 1: Write the failing integration test**

The full migration chain needs the `vector` extension, which plain `postgres:16` lacks, so the test applies **only this PR's migration** via `IMigrator.GenerateScript(previous, ours)`. This runs the real generated DDL, the seeds and the trigger SQL.

```csharp
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Persistence;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Persistence;

[Collection("PostgresIntegration")]
[Trait("Category", "Integration")]
public class MarketingAdsProposalsMigrationIntegrationTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc);
    private readonly PostgresSharedContainerFixture _fixture;
    private string _connectionString = null!;

    public MarketingAdsProposalsMigrationIntegrationTests(PostgresSharedContainerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _connectionString = await _fixture.CreateDatabaseAsync("marketing_ads_c3");
        await using var context = NewContext();
        var migrations = context.Database.GetMigrations().ToList();
        var ours = migrations.Single(m => m.EndsWith("_AddMarketingAdsProposals", StringComparison.Ordinal));
        var previous = migrations[migrations.IndexOf(ours) - 1];
        var script = context.GetService<IMigrator>().GenerateScript(previous, ours);

        await ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                "MigrationId" character varying(150) PRIMARY KEY,
                "ProductVersion" character varying(32) NOT NULL);
            """);
        await ExecuteAsync(script);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_connectionString).Options);

    private async Task ExecuteAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static AdProposal NewProposal() => AdProposal.Create(
        new AdProposalDraft("GoogleAds", "acc-1", "PauseAd", "agent-1", AdActorKind.Agent, null,
            """[{"type":"PauseAd"}]""", 1, "reason"),
        Now, TimeSpan.FromHours(72));

    private async Task<int> InsertProposalWithAuditAsync()
    {
        await using var ctx = NewContext();
        var proposal = NewProposal();
        ctx.AdProposals.Add(proposal);
        ctx.AdAuditEvents.Add(new AdAuditEvent
        {
            Proposal = proposal, Version = 1, EventType = AdAuditEventType.Submitted, ActorPrincipal = "agent-1",
            ActorKind = AdActorKind.Agent, Channel = ApprovalChannel.Mcp, PayloadJson = "{}", OccurredAt = Now,
        });
        await ctx.SaveChangesAsync();
        return proposal.Id;
    }

    [Fact]
    public async Task Seeds_ProposeOnlyEverywhere_DefaultLimits_AndExecutionDisabled()
    {
        await using var ctx = NewContext();

        var autonomy = await ctx.AdAutonomySettings.ToListAsync();
        autonomy.Should().HaveCount(6).And.OnlyContain(a => a.Mode == AdAutonomyMode.ProposeOnly);

        var limits = await ctx.AdLimitSettings.ToListAsync();
        limits.Should().HaveCount(27);
        limits.Single(l => l.Platform == "GoogleAds" && l.ActionType == "PauseAd" && l.Key == "MaxActionsPerProposal").Value.Should().Be(10);
        limits.Single(l => l.Platform == "Sklik" && l.ActionType == "AddNegativeKeyword" && l.Key == "McpMaxActionsPerProposal").Value.Should().Be(20);
        limits.Single(l => l.Platform == "MetaAds" && l.ActionType == "AddNegativeKeyword" && l.Key == "Enabled").Value.Should().Be(0);

        var killSwitch = await ctx.AdKillSwitches.SingleAsync();
        killSwitch.Scope.Should().Be(AdKillSwitch.GlobalScope);
        killSwitch.ExecutionEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData("""UPDATE public."AdAuditEvents" SET "ActorPrincipal" = 'forged'""")]
    [InlineData("""DELETE FROM public."AdAuditEvents" """)]
    [InlineData("""TRUNCATE public."AdAuditEvents" """)]
    [InlineData("""UPDATE public."AdProposalVersions" SET "Reasoning" = 'rewritten'""")]
    [InlineData("""DELETE FROM public."AdProposalVersions" """)]
    public async Task AppendOnlyTables_RejectMutation(string sql)
    {
        await InsertProposalWithAuditAsync();

        var act = () => ExecuteAsync(sql);

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == "P0001" && e.MessageText.Contains("append-only"));
    }

    [Fact]
    public async Task Proposal_RoundTripsThroughASecondContext_WithUtcTimestamps()
    {
        var id = await InsertProposalWithAuditAsync();

        await using var verify = NewContext();
        var loaded = await verify.AdProposals.Include(p => p.Versions).SingleAsync(p => p.Id == id);
        loaded.Status.Should().Be(AdProposalStatus.Pending);
        loaded.CreatedAt.Should().Be(Now);
        loaded.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        loaded.Versions.Should().ContainSingle(v => v.Version == 1 && v.ActionsJson.Contains("PauseAd"));
        (await verify.AdAuditEvents.SingleAsync()).ProposalId.Should().Be(id);
    }

    [Fact]
    public async Task Proposal_StaleRevision_ThrowsConcurrencyException()
    {
        var id = await InsertProposalWithAuditAsync();
        await using var first = NewContext();
        await using var second = NewContext();
        var a = await first.AdProposals.Include(p => p.Versions).Include(p => p.Approvals).SingleAsync(p => p.Id == id);
        var b = await second.AdProposals.Include(p => p.Versions).Include(p => p.Approvals).SingleAsync(p => p.Id == id);

        a.RecordApproval("chief", ApprovalChannel.Web, Now, isFinal: true);
        await first.SaveChangesAsync();
        b.Reject("specialist", ApprovalChannel.Web, "no", Now);
        var act = () => second.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `MSBUILDDISABLENODEREUSE=1 dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false`
Expected: FAIL — `'ApplicationDbContext' does not contain a definition for 'AdAutonomySettings'`.

- [ ] **Step 3: Add seed data and trigger SQL**

`MarketingAdsSeedData.cs`:

```csharp
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Persistence.MarketingAds;

/// <summary>
/// Seeds of the governance tables (spec 6.3, 6.4, 6.6). Strings are the C1 enum names
/// (AdPlatform, AdActionType) and the Application AdLimitKeys — a unit test pins both.
/// SecondApproverAbove is seeded equal to the per-proposal maximum, i.e. inactive until an admin lowers it.
/// </summary>
public static class MarketingAdsSeedData
{
    public const string SeededBy = "seed";
    public static readonly DateTime SeededAt = new(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);
    public static readonly string[] Platforms = { "GoogleAds", "MetaAds", "Sklik" };
    public static readonly string[] ActionTypes = { "AddNegativeKeyword", "PauseAd" };

    public static AdAutonomySetting[] Autonomy() =>
        (from platform in Platforms
         from actionType in ActionTypes
         select new AdAutonomySetting
         {
             Platform = platform,
             ActionType = actionType,
             Mode = AdAutonomyMode.ProposeOnly,
             UpdatedAt = SeededAt,
             UpdatedBy = SeededBy,
         }).ToArray();

    public static AdLimitSetting[] Limits() => Platforms.SelectMany(LimitsFor).ToArray();

    public static AdKillSwitch[] KillSwitches() => new[]
    {
        new AdKillSwitch { Scope = AdKillSwitch.GlobalScope, ExecutionEnabled = false, UpdatedAt = SeededAt, UpdatedBy = SeededBy },
    };

    private static IEnumerable<AdLimitSetting> LimitsFor(string platform)
    {
        // Meta has no negative keywords (spec 4.3).
        yield return Limit(platform, "AddNegativeKeyword", "Enabled", platform == "MetaAds" ? 0 : 1);
        yield return Limit(platform, "AddNegativeKeyword", "MaxActionsPerProposal", 50);
        yield return Limit(platform, "AddNegativeKeyword", "McpMaxActionsPerProposal", 20);
        yield return Limit(platform, "AddNegativeKeyword", "SecondApproverAbove", 50);
        yield return Limit(platform, "PauseAd", "Enabled", 1);
        yield return Limit(platform, "PauseAd", "MaxActionsPerProposal", 10);
        yield return Limit(platform, "PauseAd", "McpMaxActionsPerProposal", 3);
        yield return Limit(platform, "PauseAd", "MaxPausedAdsPer7Days", 20);
        yield return Limit(platform, "PauseAd", "SecondApproverAbove", 10);
    }

    private static AdLimitSetting Limit(string platform, string actionType, string key, int value) => new()
    {
        Platform = platform,
        ActionType = actionType,
        Key = key,
        Value = value,
        UpdatedAt = SeededAt,
        UpdatedBy = SeededBy,
    };
}
```

`MarketingAdsSql.cs`:

```csharp
namespace Anela.Heblo.Persistence.MarketingAds;

/// <summary>
/// Raw SQL used by the AddMarketingAdsProposals migration and by its integration test.
/// NEVER edit these strings once the migration is merged — add a new constant + migration instead.
/// </summary>
public static class MarketingAdsSql
{
    public const string AppendOnlyTriggersUp = """
        CREATE OR REPLACE FUNCTION public.marketing_ads_reject_mutation() RETURNS trigger
        LANGUAGE plpgsql AS $$
        BEGIN
            RAISE EXCEPTION 'Table %.% is append-only: % is not allowed', TG_TABLE_SCHEMA, TG_TABLE_NAME, TG_OP
                USING ERRCODE = 'P0001';
        END;
        $$;

        CREATE TRIGGER "TR_AdAuditEvents_AppendOnly"
            BEFORE UPDATE OR DELETE ON public."AdAuditEvents"
            FOR EACH ROW EXECUTE FUNCTION public.marketing_ads_reject_mutation();
        CREATE TRIGGER "TR_AdAuditEvents_NoTruncate"
            BEFORE TRUNCATE ON public."AdAuditEvents"
            FOR EACH STATEMENT EXECUTE FUNCTION public.marketing_ads_reject_mutation();

        CREATE TRIGGER "TR_AdProposalVersions_AppendOnly"
            BEFORE UPDATE OR DELETE ON public."AdProposalVersions"
            FOR EACH ROW EXECUTE FUNCTION public.marketing_ads_reject_mutation();
        CREATE TRIGGER "TR_AdProposalVersions_NoTruncate"
            BEFORE TRUNCATE ON public."AdProposalVersions"
            FOR EACH STATEMENT EXECUTE FUNCTION public.marketing_ads_reject_mutation();
        """;

    public const string AppendOnlyTriggersDown = """
        DROP TRIGGER IF EXISTS "TR_AdProposalVersions_NoTruncate" ON public."AdProposalVersions";
        DROP TRIGGER IF EXISTS "TR_AdProposalVersions_AppendOnly" ON public."AdProposalVersions";
        DROP TRIGGER IF EXISTS "TR_AdAuditEvents_NoTruncate" ON public."AdAuditEvents";
        DROP TRIGGER IF EXISTS "TR_AdAuditEvents_AppendOnly" ON public."AdAuditEvents";
        DROP FUNCTION IF EXISTS public.marketing_ads_reject_mutation();
        """;
}
```

- [ ] **Step 4: Add the entity configurations** (namespace `Anela.Heblo.Persistence.MarketingAds`; every file starts with
`using Anela.Heblo.Domain.Features.MarketingAds; using Anela.Heblo.Persistence.Extensions; using Microsoft.EntityFrameworkCore; using Microsoft.EntityFrameworkCore.Metadata.Builders;`)

```csharp
public class AdProposalConfiguration : IEntityTypeConfiguration<AdProposal>
{
    public void Configure(EntityTypeBuilder<AdProposal> builder)
    {
        builder.ToTable("AdProposals", "public");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Platform).HasMaxLength(32).IsRequired();
        builder.Property(x => x.AccountExternalId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ActionType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CreatedByPrincipal).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CreatedByKind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired().AsUtcTimestamp();
        builder.Property(x => x.ExpiresAt).IsRequired().AsUtcTimestamp();
        builder.Property(x => x.UpdatedAt).IsRequired().AsUtcTimestamp();
        builder.Property(x => x.ExecutionStartedAt).AsUtcTimestamp();
        builder.Property(x => x.ExecutionFinishedAt).AsUtcTimestamp();
        builder.Property(x => x.RevertedAt).AsUtcTimestamp();
        // App-managed token (not xmin): portable to the InMemory provider used by handler tests.
        builder.Property(x => x.Revision).IsConcurrencyToken();

        builder.HasMany(x => x.Versions).WithOne().HasForeignKey(v => v.ProposalId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Approvals).WithOne().HasForeignKey(a => a.ProposalId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Executions).WithOne().HasForeignKey(e => e.ProposalId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.Status, x.ExpiresAt });
        builder.HasIndex(x => x.CreatedByPrincipal);
        builder.HasIndex(x => x.AgentRunId);
    }
}

public class AdProposalVersionConfiguration : IEntityTypeConfiguration<AdProposalVersion>
{
    public void Configure(EntityTypeBuilder<AdProposalVersion> builder)
    {
        builder.ToTable("AdProposalVersions", "public");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.ActionsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Reasoning).HasColumnType("text").IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired().AsUtcTimestamp();
        builder.HasIndex(x => new { x.ProposalId, x.Version }).IsUnique();
    }
}

public class AdProposalApprovalConfiguration : IEntityTypeConfiguration<AdProposalApproval>
{
    public void Configure(EntityTypeBuilder<AdProposalApproval> builder)
    {
        builder.ToTable("AdProposalApprovals", "public");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.Principal).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Channel).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Decision).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(2000);
        builder.Property(x => x.At).IsRequired().AsUtcTimestamp();
        // One decision per principal per version.
        builder.HasIndex(x => new { x.ProposalId, x.Version, x.Principal }).IsUnique();
    }
}

public class AdProposalActionExecutionConfiguration : IEntityTypeConfiguration<AdProposalActionExecution>
{
    public void Configure(EntityTypeBuilder<AdProposalActionExecution> builder)
    {
        builder.ToTable("AdProposalActionExecutions", "public");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.Platform).HasMaxLength(32).IsRequired();
        builder.Property(x => x.AccountExternalId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ActionType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.TargetLevel).HasMaxLength(32).IsRequired();
        builder.Property(x => x.TargetExternalId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.State).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.BeforeValue).HasMaxLength(64);
        builder.Property(x => x.BeforeRawJson).HasColumnType("text");
        builder.Property(x => x.AfterValue).HasMaxLength(64);
        builder.Property(x => x.PlatformResourceId).HasMaxLength(200);
        builder.Property(x => x.PlatformResponseJson).HasColumnType("text");
        builder.Property(x => x.Error).HasColumnType("text");
        builder.Property(x => x.ExecutedAt).IsRequired().AsUtcTimestamp();
        builder.Property(x => x.RevertState).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.RevertedAt).AsUtcTimestamp();
        builder.Property(x => x.RevertResponseJson).HasColumnType("text");
        builder.Property(x => x.RevertError).HasColumnType("text");
        builder.HasIndex(x => new { x.ProposalId, x.Version, x.ActionIndex }).IsUnique();
        builder.HasIndex(x => new { x.Platform, x.AccountExternalId, x.ExecutedAt });
    }
}

public class AdAuditEventConfiguration : IEntityTypeConfiguration<AdAuditEvent>
{
    public void Configure(EntityTypeBuilder<AdAuditEvent> builder)
    {
        builder.ToTable("AdAuditEvents", "public");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.EventType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.ActorPrincipal).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ActorKind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Channel).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.OccurredAt).IsRequired().AsUtcTimestamp();
        builder.HasOne(x => x.Proposal).WithMany().HasForeignKey(x => x.ProposalId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.ProposalId);
        builder.HasIndex(x => x.OccurredAt);
    }
}

public class AgentRunConfiguration : IEntityTypeConfiguration<AgentRun>
{
    public void Configure(EntityTypeBuilder<AgentRun> builder)
    {
        builder.ToTable("AgentRuns", "public");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.AgentName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Principal).HasMaxLength(200).IsRequired();
        builder.Property(x => x.StartedAt).IsRequired().AsUtcTimestamp();
        builder.Property(x => x.FinishedAt).AsUtcTimestamp();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Summary).HasColumnType("text");
        builder.Property(x => x.DataScopesReadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CostCzk).HasColumnType("numeric(18,2)");
        builder.HasIndex(x => x.StartedAt);
    }
}

public class AdAutonomySettingConfiguration : IEntityTypeConfiguration<AdAutonomySetting>
{
    public void Configure(EntityTypeBuilder<AdAutonomySetting> builder)
    {
        builder.ToTable("AdAutonomySettings", "public");
        builder.HasKey(x => new { x.Platform, x.ActionType });
        builder.Property(x => x.Platform).HasMaxLength(32);
        builder.Property(x => x.ActionType).HasMaxLength(64);
        builder.Property(x => x.Mode).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired().AsUtcTimestamp();
        builder.Property(x => x.UpdatedBy).HasMaxLength(200).IsRequired();
        builder.HasData(MarketingAdsSeedData.Autonomy());
    }
}

public class AdLimitSettingConfiguration : IEntityTypeConfiguration<AdLimitSetting>
{
    public void Configure(EntityTypeBuilder<AdLimitSetting> builder)
    {
        builder.ToTable("AdLimitSettings", "public");
        builder.HasKey(x => new { x.Platform, x.ActionType, x.Key });
        builder.Property(x => x.Platform).HasMaxLength(32);
        builder.Property(x => x.ActionType).HasMaxLength(64);
        builder.Property(x => x.Key).HasMaxLength(64);
        builder.Property(x => x.UpdatedAt).IsRequired().AsUtcTimestamp();
        builder.Property(x => x.UpdatedBy).HasMaxLength(200).IsRequired();
        builder.HasData(MarketingAdsSeedData.Limits());
    }
}

public class AdKillSwitchConfiguration : IEntityTypeConfiguration<AdKillSwitch>
{
    public void Configure(EntityTypeBuilder<AdKillSwitch> builder)
    {
        builder.ToTable("AdKillSwitches", "public");
        builder.HasKey(x => x.Scope);
        builder.Property(x => x.Scope).HasMaxLength(32);
        builder.Property(x => x.UpdatedAt).IsRequired().AsUtcTimestamp();
        builder.Property(x => x.UpdatedBy).HasMaxLength(200).IsRequired();
        builder.HasData(MarketingAdsSeedData.KillSwitches());
    }
}
```

Put each class in its own file named after the class.

- [ ] **Step 5: Register the DbSets** — in `ApplicationDbContext.cs` add `using Anela.Heblo.Domain.Features.MarketingAds;` and, after the `// Marketing Performance module` DbSets:

```csharp
    // Marketing Ads — proposal & approval layer (PR C3)
    public DbSet<AdProposal> AdProposals { get; set; } = null!;
    public DbSet<AdProposalVersion> AdProposalVersions { get; set; } = null!;
    public DbSet<AdProposalApproval> AdProposalApprovals { get; set; } = null!;
    public DbSet<AdProposalActionExecution> AdProposalActionExecutions { get; set; } = null!;
    public DbSet<AdAuditEvent> AdAuditEvents { get; set; } = null!;
    public DbSet<AgentRun> AgentRuns { get; set; } = null!;
    public DbSet<AdAutonomySetting> AdAutonomySettings { get; set; } = null!;
    public DbSet<AdLimitSetting> AdLimitSettings { get; set; } = null!;
    public DbSet<AdKillSwitch> AdKillSwitches { get; set; } = null!;
```

- [ ] **Step 6: Generate the migration** (design-time factory needs *a* connection string; `migrations add` never connects):

```bash
MSBUILDDISABLENODEREUSE=1 dotnet build backend/src/Anela.Heblo.API -p:UseSharedCompilation=false
ConnectionStrings__Development="Host=localhost;Database=design_time_only;Username=x;Password=x" \
  dotnet ef migrations add AddMarketingAdsProposals \
  --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API
```

Expected: `Done.` and three changed files under `Migrations/`. Open the new `<timestamp>_AddMarketingAdsProposals.cs` and check: it creates exactly the 9 tables, their indexes and `InsertData` for 6 + 27 + 1 rows, every DateTime column is `type: "timestamp"`, and it alters **no** other table. If it touches other tables, stop — main has model drift; report it instead of committing it.

- [ ] **Step 7: Add the trigger SQL to the migration** — add `using Anela.Heblo.Persistence.MarketingAds;` at the top; make the **last** statement of `Up`:

```csharp
            migrationBuilder.Sql(MarketingAdsSql.AppendOnlyTriggersUp);
```

and the **first** statement of `Down`:

```csharp
            migrationBuilder.Sql(MarketingAdsSql.AppendOnlyTriggersDown);
```

- [ ] **Step 8: Run the integration tests**

```bash
podman machine start
MSBUILDDISABLENODEREUSE=1 dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~MarketingAdsProposalsMigrationIntegrationTests"
```

Expected: PASS — 8 tests (1 seed + 5 append-only + round trip + concurrency).

- [ ] **Step 9: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence backend/test/Anela.Heblo.Tests/Features/MarketingAds/Proposals/Persistence
git commit -m "feat: persist ad proposals, settings, audit and agent runs with append-only trigger

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 4: Repositories

Repository bindings live in the feature module (ADR-004) — Task 8 registers them. All repositories share the scoped `ApplicationDbContext`, so an audit row appended through `IAdAuditLog` is saved by the next `SaveChangesAsync` on any of them, in the same implicit transaction.

**Files:**
- Create (Domain, `backend/src/Anela.Heblo.Domain/Features/MarketingAds/`): `IAdProposalRepository.cs`, `IAdGovernanceRepository.cs`, `IAgentRunRepository.cs`, `IAdAuditLog.cs`
- Create (Persistence, `backend/src/Anela.Heblo.Persistence/MarketingAds/`): `AdProposalRepository.cs`, `AdGovernanceRepository.cs`, `AgentRunRepository.cs`, `AdAuditLog.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/Proposals/Persistence/AdProposalRepositoryTests.cs`

**Interfaces:**
- Produces (Domain):

```csharp
public sealed class AdProposalQuery
{
    public AdProposalStatus? Status { get; init; }
    public string? Platform { get; init; }
    public string? CreatedByPrincipal { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}
public sealed record AdApprovalOutcomeCount(string Platform, string ActionType, int Decided, int ApprovedWithoutEdit);

public interface IAdProposalRepository
{
    void Add(AdProposal proposal);
    Task<AdProposal?> GetForUpdateAsync(int id, CancellationToken cancellationToken);
    Task<AdProposal?> GetReadOnlyAsync(int id, CancellationToken cancellationToken);
    Task<(IReadOnlyList<AdProposal> Items, int TotalCount)> ListAsync(AdProposalQuery query, CancellationToken cancellationToken);
    Task<int> CountSucceededActionsSinceAsync(string platform, string accountExternalId, string actionType, DateTime sinceUtc, CancellationToken cancellationToken);
    Task<int> CountProposalsForRunAsync(Guid agentRunId, CancellationToken cancellationToken);
    Task<IReadOnlyList<int>> GetPendingExpiredIdsAsync(DateTime nowUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<int>> GetStuckApprovedIdsAsync(DateTime updatedBeforeUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdProposalActionExecution>> GetExecutionsInWindowAsync(string platform, string accountExternalId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdApprovalOutcomeCount>> GetApprovalOutcomeCountsAsync(CancellationToken cancellationToken);
    void DiscardChanges();
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IAdGovernanceRepository
{
    Task<IReadOnlyList<AdAutonomySetting>> GetAutonomySettingsAsync(CancellationToken cancellationToken);   // AsNoTracking
    Task<IReadOnlyList<AdLimitSetting>> GetLimitSettingsAsync(string? platform, string? actionType, CancellationToken cancellationToken); // AsNoTracking
    Task<IReadOnlyList<AdKillSwitch>> GetKillSwitchesAsync(CancellationToken cancellationToken);           // AsNoTracking
    Task<AdAutonomySetting?> FindAutonomySettingAsync(string platform, string actionType, CancellationToken cancellationToken); // tracked
    Task<AdLimitSetting?> FindLimitSettingAsync(string platform, string actionType, string key, CancellationToken cancellationToken); // tracked
    Task<AdKillSwitch?> FindKillSwitchAsync(string scope, CancellationToken cancellationToken);             // tracked
    void AddAutonomySetting(AdAutonomySetting setting);
    void AddLimitSetting(AdLimitSetting setting);
    void AddKillSwitch(AdKillSwitch killSwitch);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IAgentRunRepository
{
    void Add(AgentRun run);
    Task<AgentRun?> FindAsync(Guid id, CancellationToken cancellationToken);    // tracked
    Task<(IReadOnlyList<AgentRun> Items, int TotalCount)> ListAsync(int page, int pageSize, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed class AdAuditQuery
{
    public int? ProposalId { get; init; }
    public AdAuditEventType? EventType { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

public interface IAdAuditLog
{
    void Append(AdAuditEvent auditEvent);
    Task<(IReadOnlyList<AdAuditEvent> Items, int TotalCount)> ListAsync(AdAuditQuery query, CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Write the failing tests** (`AdProposalRepositoryTests.cs`; persistence verified through a **second** context — asserting through the same context reads the tracked instance and proves nothing)

```csharp
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.MarketingAds;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Persistence;

public class AdProposalRepositoryTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc);
    private readonly DbContextOptions<ApplicationDbContext> _options =
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
    private readonly ApplicationDbContext _context;

    public AdProposalRepositoryTests()
    {
        _context = new ApplicationDbContext(_options);
        _context.Database.EnsureCreated(); // applies HasData seeds on InMemory
    }

    public void Dispose() => _context.Dispose();

    private static AdProposal Proposal(string creator = "agent-1", string platform = "GoogleAds") => AdProposal.Create(
        new AdProposalDraft(platform, "acc-1", "PauseAd", creator, AdActorKind.Agent, null, "[]", 1, "r"),
        Now, TimeSpan.FromHours(72));

    private static AdProposalActionExecution Execution(AdActionExecutionState state, DateTime at) => new()
    {
        Version = 1, ActionIndex = 0, Platform = "GoogleAds", AccountExternalId = "acc-1", ActionType = "PauseAd",
        TargetLevel = "Ad", TargetExternalId = "ad-1", State = state, ExecutedAt = at,
    };

    [Fact]
    public async Task Add_ThenGetForUpdate_ReturnsChildrenFromASecondContext()
    {
        var repo = new AdProposalRepository(_context);
        var proposal = Proposal();
        repo.Add(proposal);
        await repo.SaveChangesAsync(CancellationToken.None);

        await using var verify = new ApplicationDbContext(_options);
        var loaded = await new AdProposalRepository(verify).GetForUpdateAsync(proposal.Id, CancellationToken.None);
        loaded!.Versions.Should().ContainSingle();
        loaded.GetCurrentVersion().Reasoning.Should().Be("r");
    }

    [Fact]
    public async Task ListAsync_FiltersByCreatorAndPages()
    {
        var repo = new AdProposalRepository(_context);
        repo.Add(Proposal("agent-1"));
        repo.Add(Proposal("agent-2"));
        repo.Add(Proposal("agent-1"));
        await repo.SaveChangesAsync(CancellationToken.None);

        var (items, total) = await repo.ListAsync(new AdProposalQuery { CreatedByPrincipal = "agent-1", PageSize = 1 }, CancellationToken.None);

        total.Should().Be(2);
        items.Should().ContainSingle().Which.CreatedByPrincipal.Should().Be("agent-1");
    }

    [Fact]
    public async Task CountSucceededActionsSince_CountsOnlySucceededInsideTheWindow()
    {
        var repo = new AdProposalRepository(_context);
        var proposal = Proposal();
        proposal.RecordExecution(Execution(AdActionExecutionState.Succeeded, Now.AddDays(-1)));
        proposal.RecordExecution(Execution(AdActionExecutionState.Failed, Now.AddDays(-1)));
        proposal.RecordExecution(Execution(AdActionExecutionState.Succeeded, Now.AddDays(-8)));
        repo.Add(proposal);
        await repo.SaveChangesAsync(CancellationToken.None);

        var count = await repo.CountSucceededActionsSinceAsync("GoogleAds", "acc-1", "PauseAd", Now.AddDays(-7), CancellationToken.None);

        count.Should().Be(1);
    }

    [Fact]
    public async Task GetPendingExpiredIds_ReturnsOnlyPendingPastExpiry()
    {
        var repo = new AdProposalRepository(_context);
        var expired = Proposal();
        var approved = Proposal();
        approved.RecordApproval("chief", ApprovalChannel.Web, Now, isFinal: true);
        repo.Add(expired);
        repo.Add(approved);
        await repo.SaveChangesAsync(CancellationToken.None);

        var ids = await repo.GetPendingExpiredIdsAsync(Now.AddHours(73), CancellationToken.None);

        ids.Should().Equal(expired.Id);
    }

    [Fact]
    public async Task GetLimitSettings_IsNotTracked_SoAuditBeforeValuesCannotAlias()
    {
        var governance = new AdGovernanceRepository(_context);

        var limits = await governance.GetLimitSettingsAsync("GoogleAds", "PauseAd", CancellationToken.None);

        limits.Should().HaveCount(5);
        _context.ChangeTracker.Entries<AdLimitSetting>().Should().BeEmpty();
    }

    [Fact]
    public async Task AuditLog_ListAsync_FiltersByProposalNewestFirst()
    {
        var repo = new AdProposalRepository(_context);
        var audit = new AdAuditLog(_context);
        var proposal = Proposal();
        repo.Add(proposal);
        audit.Append(new AdAuditEvent { Proposal = proposal, Version = 1, EventType = AdAuditEventType.Submitted, ActorPrincipal = "a", ActorKind = AdActorKind.Agent, Channel = ApprovalChannel.Mcp, OccurredAt = Now });
        audit.Append(new AdAuditEvent { Proposal = proposal, Version = 1, EventType = AdAuditEventType.Approved, ActorPrincipal = "c", ActorKind = AdActorKind.Human, Channel = ApprovalChannel.Web, OccurredAt = Now.AddMinutes(1) });
        audit.Append(new AdAuditEvent { EventType = AdAuditEventType.KillSwitchChanged, ActorPrincipal = "admin", ActorKind = AdActorKind.Human, Channel = ApprovalChannel.Web, OccurredAt = Now });
        await repo.SaveChangesAsync(CancellationToken.None);

        await using var verify = new ApplicationDbContext(_options);
        var (items, total) = await new AdAuditLog(verify).ListAsync(new AdAuditQuery { ProposalId = proposal.Id }, CancellationToken.None);

        total.Should().Be(2);
        items.Select(e => e.EventType).Should().Equal(AdAuditEventType.Approved, AdAuditEventType.Submitted);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `MSBUILDDISABLENODEREUSE=1 dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false`
Expected: FAIL — `The type or namespace name 'AdProposalRepository' could not be found`.

- [ ] **Step 3: Create the four Domain interfaces** exactly as listed under **Interfaces** (one file per interface; put `AdProposalQuery` and `AdApprovalOutcomeCount` in `IAdProposalRepository.cs`, `AdAuditQuery` in `IAdAuditLog.cs`; namespace `Anela.Heblo.Domain.Features.MarketingAds`).

- [ ] **Step 4: Implement the repositories**

`AdProposalRepository.cs`:

```csharp
using Anela.Heblo.Domain.Features.MarketingAds;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.MarketingAds;

public class AdProposalRepository : IAdProposalRepository
{
    private const int MaxPageSize = 200;
    private readonly ApplicationDbContext _context;

    public AdProposalRepository(ApplicationDbContext context) => _context = context;

    public void Add(AdProposal proposal) => _context.AdProposals.Add(proposal);

    public Task<AdProposal?> GetForUpdateAsync(int id, CancellationToken cancellationToken) =>
        WithChildren(_context.AdProposals).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<AdProposal?> GetReadOnlyAsync(int id, CancellationToken cancellationToken) =>
        WithChildren(_context.AdProposals.AsNoTracking()).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<AdProposal> Items, int TotalCount)> ListAsync(AdProposalQuery query, CancellationToken cancellationToken)
    {
        var proposals = _context.AdProposals.AsNoTracking();
        if (query.Status is { } status) proposals = proposals.Where(p => p.Status == status);
        if (query.Platform is { } platform) proposals = proposals.Where(p => p.Platform == platform);
        if (query.CreatedByPrincipal is { } creator) proposals = proposals.Where(p => p.CreatedByPrincipal == creator);

        var total = await proposals.CountAsync(cancellationToken);
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var items = await proposals
            .OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
            .Skip((page - 1) * size).Take(size)
            .Include(p => p.Versions)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<int> CountSucceededActionsSinceAsync(string platform, string accountExternalId, string actionType, DateTime sinceUtc, CancellationToken cancellationToken) =>
        _context.AdProposalActionExecutions.AsNoTracking().CountAsync(e =>
            e.Platform == platform && e.AccountExternalId == accountExternalId && e.ActionType == actionType
            && e.State == AdActionExecutionState.Succeeded && e.ExecutedAt >= sinceUtc, cancellationToken);

    public Task<int> CountProposalsForRunAsync(Guid agentRunId, CancellationToken cancellationToken) =>
        _context.AdProposals.AsNoTracking().CountAsync(p => p.AgentRunId == agentRunId, cancellationToken);

    public async Task<IReadOnlyList<int>> GetPendingExpiredIdsAsync(DateTime nowUtc, CancellationToken cancellationToken) =>
        await _context.AdProposals.AsNoTracking()
            .Where(p => p.Status == AdProposalStatus.Pending && p.ExpiresAt <= nowUtc)
            .Select(p => p.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<int>> GetStuckApprovedIdsAsync(DateTime updatedBeforeUtc, CancellationToken cancellationToken) =>
        await _context.AdProposals.AsNoTracking()
            .Where(p => p.Status == AdProposalStatus.Approved && p.UpdatedAt <= updatedBeforeUtc)
            .Select(p => p.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AdProposalActionExecution>> GetExecutionsInWindowAsync(
        string platform, string accountExternalId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken) =>
        await _context.AdProposalActionExecutions.AsNoTracking()
            .Where(e => e.Platform == platform && e.AccountExternalId == accountExternalId
                && ((e.State == AdActionExecutionState.Succeeded && e.ExecutedAt >= fromUtc && e.ExecutedAt <= toUtc)
                    || (e.RevertState == AdActionRevertState.Reverted && e.RevertedAt >= fromUtc && e.RevertedAt <= toUtc)))
            .OrderBy(e => e.ExecutedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AdApprovalOutcomeCount>> GetApprovalOutcomeCountsAsync(CancellationToken cancellationToken)
    {
        var rows = await _context.AdProposals.AsNoTracking()
            .Where(p => p.Status != AdProposalStatus.Pending && p.Status != AdProposalStatus.Expired)
            .GroupBy(p => new { p.Platform, p.ActionType })
            .Select(g => new
            {
                g.Key.Platform,
                g.Key.ActionType,
                Decided = g.Count(),
                WithoutEdit = g.Count(p => p.Status != AdProposalStatus.Rejected && p.CurrentVersion == 1),
            })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new AdApprovalOutcomeCount(r.Platform, r.ActionType, r.Decided, r.WithoutEdit)).ToList();
    }

    /// <summary>Drops every tracked entity. Call after a failed SaveChanges so the shared context is not poisoned.</summary>
    public void DiscardChanges() => _context.ChangeTracker.Clear();

    public Task SaveChangesAsync(CancellationToken cancellationToken) => _context.SaveChangesAsync(cancellationToken);

    private static IQueryable<AdProposal> WithChildren(IQueryable<AdProposal> query) => query
        .Include(p => p.Versions)
        .Include(p => p.Approvals)
        .Include(p => p.Executions)
        .AsSplitQuery();
}
```

`AdGovernanceRepository.cs`:

```csharp
using Anela.Heblo.Domain.Features.MarketingAds;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.MarketingAds;

public class AdGovernanceRepository : IAdGovernanceRepository
{
    private readonly ApplicationDbContext _context;

    public AdGovernanceRepository(ApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<AdAutonomySetting>> GetAutonomySettingsAsync(CancellationToken cancellationToken) =>
        await _context.AdAutonomySettings.AsNoTracking()
            .OrderBy(s => s.Platform).ThenBy(s => s.ActionType).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AdLimitSetting>> GetLimitSettingsAsync(string? platform, string? actionType, CancellationToken cancellationToken)
    {
        var limits = _context.AdLimitSettings.AsNoTracking();
        if (platform is not null) limits = limits.Where(l => l.Platform == platform);
        if (actionType is not null) limits = limits.Where(l => l.ActionType == actionType);
        return await limits.OrderBy(l => l.Platform).ThenBy(l => l.ActionType).ThenBy(l => l.Key).ToListAsync(cancellationToken);
    }

    // Read fresh on every call (no tracking): the execution job re-reads before each action.
    public async Task<IReadOnlyList<AdKillSwitch>> GetKillSwitchesAsync(CancellationToken cancellationToken) =>
        await _context.AdKillSwitches.AsNoTracking().OrderBy(s => s.Scope).ToListAsync(cancellationToken);

    public Task<AdAutonomySetting?> FindAutonomySettingAsync(string platform, string actionType, CancellationToken cancellationToken) =>
        _context.AdAutonomySettings.FirstOrDefaultAsync(s => s.Platform == platform && s.ActionType == actionType, cancellationToken);

    public Task<AdLimitSetting?> FindLimitSettingAsync(string platform, string actionType, string key, CancellationToken cancellationToken) =>
        _context.AdLimitSettings.FirstOrDefaultAsync(l => l.Platform == platform && l.ActionType == actionType && l.Key == key, cancellationToken);

    public Task<AdKillSwitch?> FindKillSwitchAsync(string scope, CancellationToken cancellationToken) =>
        _context.AdKillSwitches.FirstOrDefaultAsync(s => s.Scope == scope, cancellationToken);

    public void AddAutonomySetting(AdAutonomySetting setting) => _context.AdAutonomySettings.Add(setting);

    public void AddLimitSetting(AdLimitSetting setting) => _context.AdLimitSettings.Add(setting);

    public void AddKillSwitch(AdKillSwitch killSwitch) => _context.AdKillSwitches.Add(killSwitch);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => _context.SaveChangesAsync(cancellationToken);
}
```

`AgentRunRepository.cs`:

```csharp
using Anela.Heblo.Domain.Features.MarketingAds;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.MarketingAds;

public class AgentRunRepository : IAgentRunRepository
{
    private const int MaxPageSize = 200;
    private readonly ApplicationDbContext _context;

    public AgentRunRepository(ApplicationDbContext context) => _context = context;

    public void Add(AgentRun run) => _context.AgentRuns.Add(run);

    public Task<AgentRun?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        _context.AgentRuns.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<AgentRun> Items, int TotalCount)> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var runs = _context.AgentRuns.AsNoTracking();
        var total = await runs.CountAsync(cancellationToken);
        var size = Math.Clamp(pageSize, 1, MaxPageSize);
        var items = await runs.OrderByDescending(r => r.StartedAt)
            .Skip((Math.Max(1, page) - 1) * size).Take(size).ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => _context.SaveChangesAsync(cancellationToken);
}
```

`AdAuditLog.cs`:

```csharp
using Anela.Heblo.Domain.Features.MarketingAds;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.MarketingAds;

/// <summary>Append-only access to AdAuditEvents. Rows are saved by the caller's next SaveChanges on the shared context.</summary>
public class AdAuditLog : IAdAuditLog
{
    private const int MaxPageSize = 200;
    private readonly ApplicationDbContext _context;

    public AdAuditLog(ApplicationDbContext context) => _context = context;

    public void Append(AdAuditEvent auditEvent) => _context.AdAuditEvents.Add(auditEvent);

    public async Task<(IReadOnlyList<AdAuditEvent> Items, int TotalCount)> ListAsync(AdAuditQuery query, CancellationToken cancellationToken)
    {
        var events = _context.AdAuditEvents.AsNoTracking();
        if (query.ProposalId is { } proposalId) events = events.Where(e => e.ProposalId == proposalId);
        if (query.EventType is { } type) events = events.Where(e => e.EventType == type);
        var total = await events.CountAsync(cancellationToken);
        var size = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var items = await events.OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id)
            .Skip((Math.Max(1, query.Page) - 1) * size).Take(size).ToListAsync(cancellationToken);
        return (items, total);
    }
}
```

- [ ] **Step 5: Run the tests**

```bash
MSBUILDDISABLENODEREUSE=1 dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~AdProposalRepositoryTests"
```

Expected: PASS (6 tests).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/MarketingAds backend/src/Anela.Heblo.Persistence/MarketingAds backend/test/Anela.Heblo.Tests/Features/MarketingAds/Proposals/Persistence/AdProposalRepositoryTests.cs
git commit -m "feat: repositories for ad proposals, governance settings, agent runs and audit log

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 5: Action model — input DTO, factory, JSON, shape validation, diff renderer

Proposers send only the *target* of each action; Heblo fills `OldValue`/`NewValue`/payload from the spec 12.2 conventions, so an agent cannot propose an arbitrary "old value". The diff renderer takes `AdAction`s and entity names only — it has no parameter through which `reasoning` could reach it.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingAds/Proposals/Contracts/AdActionInputDto.cs`
- Create (`.../Proposals/Actions/`): `AdActionFactory.cs`, `AdActionJson.cs`, `AdProposalShapeValidator.cs`, `AdProposalDiffRenderer.cs`
- Test (`backend/test/Anela.Heblo.Tests/Features/MarketingAds/Proposals/Actions/`): `AdActionJsonTests.cs`, `AdProposalShapeValidatorTests.cs`, `AdProposalDiffRendererTests.cs`

**Interfaces:**
- Consumes: C1 `AdAction`, `AdActionValues`, `AdActionPayloadKeys`, enums.
- Produces:
  - `class AdActionInputDto { AdEntityLevel TargetLevel; string TargetExternalId; string? KeywordText; KeywordMatchType? MatchType }` (namespace `...Proposals.Contracts`)
  - `AdActionFactory.Create(AdPlatform platform, string accountExternalId, AdActionType type, AdActionInputDto input) : AdAction`
  - `AdActionJson.Serialize(IReadOnlyList<AdAction>) : string`, `AdActionJson.Deserialize(string) : IReadOnlyList<AdAction>`
  - `AdProposalShapeValidator.Validate(AdPlatform platform, string? accountExternalId, AdActionType actionType, IReadOnlyList<AdActionInputDto>? actions, string? reasoning) : IReadOnlyList<string>`; constant `MaxActionsPerProposal = 50`
  - `AdProposalDiffRenderer.Render(IReadOnlyList<AdAction>, IReadOnlyDictionary<string,string> entityNames) : IReadOnlyList<string>`, `RenderLine(AdAction, IReadOnlyDictionary<string,string>) : string`

- [ ] **Step 1: Write the failing tests**

`AdActionJsonTests.cs` (assertions parse JSON — never substring-match the wire shape):

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Contracts;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Actions;

public class AdActionJsonTests
{
    [Fact]
    public void RoundTrip_PreservesEveryField()
    {
        var action = AdActionFactory.Create(AdPlatform.Sklik, "acc-1", AdActionType.AddNegativeKeyword,
            new AdActionInputDto { TargetLevel = AdEntityLevel.AdGroup, TargetExternalId = " grp-1 ", KeywordText = " zdarma ", MatchType = KeywordMatchType.Phrase });

        var restored = AdActionJson.Deserialize(AdActionJson.Serialize(new[] { action })).Single();

        restored.Type.Should().Be(AdActionType.AddNegativeKeyword);
        restored.Platform.Should().Be(AdPlatform.Sklik);
        restored.TargetLevel.Should().Be(AdEntityLevel.AdGroup);
        restored.TargetExternalId.Should().Be("grp-1");
        restored.OldValue.Should().Be(AdActionValues.Absent);
        restored.NewValue.Should().Be(AdActionValues.Present);
        restored.Payload[AdActionPayloadKeys.Text].Should().Be("zdarma");
        restored.Payload[AdActionPayloadKeys.MatchType].Should().Be("Phrase");
    }

    [Fact]
    public void Serialize_WritesEnumsByName()
    {
        var action = AdActionFactory.Create(AdPlatform.GoogleAds, "acc-1", AdActionType.PauseAd,
            new AdActionInputDto { TargetLevel = AdEntityLevel.Ad, TargetExternalId = "ad-1" });

        using var doc = JsonDocument.Parse(AdActionJson.Serialize(new[] { action }));
        var element = doc.RootElement[0];

        element.GetProperty("type").GetString().Should().Be("PauseAd");
        element.GetProperty("platform").GetString().Should().Be("GoogleAds");
        element.GetProperty("oldValue").GetString().Should().Be("Enabled");
        element.GetProperty("newValue").GetString().Should().Be("Paused");
        element.GetProperty("payload").EnumerateObject().Should().BeEmpty();
    }
}
```

`AdProposalShapeValidatorTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Contracts;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Actions;

public class AdProposalShapeValidatorTests
{
    private static AdActionInputDto Pause(string id) => new() { TargetLevel = AdEntityLevel.Ad, TargetExternalId = id };
    private static AdActionInputDto Negative(string text, AdEntityLevel level = AdEntityLevel.Campaign) =>
        new() { TargetLevel = level, TargetExternalId = "cmp-1", KeywordText = text, MatchType = KeywordMatchType.Exact };

    private static IReadOnlyList<string> Validate(AdActionType type, IReadOnlyList<AdActionInputDto>? actions, string? reasoning = "why", string? account = "acc-1") =>
        AdProposalShapeValidator.Validate(AdPlatform.GoogleAds, account, type, actions, reasoning);

    [Fact]
    public void ValidPause_HasNoErrors() => Validate(AdActionType.PauseAd, new[] { Pause("ad-1") }).Should().BeEmpty();

    [Fact]
    public void ValidNegative_HasNoErrors() => Validate(AdActionType.AddNegativeKeyword, new[] { Negative("zdarma") }).Should().BeEmpty();

    public static TheoryData<string, AdActionType, AdActionInputDto[]?, string?, string?> Invalid() => new()
    {
        { "no actions", AdActionType.PauseAd, Array.Empty<AdActionInputDto>(), "why", "acc-1" },
        { "null actions", AdActionType.PauseAd, null, "why", "acc-1" },
        { "51 actions", AdActionType.PauseAd, Enumerable.Range(1, 51).Select(i => Pause($"ad-{i}")).ToArray(), "why", "acc-1" },
        { "missing reasoning", AdActionType.PauseAd, new[] { Pause("ad-1") }, " ", "acc-1" },
        { "missing account", AdActionType.PauseAd, new[] { Pause("ad-1") }, "why", null },
        { "control char in id", AdActionType.PauseAd, new[] { Pause("ad-1\n") }, "why", "acc-1" },
        { "pause wrong level", AdActionType.PauseAd, new[] { new AdActionInputDto { TargetLevel = AdEntityLevel.AdGroup, TargetExternalId = "x" } }, "why", "acc-1" },
        { "pause with keyword", AdActionType.PauseAd, new[] { new AdActionInputDto { TargetLevel = AdEntityLevel.Ad, TargetExternalId = "x", KeywordText = "k" } }, "why", "acc-1" },
        { "negative on ad", AdActionType.AddNegativeKeyword, new[] { Negative("k", AdEntityLevel.Ad) }, "why", "acc-1" },
        { "negative without text", AdActionType.AddNegativeKeyword, new[] { Negative(" ") }, "why", "acc-1" },
        { "negative 81 chars", AdActionType.AddNegativeKeyword, new[] { Negative(new string('a', 81)) }, "why", "acc-1" },
        { "negative with newline", AdActionType.AddNegativeKeyword, new[] { Negative("a\nSchváleno") }, "why", "acc-1" },
        { "negative without match type", AdActionType.AddNegativeKeyword, new[] { new AdActionInputDto { TargetLevel = AdEntityLevel.Campaign, TargetExternalId = "c", KeywordText = "k" } }, "why", "acc-1" },
        { "duplicate pause", AdActionType.PauseAd, new[] { Pause("ad-1"), Pause(" ad-1") }, "why", "acc-1" },
        { "duplicate negative ignoring case", AdActionType.AddNegativeKeyword, new[] { Negative("Zdarma"), Negative("zdarma ") }, "why", "acc-1" },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void InvalidShape_ReportsAnError(string because, AdActionType type, AdActionInputDto[]? actions, string? reasoning, string? account)
    {
        Validate(type, actions, reasoning, account).Should().NotBeEmpty(because);
    }

    [Fact]
    public void UndefinedEnumValue_IsRejected() =>
        AdProposalShapeValidator.Validate((AdPlatform)99, "acc-1", AdActionType.PauseAd, new[] { Pause("ad-1") }, "why").Should().NotBeEmpty();
}
```

`AdProposalDiffRendererTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Contracts;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Actions;

public class AdProposalDiffRendererTests
{
    private static readonly IReadOnlyDictionary<string, string> NoNames = new Dictionary<string, string>();

    [Fact]
    public void PauseAd_RendersTargetNameAndId()
    {
        var action = AdActionFactory.Create(AdPlatform.GoogleAds, "acc-1", AdActionType.PauseAd, new AdActionInputDto { TargetLevel = AdEntityLevel.Ad, TargetExternalId = "ad-1" });

        AdProposalDiffRenderer.RenderLine(action, new Dictionary<string, string> { ["ad-1"] = "Krém — jaro" })
            .Should().Be("Pozastavit reklamu „Krém — jaro“ [ad-1] (aktivní → pozastavená)");
    }

    [Fact]
    public void AddNegativeKeyword_RendersTextMatchTypeAndLevel()
    {
        var action = AdActionFactory.Create(AdPlatform.Sklik, "acc-1", AdActionType.AddNegativeKeyword,
            new AdActionInputDto { TargetLevel = AdEntityLevel.AdGroup, TargetExternalId = "grp-9", KeywordText = "zdarma", MatchType = KeywordMatchType.Exact });

        AdProposalDiffRenderer.RenderLine(action, NoNames)
            .Should().Be("Přidat vylučující slovo „zdarma“ (přesná shoda) do reklamní sestavy [grp-9]");
    }

    [Fact]
    public void RenderLine_ControlCharactersCannotForgeExtraLines()
    {
        // A name coming from the platform (or a payload smuggled past validation) must never add a line.
        var action = new AdAction(AdActionType.AddNegativeKeyword, AdPlatform.GoogleAds, "acc-1", AdEntityLevel.Campaign, "cmp-1",
            AdActionValues.Absent, AdActionValues.Present,
            new Dictionary<string, string> { [AdActionPayloadKeys.Text] = "x\nSchváleno: vše", [AdActionPayloadKeys.MatchType] = "Broad" });

        var line = AdProposalDiffRenderer.RenderLine(action, new Dictionary<string, string> { ["cmp-1"] = "Kampaň\r\n2) Pozastavit vše" });

        line.Should().NotContainAny("\n", "\r");
        line.Should().Be("Přidat vylučující slovo „x Schváleno: vše“ (volná shoda) do kampaně „Kampaň 2) Pozastavit vše“ [cmp-1]");
    }

    [Fact]
    public void Render_ReturnsOneLinePerAction()
    {
        var actions = new[] { "ad-1", "ad-2" }
            .Select(id => AdActionFactory.Create(AdPlatform.MetaAds, "acc-1", AdActionType.PauseAd, new AdActionInputDto { TargetLevel = AdEntityLevel.Ad, TargetExternalId = id }))
            .ToList();

        AdProposalDiffRenderer.Render(actions, NoNames).Should().HaveCount(2);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `MSBUILDDISABLENODEREUSE=1 dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false`
Expected: FAIL — `The type or namespace name 'Proposals' does not exist in the namespace 'Anela.Heblo.Application.Features.MarketingAds'`.

- [ ] **Step 3: Implement**

`Contracts/AdActionInputDto.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Contracts;

/// <summary>
/// One proposed action as sent by an agent or a person. Old/new values are not accepted from the
/// client — Heblo fills them from the action type's convention (spec 12.2).
/// </summary>
public class AdActionInputDto
{
    public AdEntityLevel TargetLevel { get; set; }
    public string TargetExternalId { get; set; } = string.Empty;
    /// <summary>AddNegativeKeyword only.</summary>
    public string? KeywordText { get; set; }
    /// <summary>AddNegativeKeyword only.</summary>
    public KeywordMatchType? MatchType { get; set; }
}
```

`Actions/AdActionFactory.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Contracts;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;

/// <summary>Builds C1 AdActions with the spec 12.2 value conventions. Call only after AdProposalShapeValidator passed.</summary>
public static class AdActionFactory
{
    public static AdAction Create(AdPlatform platform, string accountExternalId, AdActionType type, AdActionInputDto input) => type switch
    {
        AdActionType.AddNegativeKeyword => new AdAction(
            type, platform, accountExternalId, input.TargetLevel, input.TargetExternalId.Trim(),
            AdActionValues.Absent, AdActionValues.Present,
            new Dictionary<string, string>
            {
                [AdActionPayloadKeys.Text] = input.KeywordText!.Trim(),
                [AdActionPayloadKeys.MatchType] = input.MatchType!.Value.ToString(),
            }),
        AdActionType.PauseAd => new AdAction(
            type, platform, accountExternalId, AdEntityLevel.Ad, input.TargetExternalId.Trim(),
            AdActionValues.Enabled, AdActionValues.Paused, new Dictionary<string, string>()),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Action type has no factory rule."),
    };
}
```

`Actions/AdActionJson.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;

/// <summary>Format of AdProposalVersion.ActionsJson: camelCase, enums by name.</summary>
public static class AdActionJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(IReadOnlyList<AdAction> actions) => JsonSerializer.Serialize(actions, Options);

    public static IReadOnlyList<AdAction> Deserialize(string json) =>
        JsonSerializer.Deserialize<List<AdAction>>(json, Options)
        ?? throw new JsonException("Proposal actions JSON deserialised to null.");
}
```

`Actions/AdProposalShapeValidator.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Contracts;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;

/// <summary>
/// Structural validation of a submitted/revised proposal (spec 6.1: 1–50 actions, single type and account).
/// Per-type and per-channel caps are the limits engine's job, not this validator's.
/// </summary>
public static class AdProposalShapeValidator
{
    public const int MaxActionsPerProposal = 50;
    public const int MaxExternalIdLength = 64;
    public const int MaxKeywordLength = 80;
    public const int MaxReasoningLength = 10_000;

    public static IReadOnlyList<string> Validate(AdPlatform platform, string? accountExternalId, AdActionType actionType,
        IReadOnlyList<AdActionInputDto>? actions, string? reasoning)
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(platform)) errors.Add("Unknown platform.");
        if (!Enum.IsDefined(actionType)) errors.Add("Unknown action type.");
        if (!IsValidId(accountExternalId)) errors.Add($"AccountExternalId is required (max {MaxExternalIdLength} chars, no control characters).");
        if (string.IsNullOrWhiteSpace(reasoning)) errors.Add("Reasoning is required.");
        else if (reasoning.Length > MaxReasoningLength) errors.Add($"Reasoning is longer than {MaxReasoningLength} characters.");

        if (actions is null || actions.Count == 0) errors.Add("At least one action is required.");
        else if (actions.Count > MaxActionsPerProposal) errors.Add($"A proposal holds at most {MaxActionsPerProposal} actions.");
        else errors.AddRange(ValidateActions(actionType, actions));
        return errors;
    }

    private static IEnumerable<string> ValidateActions(AdActionType actionType, IReadOnlyList<AdActionInputDto> actions)
    {
        for (var i = 0; i < actions.Count; i++)
        {
            foreach (var error in ValidateAction(actionType, actions[i]))
            {
                yield return $"Action {i + 1}: {error}";
            }
        }

        var duplicates = actions.Where(a => a is not null)
            .GroupBy(a => DedupKey(actionType, a))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);
        foreach (var key in duplicates)
        {
            yield return $"Duplicate action: {key}.";
        }
    }

    private static IEnumerable<string> ValidateAction(AdActionType actionType, AdActionInputDto? action)
    {
        if (action is null)
        {
            yield return "is empty.";
            yield break;
        }
        if (!IsValidId(action.TargetExternalId)) yield return $"TargetExternalId is required (max {MaxExternalIdLength} chars, no control characters).";
        if (!Enum.IsDefined(action.TargetLevel)) yield return "Unknown TargetLevel.";

        switch (actionType)
        {
            case AdActionType.AddNegativeKeyword:
                if (action.TargetLevel is not (AdEntityLevel.Campaign or AdEntityLevel.AdGroup)) yield return "TargetLevel must be Campaign or AdGroup.";
                if (!IsValidText(action.KeywordText, MaxKeywordLength)) yield return $"KeywordText is required (max {MaxKeywordLength} chars, no control characters).";
                if (action.MatchType is null || !Enum.IsDefined(action.MatchType.Value)) yield return "MatchType is required.";
                break;
            case AdActionType.PauseAd:
                if (action.TargetLevel != AdEntityLevel.Ad) yield return "TargetLevel must be Ad.";
                if (action.KeywordText is not null || action.MatchType is not null) yield return "PauseAd takes no keyword.";
                break;
        }
    }

    private static string DedupKey(AdActionType actionType, AdActionInputDto action) =>
        actionType == AdActionType.AddNegativeKeyword
            ? $"{action.TargetLevel}:{action.TargetExternalId?.Trim()}:{action.KeywordText?.Trim().ToLowerInvariant()}:{action.MatchType}"
            : $"{action.TargetExternalId?.Trim()}";

    private static bool IsValidId(string? value) => IsValidText(value, MaxExternalIdLength);

    private static bool IsValidText(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maxLength && !value.Any(char.IsControl);
}
```

`Actions/AdProposalDiffRenderer.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;

/// <summary>
/// Human-readable diff rendered by Heblo from the structured actions (spec 6.1). There is deliberately
/// no parameter for the proposer's reasoning: the diff can only say what the actions do.
/// </summary>
public static class AdProposalDiffRenderer
{
    private const int MaxValueLength = 120;

    public static IReadOnlyList<string> Render(IReadOnlyList<AdAction> actions, IReadOnlyDictionary<string, string> entityNames) =>
        actions.Select(a => RenderLine(a, entityNames)).ToList();

    public static string RenderLine(AdAction action, IReadOnlyDictionary<string, string> entityNames) => action.Type switch
    {
        AdActionType.AddNegativeKeyword =>
            $"Přidat vylučující slovo „{Clean(Payload(action, AdActionPayloadKeys.Text))}“ ({MatchLabel(Payload(action, AdActionPayloadKeys.MatchType))}) do {LevelLabel(action.TargetLevel)} {Target(action, entityNames)}",
        AdActionType.PauseAd =>
            $"Pozastavit reklamu {Target(action, entityNames)} (aktivní → pozastavená)",
        _ => $"Neznámá akce {action.Type} na {Target(action, entityNames)}",
    };

    /// <summary>One line, no control characters, bounded length.</summary>
    public static string Clean(string value)
    {
        var withoutControls = new string(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        var singleSpaced = string.Join(' ', withoutControls.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return singleSpaced.Length <= MaxValueLength ? singleSpaced : singleSpaced[..MaxValueLength] + "…";
    }

    private static string Target(AdAction action, IReadOnlyDictionary<string, string> entityNames) =>
        entityNames.TryGetValue(action.TargetExternalId, out var name) && !string.IsNullOrWhiteSpace(name)
            ? $"„{Clean(name)}“ [{Clean(action.TargetExternalId)}]"
            : $"[{Clean(action.TargetExternalId)}]";

    private static string Payload(AdAction action, string key) =>
        action.Payload.TryGetValue(key, out var value) ? value : string.Empty;

    private static string MatchLabel(string matchType) => matchType switch
    {
        nameof(KeywordMatchType.Exact) => "přesná shoda",
        nameof(KeywordMatchType.Phrase) => "frázová shoda",
        nameof(KeywordMatchType.Broad) => "volná shoda",
        _ => Clean(matchType),
    };

    private static string LevelLabel(AdEntityLevel level) => level switch
    {
        AdEntityLevel.Campaign => "kampaně",
        AdEntityLevel.AdGroup => "reklamní sestavy",
        _ => level.ToString(),
    };
}
```

- [ ] **Step 4: Run the tests**

```bash
MSBUILDDISABLENODEREUSE=1 dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~MarketingAds.Proposals.Actions"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingAds/Proposals backend/test/Anela.Heblo.Tests/Features/MarketingAds/Proposals/Actions
git commit -m "feat: ad action factory, JSON format, shape validation and diff renderer

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 6: Limits engine (pure, table-driven)

**Files:**
- Create (`backend/src/Anela.Heblo.Application/Features/MarketingAds/Proposals/Limits/`): `AdLimitKeys.cs`, `AdLimitReasons.cs`, `AdLimitsDecision.cs`, `AdLimitsEvaluation.cs`, `AdLimitsRequest.cs`, `IAdLimitsEvaluator.cs`, `AdLimitsEvaluator.cs`
- Create: `.../Proposals/ReadModel/AdGroupOccupancy.cs` (input type of the request)
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/Proposals/Limits/AdLimitsEvaluatorTests.cs`, `MarketingAdsSeedDataTests.cs`

**Interfaces:**
- Consumes: C1 `AdAction`, `AdPlatform`, `AdActionType`; Domain `ApprovalChannel`; `MarketingAdsSeedData` (test only).
- Produces:
  - `AdLimitKeys.Enabled | MaxActionsPerProposal | McpMaxActionsPerProposal | MaxPausedAdsPer7Days | SecondApproverAbove`, `AdLimitKeys.All : IReadOnlySet<string>`
  - `enum AdLimitsDecision { Allowed = 1, NeedsSecondApprover = 2, NeedsWebApproval = 3, Denied = 4 }`
  - `sealed record AdLimitsEvaluation(AdLimitsDecision Decision, string? Reason)` with factories `Allow()`, `Deny(reason)`, `RequireWebApproval(reason)`, `RequireSecondApprover(reason)`
  - `sealed record AdGroupOccupancy(string AdGroupExternalId, int EnabledAdCount)` (namespace `...Proposals.ReadModel`)
  - `sealed record AdLimitsRequest(AdPlatform Platform, string AccountExternalId, AdActionType ActionType, IReadOnlyList<AdAction> Actions, ApprovalChannel Channel, string? ApproverPrincipal, IReadOnlyCollection<string> PriorApproverPrincipals, bool? IsAccountManaged, int SucceededPausesLast7Days, IReadOnlyDictionary<string, AdGroupOccupancy> AdGroupOccupancy, IReadOnlyDictionary<string, int> Limits)`
  - `interface IAdLimitsEvaluator { AdLimitsEvaluation Evaluate(AdLimitsRequest request); }`

Rule order (first match wins): **Denied** (type not enabled for platform · account not managed or unknown · no actions · mixed type/platform/account · over per-proposal max · PauseAd over 7-day account cap) → **NeedsWebApproval** (only for non-Web channels: MCP over MCP cap · PauseAd that leaves an ad group without an enabled ad, or targets an ad the read model does not know) → **NeedsSecondApprover** (over `SecondApproverAbove` unless a *different* principal already approved this version; System never counts as an approver) → **Allowed**. A missing limit row fails closed except `SecondApproverAbove` (optional rule).

- [ ] **Step 1: Write the failing tests**

`AdLimitsEvaluatorTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.ReadModel;
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Limits;

public class AdLimitsEvaluatorTests
{
    private const string Account = "acc-1";

    private static readonly Dictionary<string, int> PauseLimits = new()
    {
        [AdLimitKeys.Enabled] = 1, [AdLimitKeys.MaxActionsPerProposal] = 10, [AdLimitKeys.McpMaxActionsPerProposal] = 3,
        [AdLimitKeys.MaxPausedAdsPer7Days] = 20, [AdLimitKeys.SecondApproverAbove] = 10,
    };

    private static readonly Dictionary<string, int> NegativeLimits = new()
    {
        [AdLimitKeys.Enabled] = 1, [AdLimitKeys.MaxActionsPerProposal] = 50, [AdLimitKeys.McpMaxActionsPerProposal] = 20,
        [AdLimitKeys.SecondApproverAbove] = 50,
    };

    private static AdAction Pause(string adId, string account = Account) => new(AdActionType.PauseAd, AdPlatform.GoogleAds, account,
        AdEntityLevel.Ad, adId, AdActionValues.Enabled, AdActionValues.Paused, new Dictionary<string, string>());

    private static AdAction Negative(int i) => new(AdActionType.AddNegativeKeyword, AdPlatform.GoogleAds, Account,
        AdEntityLevel.Campaign, "cmp-1", AdActionValues.Absent, AdActionValues.Present,
        new Dictionary<string, string> { [AdActionPayloadKeys.Text] = $"kw{i}", [AdActionPayloadKeys.MatchType] = "Exact" });

    /// <summary>Each ad sits in its own group with 3 enabled ads unless the case overrides occupancy.</summary>
    private static AdLimitsRequest PauseRequest(int count = 1, ApprovalChannel channel = ApprovalChannel.Web,
        string? approver = "chief", string[]? prior = null, bool? managed = true, int pauses = 0,
        Dictionary<string, AdGroupOccupancy>? occupancy = null, Dictionary<string, int>? limits = null)
    {
        var actions = Enumerable.Range(1, count).Select(i => Pause($"ad-{i}")).ToList();
        occupancy ??= actions.ToDictionary(a => a.TargetExternalId, a => new AdGroupOccupancy($"grp-{a.TargetExternalId}", 3));
        return new AdLimitsRequest(AdPlatform.GoogleAds, Account, AdActionType.PauseAd, actions, channel, approver,
            prior ?? Array.Empty<string>(), managed, pauses, occupancy, limits ?? PauseLimits);
    }

    private static AdLimitsRequest NegativeRequest(int count, ApprovalChannel channel = ApprovalChannel.Web, int pauses = 0) =>
        new(AdPlatform.GoogleAds, Account, AdActionType.AddNegativeKeyword, Enumerable.Range(1, count).Select(Negative).ToList(),
            channel, "chief", Array.Empty<string>(), true, pauses, new Dictionary<string, AdGroupOccupancy>(), NegativeLimits);

    private static Dictionary<string, int> With(Dictionary<string, int> source, string key, int? value)
    {
        var copy = new Dictionary<string, int>(source);
        if (value is null) copy.Remove(key); else copy[key] = value.Value;
        return copy;
    }

    public static TheoryData<string, AdLimitsRequest, AdLimitsDecision, string?> Cases() => new()
    {
        { "baseline web pause", PauseRequest(), AdLimitsDecision.Allowed, null },
        { "type disabled for platform", PauseRequest(limits: With(PauseLimits, AdLimitKeys.Enabled, 0)), AdLimitsDecision.Denied, AdLimitReasons.ActionTypeNotEnabled },
        { "Enabled row missing fails closed", PauseRequest(limits: With(PauseLimits, AdLimitKeys.Enabled, null)), AdLimitsDecision.Denied, AdLimitReasons.ActionTypeNotEnabled },
        { "unmanaged account", PauseRequest(managed: false), AdLimitsDecision.Denied, AdLimitReasons.AccountNotManaged },
        { "unknown account (ads DB not configured)", PauseRequest(managed: null), AdLimitsDecision.Denied, AdLimitReasons.AccountNotManaged },
        { "mixed account", PauseRequest() with { Actions = new[] { Pause("ad-1"), Pause("ad-2", "other") } }, AdLimitsDecision.Denied, AdLimitReasons.MixedActions },
        { "no actions", PauseRequest() with { Actions = Array.Empty<AdAction>() }, AdLimitsDecision.Denied, AdLimitReasons.NoActions },
        { "10 pauses is the max", PauseRequest(10), AdLimitsDecision.Allowed, null },
        { "11 pauses over max", PauseRequest(11), AdLimitsDecision.Denied, AdLimitReasons.TooManyActions },
        { "50 negatives is the max", NegativeRequest(50), AdLimitsDecision.Allowed, null },
        { "51 negatives over max", NegativeRequest(51), AdLimitsDecision.Denied, AdLimitReasons.TooManyActions },
        { "weekly cap reached exactly", PauseRequest(1, pauses: 19), AdLimitsDecision.Allowed, null },
        { "weekly cap exceeded", PauseRequest(1, pauses: 20), AdLimitsDecision.Denied, AdLimitReasons.WeeklyPauseLimit },
        { "weekly cap ignores negatives", NegativeRequest(5, pauses: 500), AdLimitsDecision.Allowed, null },
        { "mcp 3 pauses allowed", PauseRequest(3, ApprovalChannel.Mcp), AdLimitsDecision.Allowed, null },
        { "mcp 4 pauses needs web", PauseRequest(4, ApprovalChannel.Mcp), AdLimitsDecision.NeedsWebApproval, AdLimitReasons.McpThreshold },
        { "web 4 pauses allowed", PauseRequest(4), AdLimitsDecision.Allowed, null },
        { "mcp 20 negatives allowed", NegativeRequest(20, ApprovalChannel.Mcp), AdLimitsDecision.Allowed, null },
        { "mcp 21 negatives needs web", NegativeRequest(21, ApprovalChannel.Mcp), AdLimitsDecision.NeedsWebApproval, AdLimitReasons.McpThreshold },
        { "mcp pause empties group", PauseRequest(1, ApprovalChannel.Mcp, occupancy: new() { ["ad-1"] = new("grp-1", 1) }), AdLimitsDecision.NeedsWebApproval, AdLimitReasons.AdGroupWouldBeEmpty },
        { "web pause may empty group", PauseRequest(1, occupancy: new() { ["ad-1"] = new("grp-1", 1) }), AdLimitsDecision.Allowed, null },
        { "mcp two pauses empty a 2-ad group", PauseRequest(2, ApprovalChannel.Mcp, occupancy: new() { ["ad-1"] = new("grp-1", 2), ["ad-2"] = new("grp-1", 2) }), AdLimitsDecision.NeedsWebApproval, AdLimitReasons.AdGroupWouldBeEmpty },
        { "mcp unknown ad", PauseRequest(1, ApprovalChannel.Mcp, occupancy: new()), AdLimitsDecision.NeedsWebApproval, AdLimitReasons.AdGroupWouldBeEmpty },
        { "system pause empties group", PauseRequest(1, ApprovalChannel.System, approver: "system:autonomy", occupancy: new() { ["ad-1"] = new("grp-1", 1) }), AdLimitsDecision.NeedsWebApproval, AdLimitReasons.AdGroupWouldBeEmpty },
        { "over second-approver threshold", PauseRequest(3, limits: With(PauseLimits, AdLimitKeys.SecondApproverAbove, 2)), AdLimitsDecision.NeedsSecondApprover, AdLimitReasons.SecondApproverRequired },
        { "same principal again is not a second approver", PauseRequest(3, prior: new[] { "chief" }, limits: With(PauseLimits, AdLimitKeys.SecondApproverAbove, 2)), AdLimitsDecision.NeedsSecondApprover, AdLimitReasons.SecondApproverRequired },
        { "different prior principal completes", PauseRequest(3, prior: new[] { "specialist" }, limits: With(PauseLimits, AdLimitKeys.SecondApproverAbove, 2)), AdLimitsDecision.Allowed, null },
        { "system never satisfies second approver", PauseRequest(3, ApprovalChannel.System, approver: "system:autonomy", limits: With(PauseLimits, AdLimitKeys.SecondApproverAbove, 2)), AdLimitsDecision.NeedsSecondApprover, AdLimitReasons.SecondApproverRequired },
        { "no approver yet (submit preview)", PauseRequest(3, approver: null, limits: With(PauseLimits, AdLimitKeys.SecondApproverAbove, 2)), AdLimitsDecision.NeedsSecondApprover, AdLimitReasons.SecondApproverRequired },
        { "missing second-approver row is optional", PauseRequest(3, limits: With(PauseLimits, AdLimitKeys.SecondApproverAbove, null)), AdLimitsDecision.Allowed, null },
        { "denied wins over needs-web", PauseRequest(4, ApprovalChannel.Mcp, managed: false), AdLimitsDecision.Denied, AdLimitReasons.AccountNotManaged },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Evaluate_AppliesSpecSection63(string because, AdLimitsRequest request, AdLimitsDecision expected, string? reason)
    {
        var result = new AdLimitsEvaluator().Evaluate(request);

        result.Decision.Should().Be(expected, because);
        result.Reason.Should().Be(reason, because);
    }
}
```

`MarketingAdsSeedDataTests.cs` (pins the Persistence seed strings to the Application constants/enums):

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Persistence.MarketingAds;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Limits;

public class MarketingAdsSeedDataTests
{
    [Fact]
    public void SeedStrings_MatchTheContractEnumsAndLimitKeys()
    {
        MarketingAdsSeedData.Platforms.Should().BeEquivalentTo(Enum.GetNames<AdPlatform>());
        MarketingAdsSeedData.ActionTypes.Should().BeEquivalentTo(Enum.GetNames<AdActionType>());
        MarketingAdsSeedData.Limits().Select(l => l.Key).Should().OnlyContain(k => AdLimitKeys.All.Contains(k));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `MSBUILDDISABLENODEREUSE=1 dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false`
Expected: FAIL — `The type or namespace name 'Limits' does not exist`.

- [ ] **Step 3: Implement** (namespace `Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits` unless noted)

```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;

/// <summary>Keys of AdLimitSettings rows. Must match MarketingAdsSeedData (pinned by a test).</summary>
public static class AdLimitKeys
{
    public const string Enabled = "Enabled";
    public const string MaxActionsPerProposal = "MaxActionsPerProposal";
    public const string McpMaxActionsPerProposal = "McpMaxActionsPerProposal";
    public const string MaxPausedAdsPer7Days = "MaxPausedAdsPer7Days";
    public const string SecondApproverAbove = "SecondApproverAbove";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Enabled, MaxActionsPerProposal, McpMaxActionsPerProposal, MaxPausedAdsPer7Days, SecondApproverAbove,
    };
}
```

```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;

/// <summary>Machine-readable reasons returned with an evaluation (shown to agents and in the UI).</summary>
public static class AdLimitReasons
{
    public const string ActionTypeNotEnabled = "ActionTypeNotEnabled";
    public const string AccountNotManaged = "AccountNotManaged";
    public const string NoActions = "NoActions";
    public const string MixedActions = "MixedActions";
    public const string TooManyActions = "TooManyActions";
    public const string WeeklyPauseLimit = "WeeklyPauseLimit";
    public const string McpThreshold = "McpThreshold";
    public const string AdGroupWouldBeEmpty = "AdGroupWouldBeEmpty";
    public const string SecondApproverRequired = "SecondApproverRequired";
}
```

```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;

public enum AdLimitsDecision
{
    Allowed = 1,
    NeedsSecondApprover = 2,
    NeedsWebApproval = 3,
    Denied = 4,
}
```

```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;

public sealed record AdLimitsEvaluation(AdLimitsDecision Decision, string? Reason)
{
    public static AdLimitsEvaluation Allow() => new(AdLimitsDecision.Allowed, null);
    public static AdLimitsEvaluation Deny(string reason) => new(AdLimitsDecision.Denied, reason);
    public static AdLimitsEvaluation RequireWebApproval(string reason) => new(AdLimitsDecision.NeedsWebApproval, reason);
    public static AdLimitsEvaluation RequireSecondApprover(string reason) => new(AdLimitsDecision.NeedsSecondApprover, reason);
}
```

`ReadModel/AdGroupOccupancy.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.ReadModel;

/// <summary>The ad group an ad belongs to and how many enabled ads that group has now (ads schema).</summary>
public sealed record AdGroupOccupancy(string AdGroupExternalId, int EnabledAdCount);
```

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.ReadModel;
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;

/// <summary>Everything the limits engine needs, gathered by IAdLimitsContextBuilder (Task 7).</summary>
public sealed record AdLimitsRequest(
    AdPlatform Platform,
    string AccountExternalId,
    AdActionType ActionType,
    IReadOnlyList<AdAction> Actions,
    ApprovalChannel Channel,
    string? ApproverPrincipal,
    IReadOnlyCollection<string> PriorApproverPrincipals,
    bool? IsAccountManaged,
    int SucceededPausesLast7Days,
    IReadOnlyDictionary<string, AdGroupOccupancy> AdGroupOccupancy,
    IReadOnlyDictionary<string, int> Limits);
```

```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;

/// <summary>Pure, server-side limits (spec 6.3). Independent of who approves.</summary>
public interface IAdLimitsEvaluator
{
    AdLimitsEvaluation Evaluate(AdLimitsRequest request);
}
```

`AdLimitsEvaluator.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;

public sealed class AdLimitsEvaluator : IAdLimitsEvaluator
{
    public AdLimitsEvaluation Evaluate(AdLimitsRequest request)
    {
        var denial = FindDenial(request);
        if (denial is not null)
        {
            return AdLimitsEvaluation.Deny(denial);
        }

        var webOnly = FindWebOnlyReason(request);
        if (webOnly is not null)
        {
            return AdLimitsEvaluation.RequireWebApproval(webOnly);
        }

        return NeedsSecondApprover(request)
            ? AdLimitsEvaluation.RequireSecondApprover(AdLimitReasons.SecondApproverRequired)
            : AdLimitsEvaluation.Allow();
    }

    private static string? FindDenial(AdLimitsRequest r)
    {
        if (Limit(r, AdLimitKeys.Enabled) != 1) return AdLimitReasons.ActionTypeNotEnabled;
        if (r.IsAccountManaged != true) return AdLimitReasons.AccountNotManaged;
        if (r.Actions.Count == 0) return AdLimitReasons.NoActions;
        if (r.Actions.Any(a => a.Type != r.ActionType || a.Platform != r.Platform
                               || !string.Equals(a.AccountExternalId, r.AccountExternalId, StringComparison.Ordinal)))
        {
            return AdLimitReasons.MixedActions;
        }
        if (r.Actions.Count > (Limit(r, AdLimitKeys.MaxActionsPerProposal) ?? 0)) return AdLimitReasons.TooManyActions;
        if (r.ActionType == AdActionType.PauseAd
            && r.SucceededPausesLast7Days + r.Actions.Count > (Limit(r, AdLimitKeys.MaxPausedAdsPer7Days) ?? 0))
        {
            return AdLimitReasons.WeeklyPauseLimit;
        }
        return null;
    }

    private static string? FindWebOnlyReason(AdLimitsRequest r)
    {
        if (r.Channel == ApprovalChannel.Web) return null;
        if (r.Channel == ApprovalChannel.Mcp && r.Actions.Count > (Limit(r, AdLimitKeys.McpMaxActionsPerProposal) ?? 0))
        {
            return AdLimitReasons.McpThreshold;
        }
        if (r.ActionType == AdActionType.PauseAd && WouldEmptyAnAdGroup(r)) return AdLimitReasons.AdGroupWouldBeEmpty;
        return null;
    }

    private static bool WouldEmptyAnAdGroup(AdLimitsRequest r)
    {
        var pausedPerGroup = new Dictionary<string, (int Paused, int Enabled)>(StringComparer.Ordinal);
        foreach (var action in r.Actions)
        {
            // An ad the read model does not know cannot be proven safe.
            if (!r.AdGroupOccupancy.TryGetValue(action.TargetExternalId, out var occupancy)) return true;
            var current = pausedPerGroup.GetValueOrDefault(occupancy.AdGroupExternalId, (0, occupancy.EnabledAdCount));
            pausedPerGroup[occupancy.AdGroupExternalId] = (current.Paused + 1, occupancy.EnabledAdCount);
        }
        return pausedPerGroup.Values.Any(g => g.Enabled - g.Paused <= 0);
    }

    private static bool NeedsSecondApprover(AdLimitsRequest r)
    {
        var threshold = Limit(r, AdLimitKeys.SecondApproverAbove);
        if (threshold is null || r.Actions.Count <= threshold) return false;
        if (r.Channel == ApprovalChannel.System || r.ApproverPrincipal is null) return true;
        return !r.PriorApproverPrincipals.Any(p => !string.Equals(p, r.ApproverPrincipal, StringComparison.Ordinal));
    }

    private static int? Limit(AdLimitsRequest r, string key) => r.Limits.TryGetValue(key, out var value) ? value : null;
}
```

- [ ] **Step 4: Run the tests**

```bash
MSBUILDDISABLENODEREUSE=1 dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~MarketingAds.Proposals.Limits"
```

Expected: PASS — 31 evaluator cases + seed test.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingAds/Proposals backend/test/Anela.Heblo.Tests/Features/MarketingAds/Proposals/Limits
git commit -m "feat: pure ad limits evaluator with table-driven tests for every spec 6.3 rule

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 7: `ads` read model and the limits context builder

The limits engine needs `is_managed`, the ad → ad group mapping with enabled-ad counts, and entity names for the diff. They come from C1's `ads` schema. To stay independent of C1's C# entity names, the read model queries the **spec-fixed table/column names** (`ads.ad_accounts`, `ads.ad_entities`, snake_case per spec 4.1, the `Ga4DbContext` convention) with raw SQL, and compares enum columns as text against both the enum name and its numeric value, so it works whether C1 stored them as `text` or `integer`. When `AdsDbContext` is not registered (connection string not configured) the `UnavailableAdsReadModel` answers "unknown", which the limits engine denies (fail closed).

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — add `<ProjectReference Include="../Anela.Heblo.Persistence.Ads/Anela.Heblo.Persistence.Ads.csproj" />` **only if** it is not already there (C2 may have added it; check with `grep -n Persistence.Ads backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`)
- Modify: `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — add `<ProjectReference Include="..\..\src\Anela.Heblo.Persistence.Ads\Anela.Heblo.Persistence.Ads.csproj" />` if missing
- Create (`.../Proposals/ReadModel/`): `IAdsReadModel.cs`, `AdsDbReadModel.cs`, `UnavailableAdsReadModel.cs`
- Create (`.../Proposals/Limits/`): `IAdLimitsContextBuilder.cs`, `AdLimitsContextBuilder.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/Proposals/ReadModel/AdsDbReadModelIntegrationTests.cs`, `.../Limits/AdLimitsContextBuilderTests.cs`

**Interfaces:**
- Consumes: C1 `AdsDbContext` (namespace recorded in *Before you start*; assumed `Anela.Heblo.Persistence.Ads`, constant `AdsDbContext.SchemaName = "ads"`), `IAdGovernanceRepository`, `IAdProposalRepository`, Task 6 types.
- Produces:

```csharp
public interface IAdsReadModel
{
    /// <summary>null = account unknown to Heblo (or the ads DB is not configured).</summary>
    Task<bool?> IsAccountManagedAsync(AdPlatform platform, string accountExternalId, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, AdGroupOccupancy>> GetAdGroupOccupancyAsync(AdPlatform platform, string accountExternalId, IReadOnlyCollection<string> adExternalIds, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, string>> GetEntityNamesAsync(AdPlatform platform, string accountExternalId, IReadOnlyCollection<string> externalIds, CancellationToken cancellationToken);
}

public interface IAdLimitsContextBuilder
{
    Task<AdLimitsRequest> BuildAsync(AdProposal proposal, IReadOnlyList<AdAction> actions, ApprovalChannel channel, string? approverPrincipal, CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Write the failing tests**

`AdsDbReadModelIntegrationTests.cs` — runs C1's real `ads` migrations, then inserts rows with the spec 4.1 columns. If C1 added further NOT NULL columns without defaults, the insert fails with a clear Postgres message: add those columns to the two INSERT statements (values are irrelevant to these tests).

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.ReadModel;
using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.ReadModel;

[Collection("PostgresIntegration")]
[Trait("Category", "Integration")]
public class AdsDbReadModelIntegrationTests : IAsyncLifetime
{
    private readonly PostgresSharedContainerFixture _fixture;
    private string _connectionString = null!;
    private AdsDbContext _db = null!;

    public AdsDbReadModelIntegrationTests(PostgresSharedContainerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _connectionString = await _fixture.CreateDatabaseAsync("ads_read_model");
        _db = new AdsDbContext(new DbContextOptionsBuilder<AdsDbContext>()
            .UseNpgsql(_connectionString, o => o.MigrationsHistoryTable("__EFMigrationsHistory", AdsDbContext.SchemaName))
            .Options);
        await _db.Database.MigrateAsync();

        var account = await InsertAccountAsync(AdPlatform.GoogleAds, "acc-1", isManaged: true);
        await InsertAccountAsync(AdPlatform.GoogleAds, "acc-unmanaged", isManaged: false);
        var group = await InsertEntityAsync(account, AdEntityLevel.AdGroup, "grp-1", null, "Sestava 1", AdEntityStatus.Enabled);
        await InsertEntityAsync(account, AdEntityLevel.Ad, "ad-1", group, "Reklama 1", AdEntityStatus.Enabled);
        await InsertEntityAsync(account, AdEntityLevel.Ad, "ad-2", group, "Reklama 2", AdEntityStatus.Enabled);
        await InsertEntityAsync(account, AdEntityLevel.Ad, "ad-3", group, "Reklama 3", AdEntityStatus.Paused);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private async Task<object> EnumValueAsync(string table, string column, Enum value)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT data_type FROM information_schema.columns WHERE table_schema = 'ads' AND table_name = @t AND column_name = @c", conn);
        cmd.Parameters.AddWithValue("t", table);
        cmd.Parameters.AddWithValue("c", column);
        var type = (string?)await cmd.ExecuteScalarAsync()
            ?? throw new InvalidOperationException($"ads.{table}.{column} not found — C1 schema differs from spec 4.1");
        return type is "integer" or "smallint" or "bigint" ? Convert.ToInt32(value) : value.ToString();
    }

    private async Task<object> InsertAccountAsync(AdPlatform platform, string externalId, bool isManaged)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO ads.ad_accounts (platform, external_id, name, currency, time_zone, is_managed)
            VALUES (@p, @e, @e, 'CZK', 'Europe/Prague', @m) RETURNING id
            """, conn);
        cmd.Parameters.AddWithValue("p", await EnumValueAsync("ad_accounts", "platform", platform));
        cmd.Parameters.AddWithValue("e", externalId);
        cmd.Parameters.AddWithValue("m", isManaged);
        return (await cmd.ExecuteScalarAsync())!;
    }

    private async Task<object> InsertEntityAsync(object accountId, AdEntityLevel level, string externalId, object? parentId, string name, AdEntityStatus status)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO ads.ad_entities (account_id, level, external_id, parent_id, name, status, attributes, first_seen_at, last_seen_at, updated_at)
            VALUES (@a, @l, @e, @p, @n, @s, '{}'::jsonb, now(), now(), now()) RETURNING id
            """, conn);
        cmd.Parameters.AddWithValue("a", accountId);
        cmd.Parameters.AddWithValue("l", await EnumValueAsync("ad_entities", "level", level));
        cmd.Parameters.AddWithValue("e", externalId);
        cmd.Parameters.AddWithValue("p", parentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("n", name);
        cmd.Parameters.AddWithValue("s", await EnumValueAsync("ad_entities", "status", status));
        return (await cmd.ExecuteScalarAsync())!;
    }

    [Theory]
    [InlineData(AdPlatform.GoogleAds, "acc-1", true)]
    [InlineData(AdPlatform.GoogleAds, "acc-unmanaged", false)]
    [InlineData(AdPlatform.GoogleAds, "acc-missing", null)]
    [InlineData(AdPlatform.MetaAds, "acc-1", null)]
    public async Task IsAccountManaged_ReadsIsManagedPerPlatform(AdPlatform platform, string account, bool? expected)
    {
        (await new AdsDbReadModel(_db).IsAccountManagedAsync(platform, account, CancellationToken.None)).Should().Be(expected);
    }

    [Fact]
    public async Task GetAdGroupOccupancy_CountsOnlyEnabledSiblings()
    {
        var result = await new AdsDbReadModel(_db).GetAdGroupOccupancyAsync(AdPlatform.GoogleAds, "acc-1", new[] { "ad-1", "ad-unknown" }, CancellationToken.None);

        result.Should().ContainKey("ad-1").WhoseValue.Should().Be(new AdGroupOccupancy("grp-1", 2));
        result.Should().NotContainKey("ad-unknown");
    }

    [Fact]
    public async Task GetEntityNames_ReturnsNamesOfKnownIds()
    {
        var names = await new AdsDbReadModel(_db).GetEntityNamesAsync(AdPlatform.GoogleAds, "acc-1", new[] { "grp-1", "ad-2", "nope" }, CancellationToken.None);

        names.Should().BeEquivalentTo(new Dictionary<string, string> { ["grp-1"] = "Sestava 1", ["ad-2"] = "Reklama 2" });
    }
}
```

`AdLimitsContextBuilderTests.cs` (InMemory `ApplicationDbContext` with seeds; read model mocked):

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.ReadModel;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.MarketingAds;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Limits;

public class AdLimitsContextBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task BuildAsync_GathersSeededLimitsReadModelAndWeeklyPauses()
    {
        var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        context.Database.EnsureCreated();
        var readModel = new Mock<IAdsReadModel>();
        readModel.Setup(r => r.IsAccountManagedAsync(AdPlatform.GoogleAds, "acc-1", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        readModel.Setup(r => r.GetAdGroupOccupancyAsync(AdPlatform.GoogleAds, "acc-1", It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, AdGroupOccupancy> { ["ad-1"] = new("grp-1", 2) });
        var builder = new AdLimitsContextBuilder(new AdGovernanceRepository(context), new AdProposalRepository(context), readModel.Object, new FakeTimeProvider(Now));
        var proposal = AdProposal.Create(new AdProposalDraft("GoogleAds", "acc-1", "PauseAd", "agent-1", AdActorKind.Agent, null, "[]", 1, "r"), Now.UtcDateTime, TimeSpan.FromHours(72));
        var action = new AdAction(AdActionType.PauseAd, AdPlatform.GoogleAds, "acc-1", AdEntityLevel.Ad, "ad-1", AdActionValues.Enabled, AdActionValues.Paused, new Dictionary<string, string>());

        var request = await builder.BuildAsync(proposal, new[] { action }, ApprovalChannel.Mcp, "chief", CancellationToken.None);

        request.Limits[AdLimitKeys.MaxActionsPerProposal].Should().Be(10);
        request.IsAccountManaged.Should().BeTrue();
        request.AdGroupOccupancy["ad-1"].EnabledAdCount.Should().Be(2);
        request.SucceededPausesLast7Days.Should().Be(0);
        request.Channel.Should().Be(ApprovalChannel.Mcp);
        request.ApproverPrincipal.Should().Be("chief");
    }

    [Fact]
    public async Task UnavailableReadModel_MakesTheEngineDeny()
    {
        var unavailable = new UnavailableAdsReadModel();

        (await unavailable.IsAccountManagedAsync(AdPlatform.Sklik, "acc", CancellationToken.None)).Should().BeNull();
        (await unavailable.GetAdGroupOccupancyAsync(AdPlatform.Sklik, "acc", new[] { "ad" }, CancellationToken.None)).Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `MSBUILDDISABLENODEREUSE=1 dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false`
Expected: FAIL — `The type or namespace name 'AdsDbReadModel' could not be found`.

- [ ] **Step 3: Implement the read model** (namespace `...Proposals.ReadModel`)

`IAdsReadModel.cs` — exactly as under **Interfaces**, with `using Anela.Heblo.Application.Features.MarketingAds.Contracts;`.

`UnavailableAdsReadModel.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.ReadModel;

/// <summary>Used when AdsDbContext is not registered. "Unknown" everywhere, so the limits engine denies.</summary>
public sealed class UnavailableAdsReadModel : IAdsReadModel
{
    private static readonly IReadOnlyDictionary<string, AdGroupOccupancy> NoOccupancy = new Dictionary<string, AdGroupOccupancy>();
    private static readonly IReadOnlyDictionary<string, string> NoNames = new Dictionary<string, string>();

    public Task<bool?> IsAccountManagedAsync(AdPlatform platform, string accountExternalId, CancellationToken cancellationToken) =>
        Task.FromResult<bool?>(null);

    public Task<IReadOnlyDictionary<string, AdGroupOccupancy>> GetAdGroupOccupancyAsync(AdPlatform platform, string accountExternalId,
        IReadOnlyCollection<string> adExternalIds, CancellationToken cancellationToken) => Task.FromResult(NoOccupancy);

    public Task<IReadOnlyDictionary<string, string>> GetEntityNamesAsync(AdPlatform platform, string accountExternalId,
        IReadOnlyCollection<string> externalIds, CancellationToken cancellationToken) => Task.FromResult(NoNames);
}
```

`AdsDbReadModel.cs`:

```csharp
using System.Globalization;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Persistence.Ads;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.ReadModel;

/// <summary>
/// Reads the C1 `ads` schema by its spec-fixed table/column names. Enum columns are compared as text
/// against both the enum name and its number, so the query is valid whichever storage C1 chose.
/// </summary>
public sealed class AdsDbReadModel : IAdsReadModel
{
    private readonly AdsDbContext _db;

    public AdsDbReadModel(AdsDbContext db) => _db = db;

    public async Task<bool?> IsAccountManagedAsync(AdPlatform platform, string accountExternalId, CancellationToken cancellationToken)
    {
        var platformKeys = EnumKeys(platform);
        var rows = await _db.Database.SqlQuery<ManagedRow>($"""
            SELECT a.is_managed AS "IsManaged"
            FROM ads.ad_accounts a
            WHERE a.platform::text = ANY({platformKeys}) AND a.external_id = {accountExternalId}
            """).ToListAsync(cancellationToken);
        return rows.Count == 0 ? null : rows[0].IsManaged;
    }

    public async Task<IReadOnlyDictionary<string, AdGroupOccupancy>> GetAdGroupOccupancyAsync(AdPlatform platform,
        string accountExternalId, IReadOnlyCollection<string> adExternalIds, CancellationToken cancellationToken)
    {
        if (adExternalIds.Count == 0)
        {
            return new Dictionary<string, AdGroupOccupancy>();
        }
        var platformKeys = EnumKeys(platform);
        var adLevel = EnumKeys(AdEntityLevel.Ad);
        var enabled = EnumKeys(AdEntityStatus.Enabled);
        var ids = adExternalIds.ToArray();
        var rows = await _db.Database.SqlQuery<OccupancyRow>($"""
            SELECT ad.external_id AS "AdExternalId",
                   grp.external_id AS "AdGroupExternalId",
                   (SELECT count(*)::int FROM ads.ad_entities sib
                     WHERE sib.parent_id = grp.id
                       AND sib.level::text = ANY({adLevel})
                       AND sib.status::text = ANY({enabled})) AS "EnabledAdCount"
            FROM ads.ad_entities ad
            JOIN ads.ad_accounts acc ON acc.id = ad.account_id
            JOIN ads.ad_entities grp ON grp.id = ad.parent_id
            WHERE acc.platform::text = ANY({platformKeys})
              AND acc.external_id = {accountExternalId}
              AND ad.level::text = ANY({adLevel})
              AND ad.external_id = ANY({ids})
            """).ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.AdExternalId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => new AdGroupOccupancy(g.First().AdGroupExternalId, g.First().EnabledAdCount), StringComparer.Ordinal);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetEntityNamesAsync(AdPlatform platform, string accountExternalId,
        IReadOnlyCollection<string> externalIds, CancellationToken cancellationToken)
    {
        if (externalIds.Count == 0)
        {
            return new Dictionary<string, string>();
        }
        var platformKeys = EnumKeys(platform);
        var ids = externalIds.ToArray();
        var rows = await _db.Database.SqlQuery<NameRow>($"""
            SELECT e.external_id AS "ExternalId", e.name AS "Name"
            FROM ads.ad_entities e
            JOIN ads.ad_accounts acc ON acc.id = e.account_id
            WHERE acc.platform::text = ANY({platformKeys})
              AND acc.external_id = {accountExternalId}
              AND e.external_id = ANY({ids})
            """).ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.ExternalId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);
    }

    private static string[] EnumKeys<TEnum>(TEnum value) where TEnum : struct, Enum =>
        new[] { value.ToString(), Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) };

    public sealed class ManagedRow
    {
        public bool IsManaged { get; set; }
    }

    public sealed class OccupancyRow
    {
        public string AdExternalId { get; set; } = null!;
        public string AdGroupExternalId { get; set; } = null!;
        public int EnabledAdCount { get; set; }
    }

    public sealed class NameRow
    {
        public string ExternalId { get; set; } = null!;
        public string Name { get; set; } = null!;
    }
}
```

- [ ] **Step 4: Implement the context builder** (namespace `...Proposals.Limits`)

`IAdLimitsContextBuilder.cs` — as under **Interfaces** (usings: C1 Contracts, `Anela.Heblo.Domain.Features.MarketingAds`).

`AdLimitsContextBuilder.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.ReadModel;
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;

public sealed class AdLimitsContextBuilder : IAdLimitsContextBuilder
{
    private static readonly TimeSpan PauseWindow = TimeSpan.FromDays(7);
    private static readonly IReadOnlyDictionary<string, AdGroupOccupancy> NoOccupancy = new Dictionary<string, AdGroupOccupancy>();

    private readonly IAdGovernanceRepository _governance;
    private readonly IAdProposalRepository _proposals;
    private readonly IAdsReadModel _readModel;
    private readonly TimeProvider _time;

    public AdLimitsContextBuilder(IAdGovernanceRepository governance, IAdProposalRepository proposals, IAdsReadModel readModel, TimeProvider time)
    {
        _governance = governance;
        _proposals = proposals;
        _readModel = readModel;
        _time = time;
    }

    public async Task<AdLimitsRequest> BuildAsync(AdProposal proposal, IReadOnlyList<AdAction> actions, ApprovalChannel channel,
        string? approverPrincipal, CancellationToken cancellationToken)
    {
        var platform = Enum.Parse<AdPlatform>(proposal.Platform);
        var actionType = Enum.Parse<AdActionType>(proposal.ActionType);
        var limits = (await _governance.GetLimitSettingsAsync(proposal.Platform, proposal.ActionType, cancellationToken))
            .ToDictionary(l => l.Key, l => l.Value, StringComparer.Ordinal);
        var isManaged = await _readModel.IsAccountManagedAsync(platform, proposal.AccountExternalId, cancellationToken);

        var pauses = 0;
        var occupancy = NoOccupancy;
        if (actionType == AdActionType.PauseAd)
        {
            var since = _time.GetUtcNow().UtcDateTime - PauseWindow;
            pauses = await _proposals.CountSucceededActionsSinceAsync(proposal.Platform, proposal.AccountExternalId, proposal.ActionType, since, cancellationToken);
            var adIds = actions.Select(a => a.TargetExternalId).Distinct(StringComparer.Ordinal).ToList();
            occupancy = await _readModel.GetAdGroupOccupancyAsync(platform, proposal.AccountExternalId, adIds, cancellationToken);
        }

        return new AdLimitsRequest(platform, proposal.AccountExternalId, actionType, actions, channel, approverPrincipal,
            proposal.GetCurrentApprovers().ToList(), isManaged, pauses, occupancy, limits);
    }
}
```

- [ ] **Step 5: Run the tests**

```bash
MSBUILDDISABLENODEREUSE=1 dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~AdLimitsContextBuilderTests"
podman machine start
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~AdsDbReadModelIntegrationTests"
```

Expected: PASS (2 unit, 6 integration).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application backend/test/Anela.Heblo.Tests
git commit -m "feat: ads read model and limits context builder for the proposal layer

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 8: Module wiring — options, error codes, channel resolver, audit factory, autonomy, enqueuer, test harness

**Files:**
- Create (`backend/src/Anela.Heblo.Application/Features/MarketingAds/Proposals/`): `AdProposalOptions.cs`, `AdSystemPrincipals.cs`, `AdProposalGuards.cs`, `AdProposalErrors.cs`, `MarketingAdsProposalsModule.cs`, `Approval/ApprovalChannelResolution.cs`, `Approval/IApprovalChannelResolver.cs`, `Approval/DenyAllApprovalChannelResolver.cs`, `Audit/AdAuditEvents.cs`, `Autonomy/IAdAutonomyService.cs`, `Autonomy/AdAutonomyService.cs`, `Execution/IAdProposalExecutionEnqueuer.cs`, `Execution/HangfireAdProposalExecutionEnqueuer.cs`
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` (new 39XX bucket), `backend/test/Anela.Heblo.Tests/ErrorHandlingTests.cs` (range bucket), `frontend/src/i18n.ts` (Czech texts), `backend/src/Anela.Heblo.Application/ApplicationModule.cs`
- Create (tests): `backend/test/Anela.Heblo.Tests/Features/MarketingAds/Proposals/ProposalTestHarness.cs`, `Autonomy/AdAutonomyServiceTests.cs`, `MarketingAdsProposalsModuleTests.cs`

**Interfaces:**
- Produces:
  - `AdProposalOptions { const SectionName = "Ads"; int ProposalTtlHours = 72; string ExpiryCronExpression = "*/15 * * * *"; int StuckApprovedRequeueMinutes = 15; TimeSpan ProposalTtl }`
  - `AdSystemPrincipals.Autonomy = "system:autonomy"`, `.Executor = "system:executor"`, `.Expiry = "system:expiry"`
  - `AdProposalGuards.CheckPendingVersion(AdProposal? proposal, int expectedVersion, DateTime now) : ErrorCodes?`
  - `AdProposalErrors.Params(IReadOnlyList<string> errors) : Dictionary<string,string>`, `AdProposalErrors.Reason(string? reason) : Dictionary<string,string>`
  - **For C4:** `interface IApprovalChannelResolver { ApprovalChannelResolution Resolve(); }` (synchronous — it only reads the current token's claims), `sealed class ApprovalChannelResolution { bool Allowed; ApprovalChannel Channel; string? DenyReason; static Allow(ApprovalChannel); static Deny(string) }` in namespace `Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval`; `DenyAllApprovalChannelResolver`. C4 replaces the single registration line in `MarketingAdsProposalsModule`.
  - `AdAuditEvents.ForProposal(AdProposal, AdAuditEventType, string actor, AdActorKind, ApprovalChannel, object payload, DateTime at) : AdAuditEvent`, `AdAuditEvents.ForSetting(AdAuditEventType, string actor, ApprovalChannel, object payload, DateTime at)`, `AdAuditEvents.SubmissionChannel(AdActorKind) : ApprovalChannel`
  - `interface IAdAutonomyService { Task<bool> TryAutoApproveAsync(AdProposal proposal, IReadOnlyList<AdAction> actions, CancellationToken cancellationToken); }`; `AdAutonomyService.IsAutoApprovable(AdAutonomyMode, AdLimitsDecision) : bool`
  - `interface IAdProposalExecutionEnqueuer { string? EnqueueExecution(int proposalId); }`
  - `MarketingAdsProposalsModule.AddMarketingAdsProposalsModule(this IServiceCollection, IConfiguration)`
  - ErrorCodes 3901–3917 (names below)
  - Test harness `ProposalTestHarness` (used by Tasks 9–15)

- [ ] **Step 1: Add the error codes** — in `ErrorCodes.cs`, after the `// Pricing simulator module errors (38XX)` block and before `// External Service errors (90XX)`:

```csharp
    // Marketing Ads — proposal & approval layer (39XX)
    [HttpStatusCode(HttpStatusCode.NotFound)]
    MarketingAdsProposalNotFound = 3901,
    [HttpStatusCode(HttpStatusCode.Conflict)]
    MarketingAdsProposalVersionMismatch = 3902,
    [HttpStatusCode(HttpStatusCode.Conflict)]
    MarketingAdsProposalNotPending = 3903,
    [HttpStatusCode(HttpStatusCode.Conflict)]
    MarketingAdsProposalExpired = 3904,
    [HttpStatusCode(HttpStatusCode.Forbidden)]
    MarketingAdsApprovalChannelDenied = 3905,
    [HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
    MarketingAdsLimitsDenied = 3906,
    [HttpStatusCode(HttpStatusCode.Forbidden)]
    MarketingAdsNeedsWebApproval = 3907,
    [HttpStatusCode(HttpStatusCode.Conflict)]
    MarketingAdsAlreadyApprovedByPrincipal = 3908,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    MarketingAdsInvalidProposal = 3909,
    [HttpStatusCode(HttpStatusCode.Conflict)]
    MarketingAdsNotRevertible = 3910,
    [HttpStatusCode(HttpStatusCode.BadGateway)]
    MarketingAdsRevertFailed = 3911,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    MarketingAdsAgentRunNotFound = 3912,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    MarketingAdsInvalidSetting = 3913,
    [HttpStatusCode(HttpStatusCode.Conflict)]
    MarketingAdsConcurrentChange = 3914,
    [HttpStatusCode(HttpStatusCode.Conflict)]
    MarketingAdsNoExecutor = 3915,
    [HttpStatusCode(HttpStatusCode.Forbidden)]
    MarketingAdsNotProposalOwner = 3916,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    MarketingAdsInvalidAgentRun = 3917,
```

- [ ] **Step 2: Add the range bucket** — in `ErrorHandlingTests.cs` (the test that categorises every code), add after the `pricingErrors` line:

```csharp
        var marketingAdsErrors = errorCodes.Where(code => code >= 3900 && code < 4000).ToList(); // 39XX range (Marketing Ads proposals)
```

after the `Assert.True(pricingErrors.Count > 0, …)` line:

```csharp
        Assert.True(marketingAdsErrors.Count > 0, "Should have Marketing Ads errors in 39XX range");
```

and in the `categorizedCount` sum replace `+ pricingErrors.Count + externalServiceErrors.Count;` with `+ pricingErrors.Count + marketingAdsErrors.Count + externalServiceErrors.Count;`.

- [ ] **Step 3: Add the Czech translations** — in `frontend/src/i18n.ts`, directly after the line `MarketingPerformanceEnqueueFailed: "Přepočet se nepodařilo zařadit do fronty. Zkuste to prosím znovu.",` insert (one line each, double quotes — `LocalizationCoverageTests` matches `Name:\s*"…"`):

```ts

        // Marketing Ads — proposal & approval layer (39XX)
        MarketingAdsProposalNotFound: "Návrh změny reklamy nebyl nalezen.",
        MarketingAdsProposalVersionMismatch: "Návrh byl mezitím upraven. Načtěte aktuální verzi a rozhodněte znovu.",
        MarketingAdsProposalNotPending: "Návrh už nečeká na schválení.",
        MarketingAdsProposalExpired: "Platnost návrhu vypršela. Požádejte o nový návrh.",
        MarketingAdsApprovalChannelDenied: "Z tohoto přístupu nelze návrhy schvalovat, zamítat ani měnit nastavení.",
        MarketingAdsLimitsDenied: "Návrh překračuje pevné limity a nelze ho schválit.",
        MarketingAdsNeedsWebApproval: "Tento návrh lze schválit jen ve webové aplikaci Heblo.",
        MarketingAdsAlreadyApprovedByPrincipal: "Tuto verzi návrhu už jste schválili. Druhé schválení musí udělat jiná osoba.",
        MarketingAdsInvalidProposal: "Návrh je neplatný.",
        MarketingAdsNotRevertible: "Návrh nelze vrátit, protože nebyl proveden.",
        MarketingAdsRevertFailed: "Vrácení změn se nepodařilo u všech akcí. Zkontrolujte historii návrhu.",
        MarketingAdsAgentRunNotFound: "Běh agenta nebyl nalezen.",
        MarketingAdsInvalidSetting: "Neplatné nastavení.",
        MarketingAdsConcurrentChange: "Návrh mezitím změnil někdo jiný. Načtěte ho znovu.",
        MarketingAdsNoExecutor: "Pro tuto platformu není k dispozici vykonavatel akcí.",
        MarketingAdsNotProposalOwner: "Návrh může upravit jen jeho autor.",
        MarketingAdsInvalidAgentRun: "Běh agenta nelze takto ukončit.",
```

- [ ] **Step 4: Create the small shared types**

`AdProposalOptions.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Proposals;

/// <summary>Bound to the "Ads" section (shared with C2's sync keys; each class reads only its own properties).</summary>
public class AdProposalOptions
{
    public const string SectionName = "Ads";

    public int ProposalTtlHours { get; set; } = 72;
    public string ExpiryCronExpression { get; set; } = "*/15 * * * *";
    /// <summary>An Approved proposal not picked up by the execution job for this long is re-enqueued.</summary>
    public int StuckApprovedRequeueMinutes { get; set; } = 15;

    public TimeSpan ProposalTtl => TimeSpan.FromHours(ProposalTtlHours);
}
```

`AdSystemPrincipals.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Proposals;

/// <summary>Principals recorded for actions Heblo takes on its own.</summary>
public static class AdSystemPrincipals
{
    public const string Autonomy = "system:autonomy";
    public const string Executor = "system:executor";
    public const string Expiry = "system:expiry";
}
```

`AdProposalGuards.cs`:

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals;

public static class AdProposalGuards
{
    /// <summary>Approve, reject and revise all require a Pending, unexpired proposal at the named version.</summary>
    public static ErrorCodes? CheckPendingVersion(AdProposal? proposal, int expectedVersion, DateTime now)
    {
        if (proposal is null) return ErrorCodes.MarketingAdsProposalNotFound;
        if (proposal.IsExpiredAt(now)) return ErrorCodes.MarketingAdsProposalExpired;
        if (proposal.Status != AdProposalStatus.Pending) return ErrorCodes.MarketingAdsProposalNotPending;
        if (proposal.CurrentVersion != expectedVersion) return ErrorCodes.MarketingAdsProposalVersionMismatch;
        return null;
    }
}
```

`AdProposalErrors.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Proposals;

public static class AdProposalErrors
{
    public static Dictionary<string, string> Params(IReadOnlyList<string> errors) =>
        new() { ["Errors"] = string.Join(" ", errors) };

    public static Dictionary<string, string> Reason(string? reason) =>
        new() { ["Reason"] = reason ?? string.Empty };
}
```

- [ ] **Step 5: Create the channel resolver contract and the deny-all default** (namespace `...Proposals.Approval`)

```csharp
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;

/// <summary>Result of resolving who may approve from where. Class, not record: it may cross the API boundary in C4.</summary>
public sealed class ApprovalChannelResolution
{
    public bool Allowed { get; init; }
    public ApprovalChannel Channel { get; init; }
    public string? DenyReason { get; init; }

    public static ApprovalChannelResolution Allow(ApprovalChannel channel) => new() { Allowed = true, Channel = channel };
    public static ApprovalChannelResolution Deny(string reason) => new() { Allowed = false, DenyReason = reason };
}
```

```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;

/// <summary>
/// Spec 7.4: decides from the caller's token whether it may approve/reject and through which channel.
/// Used by every approve/reject/revert/settings handler. The real implementation ships in PR C4.
/// </summary>
public interface IApprovalChannelResolver
{
    ApprovalChannelResolution Resolve();
}
```

```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;

/// <summary>Registered by C3 so nothing can be approved before C4's claims-based resolver is deployed.</summary>
public sealed class DenyAllApprovalChannelResolver : IApprovalChannelResolver
{
    public const string Reason = "Approval channels are not configured yet (PR C4).";

    public ApprovalChannelResolution Resolve() => ApprovalChannelResolution.Deny(Reason);
}
```

- [ ] **Step 6: Create the audit factory** — `Audit/AdAuditEvents.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Audit;

public static class AdAuditEvents
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Pass a *tracked* proposal: EF fills ProposalId from the navigation, also for a proposal inserted in the same SaveChanges.</summary>
    public static AdAuditEvent ForProposal(AdProposal proposal, AdAuditEventType type, string actor, AdActorKind actorKind,
        ApprovalChannel channel, object payload, DateTime at) => new()
    {
        Proposal = proposal,
        Version = proposal.CurrentVersion,
        EventType = type,
        ActorPrincipal = actor,
        ActorKind = actorKind,
        Channel = channel,
        PayloadJson = JsonSerializer.Serialize(payload, Options),
        OccurredAt = at,
    };

    public static AdAuditEvent ForSetting(AdAuditEventType type, string actor, ApprovalChannel channel, object payload, DateTime at) => new()
    {
        EventType = type,
        ActorPrincipal = actor,
        ActorKind = AdActorKind.Human,
        Channel = channel,
        PayloadJson = JsonSerializer.Serialize(payload, Options),
        OccurredAt = at,
    };

    /// <summary>
    /// Channel recorded for Submitted/Revised until C4 resolves it from the token: agents talk to Heblo
    /// only over MCP; a person is recorded as Web.
    /// </summary>
    public static ApprovalChannel SubmissionChannel(AdActorKind kind) =>
        kind == AdActorKind.Agent ? ApprovalChannel.Mcp : ApprovalChannel.Web;
}
```

- [ ] **Step 7: Create the enqueuer**

```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Execution;

public interface IAdProposalExecutionEnqueuer
{
    /// <summary>Returns the Hangfire job id, or null when enqueueing failed (the expiry job re-enqueues stuck Approved proposals).</summary>
    string? EnqueueExecution(int proposalId);
}
```

```csharp
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Execution;

public class HangfireAdProposalExecutionEnqueuer : IAdProposalExecutionEnqueuer
{
    private readonly IBackgroundJobClient _client;
    private readonly ILogger<HangfireAdProposalExecutionEnqueuer> _logger;

    public HangfireAdProposalExecutionEnqueuer(IBackgroundJobClient client, ILogger<HangfireAdProposalExecutionEnqueuer> logger)
    {
        _client = client;
        _logger = logger;
    }

    public string? EnqueueExecution(int proposalId)
    {
        try
        {
            return _client.Enqueue<AdProposalExecutionJob>(job => job.RunAsync(proposalId, CancellationToken.None));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue execution of ad proposal {ProposalId}", proposalId);
            return null;
        }
    }
}
```

(`AdProposalExecutionJob` is created in Task 11. Until then, add this placeholder-free stub so the solution compiles — Task 11 replaces the whole file:)

```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Execution;

public class AdProposalExecutionJob
{
    public Task RunAsync(int proposalId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Replaced by the execution pipeline in Task 11.");
}
```

- [ ] **Step 8: Create the autonomy service** (namespace `...Proposals.Autonomy`)

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Autonomy;

public interface IAdAutonomyService
{
    /// <summary>Approves the (tracked, unsaved or loaded) proposal as the system principal when its autonomy mode allows it. Caller saves.</summary>
    Task<bool> TryAutoApproveAsync(AdProposal proposal, IReadOnlyList<AdAction> actions, CancellationToken cancellationToken);
}
```

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Audit;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Autonomy;

/// <summary>
/// Spec 6.4. AutoWithinLimits: auto-approve only an Allowed evaluation. Auto: auto-approve anything the
/// hard limits do not deny (the admin chose to skip the human gates). Evaluated with channel System,
/// which never satisfies a second-approver rule and always counts as "not web".
/// </summary>
public sealed class AdAutonomyService : IAdAutonomyService
{
    private readonly IAdGovernanceRepository _governance;
    private readonly IAdLimitsContextBuilder _limitsContext;
    private readonly IAdLimitsEvaluator _evaluator;
    private readonly IAdAuditLog _audit;
    private readonly TimeProvider _time;

    public AdAutonomyService(IAdGovernanceRepository governance, IAdLimitsContextBuilder limitsContext,
        IAdLimitsEvaluator evaluator, IAdAuditLog audit, TimeProvider time)
    {
        _governance = governance;
        _limitsContext = limitsContext;
        _evaluator = evaluator;
        _audit = audit;
        _time = time;
    }

    public async Task<bool> TryAutoApproveAsync(AdProposal proposal, IReadOnlyList<AdAction> actions, CancellationToken cancellationToken)
    {
        var mode = (await _governance.GetAutonomySettingsAsync(cancellationToken))
            .FirstOrDefault(s => s.Platform == proposal.Platform && s.ActionType == proposal.ActionType)?.Mode
            ?? AdAutonomyMode.ProposeOnly;
        if (mode == AdAutonomyMode.ProposeOnly)
        {
            return false;
        }

        var request = await _limitsContext.BuildAsync(proposal, actions, ApprovalChannel.System, AdSystemPrincipals.Autonomy, cancellationToken);
        var evaluation = _evaluator.Evaluate(request);
        if (!IsAutoApprovable(mode, evaluation.Decision))
        {
            return false;
        }

        var now = _time.GetUtcNow().UtcDateTime;
        proposal.RecordApproval(AdSystemPrincipals.Autonomy, ApprovalChannel.System, now, isFinal: true);
        _audit.Append(AdAuditEvents.ForProposal(proposal, AdAuditEventType.Approved, AdSystemPrincipals.Autonomy,
            AdActorKind.System, ApprovalChannel.System,
            new { autonomyMode = mode, evaluation = evaluation.Decision, evaluation.Reason }, now));
        return true;
    }

    public static bool IsAutoApprovable(AdAutonomyMode mode, AdLimitsDecision decision) => mode switch
    {
        AdAutonomyMode.AutoWithinLimits => decision == AdLimitsDecision.Allowed,
        AdAutonomyMode.Auto => decision != AdLimitsDecision.Denied,
        _ => false,
    };
}
```

- [ ] **Step 9: Create the module and register it**

`MarketingAdsProposalsModule.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Autonomy;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Execution;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.ReadModel;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Persistence.MarketingAds;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals;

public static class MarketingAdsProposalsModule
{
    public static IServiceCollection AddMarketingAdsProposalsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AdProposalOptions>()
            .Bind(configuration.GetSection(AdProposalOptions.SectionName))
            .Validate(o => o.ProposalTtlHours > 0 && o.StuckApprovedRequeueMinutes > 0,
                "Ads:ProposalTtlHours and Ads:StuckApprovedRequeueMinutes must be positive.")
            .ValidateOnStart();

        services.AddScoped<IAdProposalRepository, AdProposalRepository>();
        services.AddScoped<IAdGovernanceRepository, AdGovernanceRepository>();
        services.AddScoped<IAgentRunRepository, AgentRunRepository>();
        services.AddScoped<IAdAuditLog, AdAuditLog>();

        services.AddSingleton<IAdLimitsEvaluator, AdLimitsEvaluator>();
        services.AddScoped<IAdLimitsContextBuilder, AdLimitsContextBuilder>();
        // AdsDbContext is registered by C1 only when AdsDatabase:ConnectionString is really configured.
        services.AddScoped<IAdsReadModel>(sp => sp.GetService<AdsDbContext>() is { } db
            ? new AdsDbReadModel(db)
            : new UnavailableAdsReadModel());
        services.AddScoped<IAdAutonomyService, AdAutonomyService>();

        // PR C4 replaces this line with its claims-based resolver. Until then nothing can be approved.
        services.AddScoped<IApprovalChannelResolver, DenyAllApprovalChannelResolver>();

        services.AddScoped<IAdProposalExecutionEnqueuer, HangfireAdProposalExecutionEnqueuer>();
        services.AddScoped<AdProposalExecutionJob>();

        // MediatR handlers and the IRecurringJob (AdProposalExpiryJob) are discovered by assembly scan.
        return services;
    }
}
```

In `ApplicationModule.cs` add `using Anela.Heblo.Application.Features.MarketingAds.Proposals;` and, directly after the line that registers C1's MarketingAds module (found in *Before you start*; if there is none, after `services.AddMarketingPerformanceModule(configuration);`):

```csharp
        services.AddMarketingAdsProposalsModule(configuration);
```

- [ ] **Step 10: Create the test harness** — `backend/test/Anela.Heblo.Tests/Features/MarketingAds/Proposals/ProposalTestHarness.cs`. It wires real repositories on an InMemory `ApplicationDbContext` (with HasData seeds via `EnsureCreated`), the real evaluator/context builder/autonomy service, and mocks for identity, channel resolver, enqueuer and the ads read model. `NewContext()` opens a **second** context on the same store for verification.

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Autonomy;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Execution;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.ReadModel;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.MarketingAds;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals;

internal sealed class ProposalTestHarness : IDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);
    private readonly DbContextOptions<ApplicationDbContext> _options =
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    public ProposalTestHarness()
    {
        Context = new ApplicationDbContext(_options);
        Context.Database.EnsureCreated();
        Proposals = new AdProposalRepository(Context);
        Governance = new AdGovernanceRepository(Context);
        AgentRuns = new AgentRunRepository(Context);
        Audit = new AdAuditLog(Context);
        ContextBuilder = new AdLimitsContextBuilder(Governance, Proposals, ReadModel.Object, Time);
        Autonomy = new AdAutonomyService(Governance, ContextBuilder, Evaluator, Audit, Time);

        ActAs("chief");
        ChannelResolver.Setup(r => r.Resolve()).Returns(ApprovalChannelResolution.Allow(ApprovalChannel.Web));
        Enqueuer.Setup(e => e.EnqueueExecution(It.IsAny<int>())).Returns("job-1");
        ReadModel.Setup(r => r.IsAccountManagedAsync(It.IsAny<AdPlatform>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        ReadModel.Setup(r => r.GetAdGroupOccupancyAsync(It.IsAny<AdPlatform>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AdPlatform _, string _, IReadOnlyCollection<string> ids, CancellationToken _) =>
                ids.ToDictionary(id => id, id => new AdGroupOccupancy($"grp-{id}", 3)));
        ReadModel.Setup(r => r.GetEntityNamesAsync(It.IsAny<AdPlatform>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());
    }

    public FakeTimeProvider Time { get; } = new(Start);
    public ApplicationDbContext Context { get; }
    public AdProposalRepository Proposals { get; }
    public AdGovernanceRepository Governance { get; }
    public AgentRunRepository AgentRuns { get; }
    public AdAuditLog Audit { get; }
    public AdLimitsEvaluator Evaluator { get; } = new();
    public AdLimitsContextBuilder ContextBuilder { get; }
    public AdAutonomyService Autonomy { get; }
    public Mock<ICurrentUserService> CurrentUser { get; } = new();
    public Mock<IApprovalChannelResolver> ChannelResolver { get; } = new();
    public Mock<IAdProposalExecutionEnqueuer> Enqueuer { get; } = new();
    public Mock<IAdsReadModel> ReadModel { get; } = new();
    public IOptions<AdProposalOptions> Options { get; } = Microsoft.Extensions.Options.Options.Create(new AdProposalOptions());
    public DateTime Now => Time.GetUtcNow().UtcDateTime;

    public ApplicationDbContext NewContext() => new(_options);

    public void ActAs(string principal) =>
        CurrentUser.Setup(c => c.GetCurrentUser()).Returns(new CurrentUser(principal, principal, $"{principal}@anela.cz", true));

    public void UseChannel(ApprovalChannel channel) =>
        ChannelResolver.Setup(r => r.Resolve()).Returns(ApprovalChannelResolution.Allow(channel));

    public void DenyChannel() =>
        ChannelResolver.Setup(r => r.Resolve()).Returns(ApprovalChannelResolution.Deny(DenyAllApprovalChannelResolver.Reason));

    public static AdAction PauseAction(string adId, string account = "acc-1", AdPlatform platform = AdPlatform.GoogleAds) =>
        new(AdActionType.PauseAd, platform, account, AdEntityLevel.Ad, adId, AdActionValues.Enabled, AdActionValues.Paused, new Dictionary<string, string>());

    /// <summary>Saves a Pending PauseAd proposal (version 1) and returns its id.</summary>
    public async Task<int> SeedPendingAsync(int actionCount = 1, string creator = "agent-1", AdPlatform platform = AdPlatform.GoogleAds)
    {
        var actions = Enumerable.Range(1, actionCount).Select(i => PauseAction($"ad-{i}", platform: platform)).ToList();
        var proposal = AdProposal.Create(new AdProposalDraft(platform.ToString(), "acc-1", nameof(AdActionType.PauseAd), creator,
            AdActorKind.Agent, null, AdActionJson.Serialize(actions), actions.Count, "reason"), Now, Options.Value.ProposalTtl);
        Proposals.Add(proposal);
        await Proposals.SaveChangesAsync(CancellationToken.None);
        Context.ChangeTracker.Clear();
        return proposal.Id;
    }

    /// <summary>Saves an Approved proposal ready for the execution job.</summary>
    public async Task<int> SeedApprovedAsync(int actionCount = 1, AdPlatform platform = AdPlatform.GoogleAds)
    {
        var id = await SeedPendingAsync(actionCount, platform: platform);
        var proposal = await Proposals.GetForUpdateAsync(id, CancellationToken.None);
        proposal!.RecordApproval("chief", ApprovalChannel.Web, Now, isFinal: true);
        await Proposals.SaveChangesAsync(CancellationToken.None);
        Context.ChangeTracker.Clear();
        return id;
    }

    public async Task SetKillSwitchAsync(string scope, bool enabled)
    {
        await using var ctx = NewContext();
        var row = await ctx.AdKillSwitches.FirstOrDefaultAsync(s => s.Scope == scope);
        if (row is null)
        {
            ctx.AdKillSwitches.Add(new AdKillSwitch { Scope = scope, ExecutionEnabled = enabled, UpdatedAt = Now, UpdatedBy = "test" });
        }
        else
        {
            row.ExecutionEnabled = enabled;
        }
        await ctx.SaveChangesAsync();
    }

    public async Task SetAutonomyAsync(string platform, string actionType, AdAutonomyMode mode)
    {
        await using var ctx = NewContext();
        var row = await ctx.AdAutonomySettings.SingleAsync(s => s.Platform == platform && s.ActionType == actionType);
        row.Mode = mode;
        await ctx.SaveChangesAsync();
    }

    public async Task SetLimitAsync(string platform, string actionType, string key, int value)
    {
        await using var ctx = NewContext();
        var row = await ctx.AdLimitSettings.SingleAsync(l => l.Platform == platform && l.ActionType == actionType && l.Key == key);
        row.Value = value;
        await ctx.SaveChangesAsync();
    }

    public void Dispose() => Context.Dispose();
}
```

- [ ] **Step 11: Write the autonomy and module tests**

`Autonomy/AdAutonomyServiceTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Autonomy;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.ReadModel;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Autonomy;

public class AdAutonomyServiceTests
{
    [Theory]
    [InlineData(AdAutonomyMode.ProposeOnly, AdLimitsDecision.Allowed, false)]
    [InlineData(AdAutonomyMode.AutoWithinLimits, AdLimitsDecision.Allowed, true)]
    [InlineData(AdAutonomyMode.AutoWithinLimits, AdLimitsDecision.NeedsWebApproval, false)]
    [InlineData(AdAutonomyMode.AutoWithinLimits, AdLimitsDecision.NeedsSecondApprover, false)]
    [InlineData(AdAutonomyMode.Auto, AdLimitsDecision.NeedsWebApproval, true)]
    [InlineData(AdAutonomyMode.Auto, AdLimitsDecision.NeedsSecondApprover, true)]
    [InlineData(AdAutonomyMode.Auto, AdLimitsDecision.Denied, false)]
    public void IsAutoApprovable_FollowsSpec64(AdAutonomyMode mode, AdLimitsDecision decision, bool expected) =>
        AdAutonomyService.IsAutoApprovable(mode, decision).Should().Be(expected);

    [Fact]
    public async Task SeededProposeOnly_NeverAutoApproves()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync();
        var proposal = await h.Proposals.GetForUpdateAsync(id, CancellationToken.None);

        var approved = await h.Autonomy.TryAutoApproveAsync(proposal!, new[] { ProposalTestHarness.PauseAction("ad-1") }, CancellationToken.None);

        approved.Should().BeFalse();
        proposal!.Status.Should().Be(AdProposalStatus.Pending);
    }

    [Fact]
    public async Task AutoWithinLimits_AllowedProposal_IsApprovedBySystemAndAudited()
    {
        using var h = new ProposalTestHarness();
        await h.SetAutonomyAsync("GoogleAds", "PauseAd", AdAutonomyMode.AutoWithinLimits);
        var id = await h.SeedPendingAsync();
        var proposal = await h.Proposals.GetForUpdateAsync(id, CancellationToken.None);

        var approved = await h.Autonomy.TryAutoApproveAsync(proposal!, new[] { ProposalTestHarness.PauseAction("ad-1") }, CancellationToken.None);
        await h.Proposals.SaveChangesAsync(CancellationToken.None);

        approved.Should().BeTrue();
        await using var verify = h.NewContext();
        (await verify.AdProposals.SingleAsync(p => p.Id == id)).Status.Should().Be(AdProposalStatus.Approved);
        var approval = await verify.AdProposalApprovals.SingleAsync(a => a.ProposalId == id);
        approval.Principal.Should().Be("system:autonomy");
        approval.Channel.Should().Be(ApprovalChannel.System);
        (await verify.AdAuditEvents.SingleAsync(e => e.ProposalId == id)).ActorKind.Should().Be(AdActorKind.System);
    }

    [Fact]
    public async Task AutoWithinLimits_PauseThatEmptiesAnAdGroup_StaysPending()
    {
        using var h = new ProposalTestHarness();
        await h.SetAutonomyAsync("GoogleAds", "PauseAd", AdAutonomyMode.AutoWithinLimits);
        h.ReadModel.Setup(r => r.GetAdGroupOccupancyAsync(It.IsAny<AdPlatform>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, AdGroupOccupancy> { ["ad-1"] = new("grp-1", 1) });
        var id = await h.SeedPendingAsync();
        var proposal = await h.Proposals.GetForUpdateAsync(id, CancellationToken.None);

        var approved = await h.Autonomy.TryAutoApproveAsync(proposal!, new[] { ProposalTestHarness.PauseAction("ad-1") }, CancellationToken.None);

        approved.Should().BeFalse();
    }
}
```

`MarketingAdsProposalsModuleTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Proposals;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.ReadModel;
using Anela.Heblo.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals;

public class MarketingAdsProposalsModuleTests
{
    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddMarketingAdsProposalsModule(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }

    [Fact]
    public void DefaultResolver_DeniesEveryApprovalUntilC4()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        var resolution = scope.ServiceProvider.GetRequiredService<IApprovalChannelResolver>().Resolve();

        resolution.Allowed.Should().BeFalse();
        resolution.DenyReason.Should().Be(DenyAllApprovalChannelResolver.Reason);
    }

    [Fact]
    public void WithoutAdsDbContext_ReadModelIsUnavailable()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IAdsReadModel>().Should().BeOfType<UnavailableAdsReadModel>();
    }
}
```

- [ ] **Step 12: Build and run**

```bash
MSBUILDDISABLENODEREUSE=1 dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~MarketingAds.Proposals|FullyQualifiedName~ErrorHandlingTests|FullyQualifiedName~LocalizationCoverageTests|FullyQualifiedName~ApplicationStartupTests"
```

Expected: PASS. (`ApplicationStartupTests` proves the whole DI graph still resolves.)

- [ ] **Step 13: Commit**

```bash
git add backend/src/Anela.Heblo.Application backend/test/Anela.Heblo.Tests frontend/src/i18n.ts
git commit -m "feat: wire the ad proposal module with deny-all approval channel and autonomy

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 9: Submit and revise proposals

**Files:**
- Create: `.../Proposals/Contracts/AdProposalDtos.cs`, `.../Proposals/AdProposalDtoMapper.cs`
- Create: `.../Proposals/UseCases/SubmitAdProposal/{SubmitAdProposalRequest,SubmitAdProposalResponse,SubmitAdProposalHandler}.cs`
- Create: `.../Proposals/UseCases/ReviseAdProposal/{ReviseAdProposalRequest,ReviseAdProposalResponse,ReviseAdProposalHandler}.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/Proposals/UseCases/SubmitAdProposalHandlerTests.cs`, `ReviseAdProposalHandlerTests.cs`

**Interfaces:**
- Consumes: Tasks 4–8.
- Produces:
  - DTO classes (namespace `...Proposals.Contracts`): `AdLimitsEvaluationDto { AdLimitsDecision Decision; string? Reason }`, `AdProposalSummaryDto`, `AdProposalVersionDto`, `AdProposalApprovalDto`, `AdActionExecutionDto`, `AdProposalDetailDto` (fields below)
  - `AdProposalDtoMapper.ToDto(AdLimitsEvaluation)`, `.ToSummary(AdProposal, IReadOnlyDictionary<string,string> names)`, `.ToDetail(AdProposal, IReadOnlyDictionary<string,string> names, AdLimitsEvaluation? currentEvaluation)`, `.TargetIds(AdProposal) : IReadOnlyCollection<string>`
  - `SubmitAdProposalRequest { AdPlatform Platform; string AccountExternalId; AdActionType ActionType; List<AdActionInputDto> Actions; string Reasoning; Guid? AgentRunId }` → `SubmitAdProposalResponse { int ProposalId; int Version; AdProposalStatus Status; bool AutoApproved; AdLimitsEvaluationDto Evaluation; List<string> DiffLines }`
  - `ReviseAdProposalRequest { int Id; int ExpectedVersion; List<AdActionInputDto> Actions; string Reasoning }` → `ReviseAdProposalResponse` (same fields as submit)

Rules: identity from `ICurrentUserService` (Id = Entra oid); shape validation → 400 `MarketingAdsInvalidProposal` with `Params["Errors"]`; `AgentRunId` must be a *Running* run of the same principal (then `CreatedByKind = Agent`), otherwise 404 `MarketingAdsAgentRunNotFound`; the proposal is evaluated for the **Web** channel and rejected without being stored when `Denied` (422 `MarketingAdsLimitsDenied`, `Params["Reason"]`); otherwise stored + `Submitted` audit + autonomy attempt in **one** SaveChanges; execution is enqueued only after the save. Revise: only the creator (403 `MarketingAdsNotProposalOwner`), `ExpectedVersion` must be current (409), resets the expiry, voids approvals, audits `Revised`, re-runs autonomy.

- [ ] **Step 1: Write the failing tests**

`SubmitAdProposalHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.SubmitAdProposal;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.UseCases;

public class SubmitAdProposalHandlerTests
{
    private static SubmitAdProposalHandler Handler(ProposalTestHarness h) => new(
        h.Proposals, h.AgentRuns, h.Audit, h.ContextBuilder, h.Evaluator, h.Autonomy, h.Enqueuer.Object,
        h.CurrentUser.Object, h.Time, h.Options, NullLogger<SubmitAdProposalHandler>.Instance);

    private static SubmitAdProposalRequest PauseRequest(params string[] adIds) => new()
    {
        Platform = AdPlatform.GoogleAds,
        AccountExternalId = "acc-1",
        ActionType = AdActionType.PauseAd,
        Actions = adIds.Select(id => new AdActionInputDto { TargetLevel = AdEntityLevel.Ad, TargetExternalId = id }).ToList(),
        Reasoning = "CTR fell below 0.5 % for 30 days",
    };

    [Fact]
    public async Task ValidProposal_IsStoredPendingWithVersionAuditAndDiff()
    {
        using var h = new ProposalTestHarness();

        var response = await Handler(h).Handle(PauseRequest("ad-1", "ad-2"), CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Version.Should().Be(1);
        response.Status.Should().Be(AdProposalStatus.Pending);
        response.AutoApproved.Should().BeFalse();
        response.Evaluation.Decision.Should().Be(AdLimitsDecision.Allowed);
        response.DiffLines.Should().HaveCount(2);

        await using var verify = h.NewContext();
        var stored = await verify.AdProposals.Include(p => p.Versions).SingleAsync(p => p.Id == response.ProposalId);
        stored.CreatedByPrincipal.Should().Be("chief");
        stored.CreatedByKind.Should().Be(AdActorKind.Human);
        stored.ExpiresAt.Should().Be(h.Now.AddHours(72));
        stored.Versions.Single().ActionCount.Should().Be(2);
        var audit = await verify.AdAuditEvents.SingleAsync(e => e.ProposalId == response.ProposalId);
        audit.EventType.Should().Be(AdAuditEventType.Submitted);
        audit.Channel.Should().Be(ApprovalChannel.Web);
        h.Enqueuer.Verify(e => e.EnqueueExecution(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task InvalidShape_ReturnsErrorsAndStoresNothing()
    {
        using var h = new ProposalTestHarness();

        var response = await Handler(h).Handle(PauseRequest(), CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsInvalidProposal);
        response.Params!["Errors"].Should().Contain("At least one action");
        await using var verify = h.NewContext();
        (await verify.AdProposals.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task HardLimitDenied_IsRejectedWithReasonAndNotStored()
    {
        using var h = new ProposalTestHarness();
        h.ReadModel.Setup(r => r.IsAccountManagedAsync(AdPlatform.GoogleAds, "acc-1", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var response = await Handler(h).Handle(PauseRequest("ad-1"), CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsLimitsDenied);
        response.Params!["Reason"].Should().Be(AdLimitReasons.AccountNotManaged);
        await using var verify = h.NewContext();
        (await verify.AdProposals.CountAsync()).Should().Be(0);
        (await verify.AdAuditEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AgentRunOfTheCaller_MarksTheProposalAsAgentCreatedOverMcp()
    {
        using var h = new ProposalTestHarness();
        h.ActAs("agent-1");
        var runId = Guid.NewGuid();
        h.AgentRuns.Add(AgentRun.Start(runId, "paid-search", "agent-1", "[]", h.Now));
        await h.AgentRuns.SaveChangesAsync(CancellationToken.None);
        var request = PauseRequest("ad-1");
        request.AgentRunId = runId;

        var response = await Handler(h).Handle(request, CancellationToken.None);

        await using var verify = h.NewContext();
        var stored = await verify.AdProposals.SingleAsync(p => p.Id == response.ProposalId);
        stored.CreatedByKind.Should().Be(AdActorKind.Agent);
        stored.AgentRunId.Should().Be(runId);
        (await verify.AdAuditEvents.SingleAsync()).Channel.Should().Be(ApprovalChannel.Mcp);
    }

    [Fact]
    public async Task AgentRunOfAnotherPrincipal_IsRejected()
    {
        using var h = new ProposalTestHarness();
        var runId = Guid.NewGuid();
        h.AgentRuns.Add(AgentRun.Start(runId, "paid-search", "agent-1", "[]", h.Now));
        await h.AgentRuns.SaveChangesAsync(CancellationToken.None);
        var request = PauseRequest("ad-1");
        request.AgentRunId = runId;

        var response = await Handler(h).Handle(request, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsAgentRunNotFound);
    }

    [Fact]
    public async Task AutoWithinLimits_ApprovesAndEnqueuesAfterSaving()
    {
        using var h = new ProposalTestHarness();
        await h.SetAutonomyAsync("GoogleAds", "PauseAd", AdAutonomyMode.AutoWithinLimits);

        var response = await Handler(h).Handle(PauseRequest("ad-1"), CancellationToken.None);

        response.AutoApproved.Should().BeTrue();
        response.Status.Should().Be(AdProposalStatus.Approved);
        h.Enqueuer.Verify(e => e.EnqueueExecution(response.ProposalId), Times.Once);
        await using var verify = h.NewContext();
        (await verify.AdAuditEvents.Where(e => e.ProposalId == response.ProposalId).Select(e => e.EventType).ToListAsync())
            .Should().BeEquivalentTo(new[] { AdAuditEventType.Submitted, AdAuditEventType.Approved });
    }

    [Fact]
    public async Task AnonymousCaller_IsUnauthorized()
    {
        using var h = new ProposalTestHarness();
        h.CurrentUser.Setup(c => c.GetCurrentUser()).Returns(new Anela.Heblo.Domain.Features.Users.CurrentUser(null, "Anonymous", null, false));

        var response = await Handler(h).Handle(PauseRequest("ad-1"), CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.Unauthorized);
    }
}
```

`ReviseAdProposalHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ReviseAdProposal;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.UseCases;

public class ReviseAdProposalHandlerTests
{
    private static ReviseAdProposalHandler Handler(ProposalTestHarness h) => new(
        h.Proposals, h.Audit, h.ContextBuilder, h.Evaluator, h.Autonomy, h.Enqueuer.Object,
        h.CurrentUser.Object, h.Time, h.Options, NullLogger<ReviseAdProposalHandler>.Instance);

    private static ReviseAdProposalRequest Revision(int id, int expectedVersion, params string[] adIds) => new()
    {
        Id = id,
        ExpectedVersion = expectedVersion,
        Actions = adIds.Select(a => new AdActionInputDto { TargetLevel = AdEntityLevel.Ad, TargetExternalId = a }).ToList(),
        Reasoning = "narrowed to one ad",
    };

    [Fact]
    public async Task Creator_RevisesCurrentVersion_InsertsVersionTwoAndVoidsApprovals()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync(actionCount: 2, creator: "agent-1");
        await using (var ctx = h.NewContext())
        {
            var p = await ctx.AdProposals.Include(x => x.Versions).Include(x => x.Approvals).SingleAsync(x => x.Id == id);
            p.RecordApproval("chief", ApprovalChannel.Web, h.Now, isFinal: false);
            await ctx.SaveChangesAsync();
        }
        h.ActAs("agent-1");
        h.Time.Advance(TimeSpan.FromHours(10));

        var response = await Handler(h).Handle(Revision(id, 1, "ad-1"), CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Version.Should().Be(2);
        await using var verify = h.NewContext();
        var stored = await verify.AdProposals.Include(p => p.Versions).Include(p => p.Approvals).SingleAsync(p => p.Id == id);
        stored.Versions.Select(v => v.Version).Should().BeEquivalentTo(new[] { 1, 2 });
        stored.Versions.Single(v => v.Version == 1).ActionCount.Should().Be(2, "version 1 is immutable");
        stored.GetCurrentApprovers().Should().BeEmpty();
        stored.ExpiresAt.Should().Be(h.Now.AddHours(72));
        (await verify.AdAuditEvents.SingleAsync(e => e.ProposalId == id)).EventType.Should().Be(AdAuditEventType.Revised);
    }

    [Fact]
    public async Task StaleExpectedVersion_Returns409()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync(creator: "agent-1");
        h.ActAs("agent-1");

        var response = await Handler(h).Handle(Revision(id, 2, "ad-1"), CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsProposalVersionMismatch);
    }

    [Fact]
    public async Task NonCreator_IsForbidden()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync(creator: "agent-1");
        h.ActAs("agent-2");

        var response = await Handler(h).Handle(Revision(id, 1, "ad-1"), CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsNotProposalOwner);
    }

    [Fact]
    public async Task ExpiredProposal_CannotBeRevised()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync(creator: "agent-1");
        h.ActAs("agent-1");
        h.Time.Advance(TimeSpan.FromHours(73));

        var response = await Handler(h).Handle(Revision(id, 1, "ad-1"), CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsProposalExpired);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `MSBUILDDISABLENODEREUSE=1 dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false`
Expected: FAIL — `The type or namespace name 'SubmitAdProposal' does not exist`.

- [ ] **Step 3: Create the DTOs** — `Contracts/AdProposalDtos.cs` (classes, never records):

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Contracts;

public class AdLimitsEvaluationDto
{
    public AdLimitsDecision Decision { get; set; }
    public string? Reason { get; set; }
}

public class AdProposalSummaryDto
{
    public int Id { get; set; }
    public int CurrentVersion { get; set; }
    public AdProposalStatus Status { get; set; }
    public AdPlatform Platform { get; set; }
    public string AccountExternalId { get; set; } = null!;
    public AdActionType ActionType { get; set; }
    public int ActionCount { get; set; }
    public string CreatedByPrincipal { get; set; } = null!;
    public AdActorKind CreatedByKind { get; set; }
    public Guid? AgentRunId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    /// <summary>Rendered by Heblo from the current version's actions.</summary>
    public List<string> DiffLines { get; set; } = new();
}

public class AdProposalVersionDto
{
    public int Version { get; set; }
    public int ActionCount { get; set; }
    /// <summary>The proposer's free text. Display separately from the diff, as untrusted text.</summary>
    public string Reasoning { get; set; } = null!;
    public string CreatedBy { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public List<string> DiffLines { get; set; } = new();
}

public class AdProposalApprovalDto
{
    public int Version { get; set; }
    public string Principal { get; set; } = null!;
    public ApprovalChannel Channel { get; set; }
    public ApprovalDecision Decision { get; set; }
    public string? Reason { get; set; }
    public DateTime At { get; set; }
}

public class AdActionExecutionDto
{
    public int Version { get; set; }
    public int ActionIndex { get; set; }
    public string TargetExternalId { get; set; } = null!;
    public AdActionExecutionState State { get; set; }
    public string? BeforeValue { get; set; }
    public string? AfterValue { get; set; }
    public string? PlatformResourceId { get; set; }
    public string? Error { get; set; }
    public DateTime ExecutedAt { get; set; }
    public AdActionRevertState RevertState { get; set; }
    public DateTime? RevertedAt { get; set; }
    public string? RevertError { get; set; }
}

public class AdProposalDetailDto
{
    public AdProposalSummaryDto Summary { get; set; } = new();
    public List<AdProposalVersionDto> Versions { get; set; } = new();
    public List<AdProposalApprovalDto> Approvals { get; set; } = new();
    public List<AdActionExecutionDto> Executions { get; set; } = new();
    /// <summary>Limits evaluation for the current user on the Web channel; null unless Pending.</summary>
    public AdLimitsEvaluationDto? CurrentEvaluation { get; set; }
}
```

- [ ] **Step 4: Create the mapper** — `AdProposalDtoMapper.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals;

public static class AdProposalDtoMapper
{
    public static AdLimitsEvaluationDto ToDto(AdLimitsEvaluation evaluation) =>
        new() { Decision = evaluation.Decision, Reason = evaluation.Reason };

    public static IReadOnlyCollection<string> TargetIds(AdProposal proposal) => proposal.Versions
        .SelectMany(v => AdActionJson.Deserialize(v.ActionsJson))
        .Select(a => a.TargetExternalId)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    public static AdProposalSummaryDto ToSummary(AdProposal proposal, IReadOnlyDictionary<string, string> names)
    {
        var current = proposal.GetCurrentVersion();
        return new AdProposalSummaryDto
        {
            Id = proposal.Id,
            CurrentVersion = proposal.CurrentVersion,
            Status = proposal.Status,
            Platform = Enum.Parse<AdPlatform>(proposal.Platform),
            AccountExternalId = proposal.AccountExternalId,
            ActionType = Enum.Parse<AdActionType>(proposal.ActionType),
            ActionCount = current.ActionCount,
            CreatedByPrincipal = proposal.CreatedByPrincipal,
            CreatedByKind = proposal.CreatedByKind,
            AgentRunId = proposal.AgentRunId,
            CreatedAt = proposal.CreatedAt,
            ExpiresAt = proposal.ExpiresAt,
            UpdatedAt = proposal.UpdatedAt,
            DiffLines = Render(current, names),
        };
    }

    public static AdProposalDetailDto ToDetail(AdProposal proposal, IReadOnlyDictionary<string, string> names, AdLimitsEvaluation? currentEvaluation) => new()
    {
        Summary = ToSummary(proposal, names),
        Versions = proposal.Versions.OrderByDescending(v => v.Version).Select(v => new AdProposalVersionDto
        {
            Version = v.Version,
            ActionCount = v.ActionCount,
            Reasoning = v.Reasoning,
            CreatedBy = v.CreatedBy,
            CreatedAt = v.CreatedAt,
            DiffLines = Render(v, names),
        }).ToList(),
        Approvals = proposal.Approvals.OrderBy(a => a.At).Select(a => new AdProposalApprovalDto
        {
            Version = a.Version, Principal = a.Principal, Channel = a.Channel, Decision = a.Decision, Reason = a.Reason, At = a.At,
        }).ToList(),
        Executions = proposal.Executions.OrderBy(e => e.Version).ThenBy(e => e.ActionIndex).Select(e => new AdActionExecutionDto
        {
            Version = e.Version, ActionIndex = e.ActionIndex, TargetExternalId = e.TargetExternalId, State = e.State,
            BeforeValue = e.BeforeValue, AfterValue = e.AfterValue, PlatformResourceId = e.PlatformResourceId, Error = e.Error,
            ExecutedAt = e.ExecutedAt, RevertState = e.RevertState, RevertedAt = e.RevertedAt, RevertError = e.RevertError,
        }).ToList(),
        CurrentEvaluation = currentEvaluation is null ? null : ToDto(currentEvaluation),
    };

    private static List<string> Render(AdProposalVersion version, IReadOnlyDictionary<string, string> names) =>
        AdProposalDiffRenderer.Render(AdActionJson.Deserialize(version.ActionsJson), names).ToList();
}
```

- [ ] **Step 5: Implement Submit**

`SubmitAdProposalRequest.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.SubmitAdProposal;

/// <summary>No data-annotation attributes: the handler returns structured validation errors agents can act on.</summary>
public class SubmitAdProposalRequest : IRequest<SubmitAdProposalResponse>
{
    public AdPlatform Platform { get; set; }
    public string AccountExternalId { get; set; } = string.Empty;
    public AdActionType ActionType { get; set; }
    public List<AdActionInputDto> Actions { get; set; } = new();
    public string Reasoning { get; set; } = string.Empty;
    /// <summary>The caller's own Running agent run, if the proposal comes from an agent.</summary>
    public Guid? AgentRunId { get; set; }
}
```

`SubmitAdProposalResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.SubmitAdProposal;

public class SubmitAdProposalResponse : BaseResponse
{
    public int ProposalId { get; set; }
    public int Version { get; set; }
    public AdProposalStatus Status { get; set; }
    public bool AutoApproved { get; set; }
    public AdLimitsEvaluationDto Evaluation { get; set; } = new();
    public List<string> DiffLines { get; set; } = new();

    public SubmitAdProposalResponse() { }
    public SubmitAdProposalResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

`SubmitAdProposalHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Audit;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Autonomy;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Execution;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.SubmitAdProposal;

public class SubmitAdProposalHandler : IRequestHandler<SubmitAdProposalRequest, SubmitAdProposalResponse>
{
    private static readonly IReadOnlyDictionary<string, string> NoNames = new Dictionary<string, string>();

    private readonly IAdProposalRepository _proposals;
    private readonly IAgentRunRepository _agentRuns;
    private readonly IAdAuditLog _audit;
    private readonly IAdLimitsContextBuilder _limitsContext;
    private readonly IAdLimitsEvaluator _evaluator;
    private readonly IAdAutonomyService _autonomy;
    private readonly IAdProposalExecutionEnqueuer _enqueuer;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;
    private readonly IOptions<AdProposalOptions> _options;
    private readonly ILogger<SubmitAdProposalHandler> _logger;

    public SubmitAdProposalHandler(IAdProposalRepository proposals, IAgentRunRepository agentRuns, IAdAuditLog audit,
        IAdLimitsContextBuilder limitsContext, IAdLimitsEvaluator evaluator, IAdAutonomyService autonomy,
        IAdProposalExecutionEnqueuer enqueuer, ICurrentUserService currentUser, TimeProvider time,
        IOptions<AdProposalOptions> options, ILogger<SubmitAdProposalHandler> logger)
    {
        _proposals = proposals;
        _agentRuns = agentRuns;
        _audit = audit;
        _limitsContext = limitsContext;
        _evaluator = evaluator;
        _autonomy = autonomy;
        _enqueuer = enqueuer;
        _currentUser = currentUser;
        _time = time;
        _options = options;
        _logger = logger;
    }

    public async Task<SubmitAdProposalResponse> Handle(SubmitAdProposalRequest request, CancellationToken cancellationToken)
    {
        var principal = _currentUser.GetCurrentUser().Id;
        if (string.IsNullOrWhiteSpace(principal)) return new SubmitAdProposalResponse(ErrorCodes.Unauthorized);

        var errors = AdProposalShapeValidator.Validate(request.Platform, request.AccountExternalId, request.ActionType, request.Actions, request.Reasoning);
        if (errors.Count > 0) return new SubmitAdProposalResponse(ErrorCodes.MarketingAdsInvalidProposal, AdProposalErrors.Params(errors));

        var creatorKind = await ResolveCreatorKindAsync(request.AgentRunId, principal, cancellationToken);
        if (creatorKind is null) return new SubmitAdProposalResponse(ErrorCodes.MarketingAdsAgentRunNotFound);

        var account = request.AccountExternalId.Trim();
        var actions = request.Actions.Select(a => AdActionFactory.Create(request.Platform, account, request.ActionType, a)).ToList();
        var now = _time.GetUtcNow().UtcDateTime;
        var proposal = AdProposal.Create(new AdProposalDraft(request.Platform.ToString(), account, request.ActionType.ToString(),
            principal, creatorKind.Value, request.AgentRunId, AdActionJson.Serialize(actions), actions.Count, request.Reasoning.Trim()),
            now, _options.Value.ProposalTtl);

        var evaluation = _evaluator.Evaluate(await _limitsContext.BuildAsync(proposal, actions, ApprovalChannel.Web, null, cancellationToken));
        if (evaluation.Decision == AdLimitsDecision.Denied)
        {
            return new SubmitAdProposalResponse(ErrorCodes.MarketingAdsLimitsDenied, AdProposalErrors.Reason(evaluation.Reason));
        }

        _proposals.Add(proposal);
        _audit.Append(AdAuditEvents.ForProposal(proposal, AdAuditEventType.Submitted, principal, creatorKind.Value,
            AdAuditEvents.SubmissionChannel(creatorKind.Value),
            new { actionCount = actions.Count, evaluation = evaluation.Decision, evaluation.Reason }, now));
        var autoApproved = await _autonomy.TryAutoApproveAsync(proposal, actions, cancellationToken);
        await _proposals.SaveChangesAsync(cancellationToken);

        if (autoApproved && _enqueuer.EnqueueExecution(proposal.Id) is null)
        {
            _logger.LogWarning("Ad proposal {ProposalId} was auto-approved but enqueueing failed; the expiry job re-enqueues it.", proposal.Id);
        }

        return new SubmitAdProposalResponse
        {
            ProposalId = proposal.Id,
            Version = proposal.CurrentVersion,
            Status = proposal.Status,
            AutoApproved = autoApproved,
            Evaluation = AdProposalDtoMapper.ToDto(evaluation),
            DiffLines = AdProposalDiffRenderer.Render(actions, NoNames).ToList(),
        };
    }

    private async Task<AdActorKind?> ResolveCreatorKindAsync(Guid? agentRunId, string principal, CancellationToken cancellationToken)
    {
        if (agentRunId is null) return AdActorKind.Human;
        var run = await _agentRuns.FindAsync(agentRunId.Value, cancellationToken);
        return run is not null && run.Principal == principal && run.Status == AgentRunStatus.Running
            ? AdActorKind.Agent
            : null;
    }
}
```

- [ ] **Step 6: Implement Revise**

`ReviseAdProposalRequest.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ReviseAdProposal;

public class ReviseAdProposalRequest : IRequest<ReviseAdProposalResponse>
{
    /// <summary>Set from the route by the controller.</summary>
    public int Id { get; set; }
    public int ExpectedVersion { get; set; }
    public List<AdActionInputDto> Actions { get; set; } = new();
    public string Reasoning { get; set; } = string.Empty;
}
```

`ReviseAdProposalResponse.cs` — identical body to `SubmitAdProposalResponse` with the class name `ReviseAdProposalResponse` (namespace `...UseCases.ReviseAdProposal`):

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ReviseAdProposal;

public class ReviseAdProposalResponse : BaseResponse
{
    public int ProposalId { get; set; }
    public int Version { get; set; }
    public AdProposalStatus Status { get; set; }
    public bool AutoApproved { get; set; }
    public AdLimitsEvaluationDto Evaluation { get; set; } = new();
    public List<string> DiffLines { get; set; } = new();

    public ReviseAdProposalResponse() { }
    public ReviseAdProposalResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

`ReviseAdProposalHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Audit;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Autonomy;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Execution;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ReviseAdProposal;

public class ReviseAdProposalHandler : IRequestHandler<ReviseAdProposalRequest, ReviseAdProposalResponse>
{
    private static readonly IReadOnlyDictionary<string, string> NoNames = new Dictionary<string, string>();

    private readonly IAdProposalRepository _proposals;
    private readonly IAdAuditLog _audit;
    private readonly IAdLimitsContextBuilder _limitsContext;
    private readonly IAdLimitsEvaluator _evaluator;
    private readonly IAdAutonomyService _autonomy;
    private readonly IAdProposalExecutionEnqueuer _enqueuer;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;
    private readonly IOptions<AdProposalOptions> _options;
    private readonly ILogger<ReviseAdProposalHandler> _logger;

    public ReviseAdProposalHandler(IAdProposalRepository proposals, IAdAuditLog audit, IAdLimitsContextBuilder limitsContext,
        IAdLimitsEvaluator evaluator, IAdAutonomyService autonomy, IAdProposalExecutionEnqueuer enqueuer,
        ICurrentUserService currentUser, TimeProvider time, IOptions<AdProposalOptions> options, ILogger<ReviseAdProposalHandler> logger)
    {
        _proposals = proposals;
        _audit = audit;
        _limitsContext = limitsContext;
        _evaluator = evaluator;
        _autonomy = autonomy;
        _enqueuer = enqueuer;
        _currentUser = currentUser;
        _time = time;
        _options = options;
        _logger = logger;
    }

    public async Task<ReviseAdProposalResponse> Handle(ReviseAdProposalRequest request, CancellationToken cancellationToken)
    {
        var principal = _currentUser.GetCurrentUser().Id;
        if (string.IsNullOrWhiteSpace(principal)) return new ReviseAdProposalResponse(ErrorCodes.Unauthorized);

        var now = _time.GetUtcNow().UtcDateTime;
        var proposal = await _proposals.GetForUpdateAsync(request.Id, cancellationToken);
        if (AdProposalGuards.CheckPendingVersion(proposal, request.ExpectedVersion, now) is { } guard) return new ReviseAdProposalResponse(guard);
        if (!string.Equals(proposal!.CreatedByPrincipal, principal, StringComparison.Ordinal)) return new ReviseAdProposalResponse(ErrorCodes.MarketingAdsNotProposalOwner);

        var platform = Enum.Parse<AdPlatform>(proposal.Platform);
        var actionType = Enum.Parse<AdActionType>(proposal.ActionType);
        var errors = AdProposalShapeValidator.Validate(platform, proposal.AccountExternalId, actionType, request.Actions, request.Reasoning);
        if (errors.Count > 0) return new ReviseAdProposalResponse(ErrorCodes.MarketingAdsInvalidProposal, AdProposalErrors.Params(errors));

        var actions = request.Actions.Select(a => AdActionFactory.Create(platform, proposal.AccountExternalId, actionType, a)).ToList();
        var fromVersion = proposal.CurrentVersion;
        proposal.Revise(AdActionJson.Serialize(actions), actions.Count, request.Reasoning.Trim(), principal, now, _options.Value.ProposalTtl);

        var evaluation = _evaluator.Evaluate(await _limitsContext.BuildAsync(proposal, actions, ApprovalChannel.Web, null, cancellationToken));
        if (evaluation.Decision == AdLimitsDecision.Denied)
        {
            _proposals.DiscardChanges();
            return new ReviseAdProposalResponse(ErrorCodes.MarketingAdsLimitsDenied, AdProposalErrors.Reason(evaluation.Reason));
        }

        _audit.Append(AdAuditEvents.ForProposal(proposal, AdAuditEventType.Revised, principal, proposal.CreatedByKind,
            AdAuditEvents.SubmissionChannel(proposal.CreatedByKind),
            new { fromVersion, actionCount = actions.Count, evaluation = evaluation.Decision, evaluation.Reason }, now));
        var autoApproved = await _autonomy.TryAutoApproveAsync(proposal, actions, cancellationToken);

        try
        {
            await _proposals.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Concurrent change while revising ad proposal {ProposalId}", request.Id);
            _proposals.DiscardChanges();
            return new ReviseAdProposalResponse(ErrorCodes.MarketingAdsConcurrentChange);
        }

        if (autoApproved && _enqueuer.EnqueueExecution(proposal.Id) is null)
        {
            _logger.LogWarning("Ad proposal {ProposalId} was auto-approved but enqueueing failed; the expiry job re-enqueues it.", proposal.Id);
        }

        return new ReviseAdProposalResponse
        {
            ProposalId = proposal.Id,
            Version = proposal.CurrentVersion,
            Status = proposal.Status,
            AutoApproved = autoApproved,
            Evaluation = AdProposalDtoMapper.ToDto(evaluation),
            DiffLines = AdProposalDiffRenderer.Render(actions, NoNames).ToList(),
        };
    }
}
```

- [ ] **Step 7: Run the tests**

```bash
MSBUILDDISABLENODEREUSE=1 dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~SubmitAdProposalHandlerTests|FullyQualifiedName~ReviseAdProposalHandlerTests"
```

Expected: PASS (7 + 4).

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingAds/Proposals backend/test/Anela.Heblo.Tests/Features/MarketingAds/Proposals/UseCases
git commit -m "feat: submit and revise ad proposals with limits evaluation and autonomy

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 10: Approve and reject (channel-bound, version-bound)

**Files:**
- Create: `.../Proposals/UseCases/ApproveAdProposal/{ApproveAdProposalRequest,ApproveAdProposalResponse,ApproveAdProposalHandler}.cs`
- Create: `.../Proposals/UseCases/RejectAdProposal/{RejectAdProposalRequest,RejectAdProposalResponse,RejectAdProposalHandler}.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/Proposals/UseCases/ApproveAdProposalHandlerTests.cs`, `RejectAdProposalHandlerTests.cs`

**Interfaces:**
- Consumes: `IApprovalChannelResolver` (the channel is an *input* of the decision, resolved server-side — never from the request body), Tasks 4–9.
- Produces:
  - `ApproveAdProposalRequest { int Id; int Version }` → `ApproveAdProposalResponse { int ProposalId; int Version; AdProposalStatus Status; bool AwaitingSecondApprover; string? ExecutionJobId }`
  - `RejectAdProposalRequest { int Id; int Version; string Reason }` → `RejectAdProposalResponse { int ProposalId; int Version; AdProposalStatus Status }`

Approve flow: principal → `Resolve()` (not allowed → 403 `MarketingAdsApprovalChannelDenied` with `Params["Reason"]`) → guard (404/409) → same principal already approved this version → 409 `MarketingAdsAlreadyApprovedByPrincipal` → evaluate with the resolved channel and approver: `Denied` → 422, `NeedsWebApproval` → 403 `MarketingAdsNeedsWebApproval`, `NeedsSecondApprover` → record the approval and stay Pending, `Allowed` → record and move to Approved → one SaveChanges (concurrency failure → 409 `MarketingAdsConcurrentChange`) → enqueue after save. The Executor is checked by the job, not here.

- [ ] **Step 1: Write the failing tests**

`ApproveAdProposalHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ApproveAdProposal;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.UseCases;

public class ApproveAdProposalHandlerTests
{
    private static ApproveAdProposalHandler Handler(ProposalTestHarness h) => new(
        h.Proposals, h.Audit, h.ContextBuilder, h.Evaluator, h.ChannelResolver.Object, h.Enqueuer.Object,
        h.CurrentUser.Object, h.Time, NullLogger<ApproveAdProposalHandler>.Instance);

    [Fact]
    public async Task WebApproval_OfCurrentVersion_ApprovesAuditsAndEnqueues()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync();

        var response = await Handler(h).Handle(new ApproveAdProposalRequest { Id = id, Version = 1 }, CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Status.Should().Be(AdProposalStatus.Approved);
        response.ExecutionJobId.Should().Be("job-1");
        await using var verify = h.NewContext();
        var approval = await verify.AdProposalApprovals.SingleAsync(a => a.ProposalId == id);
        approval.Principal.Should().Be("chief");
        approval.Channel.Should().Be(ApprovalChannel.Web);
        var audit = await verify.AdAuditEvents.SingleAsync(e => e.ProposalId == id);
        audit.EventType.Should().Be(AdAuditEventType.Approved);
        audit.Channel.Should().Be(ApprovalChannel.Web);
        h.Enqueuer.Verify(e => e.EnqueueExecution(id), Times.Once);
    }

    [Fact]
    public async Task DenyAllResolver_BlocksApproval_EvenForAnAdmin()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync();
        h.DenyChannel();

        var response = await Handler(h).Handle(new ApproveAdProposalRequest { Id = id, Version = 1 }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsApprovalChannelDenied);
        await using var verify = h.NewContext();
        (await verify.AdProposals.SingleAsync(p => p.Id == id)).Status.Should().Be(AdProposalStatus.Pending);
        h.Enqueuer.Verify(e => e.EnqueueExecution(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task OldVersion_Returns409()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync();

        var response = await Handler(h).Handle(new ApproveAdProposalRequest { Id = id, Version = 2 }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsProposalVersionMismatch);
    }

    [Fact]
    public async Task UnknownProposal_Returns404()
    {
        using var h = new ProposalTestHarness();

        var response = await Handler(h).Handle(new ApproveAdProposalRequest { Id = 999, Version = 1 }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsProposalNotFound);
    }

    [Fact]
    public async Task McpApproval_OverMcpThreshold_NeedsWebApproval()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync(actionCount: 4);
        h.UseChannel(ApprovalChannel.Mcp);

        var response = await Handler(h).Handle(new ApproveAdProposalRequest { Id = id, Version = 1 }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsNeedsWebApproval);
    }

    [Fact]
    public async Task SecondApprover_FirstStaysPending_SamePrincipalRejected_DifferentPrincipalApproves()
    {
        using var h = new ProposalTestHarness();
        await h.SetLimitAsync("GoogleAds", "PauseAd", "SecondApproverAbove", 1);
        var id = await h.SeedPendingAsync(actionCount: 2);

        var first = await Handler(h).Handle(new ApproveAdProposalRequest { Id = id, Version = 1 }, CancellationToken.None);
        h.Context.ChangeTracker.Clear();
        var again = await Handler(h).Handle(new ApproveAdProposalRequest { Id = id, Version = 1 }, CancellationToken.None);
        h.Context.ChangeTracker.Clear();
        h.ActAs("specialist");
        var second = await Handler(h).Handle(new ApproveAdProposalRequest { Id = id, Version = 1 }, CancellationToken.None);

        first.Status.Should().Be(AdProposalStatus.Pending);
        first.AwaitingSecondApprover.Should().BeTrue();
        again.ErrorCode.Should().Be(ErrorCodes.MarketingAdsAlreadyApprovedByPrincipal);
        second.Status.Should().Be(AdProposalStatus.Approved);
        h.Enqueuer.Verify(e => e.EnqueueExecution(id), Times.Once);
    }

    [Fact]
    public async Task ExpiredProposal_CannotBeApproved()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync();
        h.Time.Advance(TimeSpan.FromHours(72));

        var response = await Handler(h).Handle(new ApproveAdProposalRequest { Id = id, Version = 1 }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsProposalExpired);
    }

    [Fact]
    public async Task ConcurrentRevision_Returns409AndLeavesNothingTracked()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync();
        var handler = Handler(h);
        // Load the proposal into the handler's context, then let "someone else" bump its Revision.
        _ = await h.Proposals.GetForUpdateAsync(id, CancellationToken.None);
        await using (var other = h.NewContext())
        {
            var p = await other.AdProposals.Include(x => x.Versions).Include(x => x.Approvals).SingleAsync(x => x.Id == id);
            p.Touch(h.Now);
            await other.SaveChangesAsync();
        }

        var response = await handler.Handle(new ApproveAdProposalRequest { Id = id, Version = 1 }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsConcurrentChange);
        h.Context.ChangeTracker.Entries().Should().BeEmpty();
        h.Enqueuer.Verify(e => e.EnqueueExecution(It.IsAny<int>()), Times.Never);
    }
}
```

The concurrency test relies on the handler getting the already-tracked (stale) instance from the context's identity map, as happens when two requests race on Postgres. The InMemory provider enforces `IsConcurrencyToken()`; the real-Postgres behaviour is pinned in Task 3.

`RejectAdProposalHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.RejectAdProposal;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.UseCases;

public class RejectAdProposalHandlerTests
{
    private static RejectAdProposalHandler Handler(ProposalTestHarness h) => new(
        h.Proposals, h.Audit, h.ChannelResolver.Object, h.CurrentUser.Object, h.Time, NullLogger<RejectAdProposalHandler>.Instance);

    [Fact]
    public async Task McpReject_StoresReasonChannelAndAudit()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync();
        h.UseChannel(ApprovalChannel.Mcp);

        var response = await Handler(h).Handle(new RejectAdProposalRequest { Id = id, Version = 1, Reason = "brand term" }, CancellationToken.None);

        response.Status.Should().Be(AdProposalStatus.Rejected);
        await using var verify = h.NewContext();
        var decision = await verify.AdProposalApprovals.SingleAsync(a => a.ProposalId == id);
        decision.Decision.Should().Be(ApprovalDecision.Rejected);
        decision.Reason.Should().Be("brand term");
        decision.Channel.Should().Be(ApprovalChannel.Mcp);
        (await verify.AdAuditEvents.SingleAsync(e => e.ProposalId == id)).EventType.Should().Be(AdAuditEventType.Rejected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingReason_IsInvalid(string reason)
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync();

        var response = await Handler(h).Handle(new RejectAdProposalRequest { Id = id, Version = 1, Reason = reason }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsInvalidProposal);
    }

    [Fact]
    public async Task DeniedChannel_CannotReject()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync();
        h.DenyChannel();

        var response = await Handler(h).Handle(new RejectAdProposalRequest { Id = id, Version = 1, Reason = "no" }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsApprovalChannelDenied);
    }

    [Fact]
    public async Task StaleVersion_Returns409()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync();

        var response = await Handler(h).Handle(new RejectAdProposalRequest { Id = id, Version = 3, Reason = "no" }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsProposalVersionMismatch);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `MSBUILDDISABLENODEREUSE=1 dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false`
Expected: FAIL — `The type or namespace name 'ApproveAdProposal' does not exist`.

- [ ] **Step 3: Implement Approve**

`ApproveAdProposalRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ApproveAdProposal;

public class ApproveAdProposalRequest : IRequest<ApproveAdProposalResponse>
{
    /// <summary>Set from the route by the controller.</summary>
    public int Id { get; set; }
    /// <summary>Must equal the proposal's current version.</summary>
    public int Version { get; set; }
}
```

`ApproveAdProposalResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ApproveAdProposal;

public class ApproveAdProposalResponse : BaseResponse
{
    public int ProposalId { get; set; }
    public int Version { get; set; }
    public AdProposalStatus Status { get; set; }
    public bool AwaitingSecondApprover { get; set; }
    public string? ExecutionJobId { get; set; }

    public ApproveAdProposalResponse() { }
    public ApproveAdProposalResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

`ApproveAdProposalHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Audit;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Execution;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ApproveAdProposal;

public class ApproveAdProposalHandler : IRequestHandler<ApproveAdProposalRequest, ApproveAdProposalResponse>
{
    private readonly IAdProposalRepository _proposals;
    private readonly IAdAuditLog _audit;
    private readonly IAdLimitsContextBuilder _limitsContext;
    private readonly IAdLimitsEvaluator _evaluator;
    private readonly IApprovalChannelResolver _channelResolver;
    private readonly IAdProposalExecutionEnqueuer _enqueuer;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;
    private readonly ILogger<ApproveAdProposalHandler> _logger;

    public ApproveAdProposalHandler(IAdProposalRepository proposals, IAdAuditLog audit, IAdLimitsContextBuilder limitsContext,
        IAdLimitsEvaluator evaluator, IApprovalChannelResolver channelResolver, IAdProposalExecutionEnqueuer enqueuer,
        ICurrentUserService currentUser, TimeProvider time, ILogger<ApproveAdProposalHandler> logger)
    {
        _proposals = proposals;
        _audit = audit;
        _limitsContext = limitsContext;
        _evaluator = evaluator;
        _channelResolver = channelResolver;
        _enqueuer = enqueuer;
        _currentUser = currentUser;
        _time = time;
        _logger = logger;
    }

    public async Task<ApproveAdProposalResponse> Handle(ApproveAdProposalRequest request, CancellationToken cancellationToken)
    {
        var principal = _currentUser.GetCurrentUser().Id;
        if (string.IsNullOrWhiteSpace(principal)) return new ApproveAdProposalResponse(ErrorCodes.Unauthorized);

        var resolution = _channelResolver.Resolve();
        if (!resolution.Allowed) return new ApproveAdProposalResponse(ErrorCodes.MarketingAdsApprovalChannelDenied, AdProposalErrors.Reason(resolution.DenyReason));

        var now = _time.GetUtcNow().UtcDateTime;
        var proposal = await _proposals.GetForUpdateAsync(request.Id, cancellationToken);
        if (AdProposalGuards.CheckPendingVersion(proposal, request.Version, now) is { } guard) return new ApproveAdProposalResponse(guard);
        if (proposal!.GetCurrentApprovers().Contains(principal, StringComparer.Ordinal)) return new ApproveAdProposalResponse(ErrorCodes.MarketingAdsAlreadyApprovedByPrincipal);

        var actions = AdActionJson.Deserialize(proposal.GetCurrentVersion().ActionsJson);
        var evaluation = _evaluator.Evaluate(await _limitsContext.BuildAsync(proposal, actions, resolution.Channel, principal, cancellationToken));
        switch (evaluation.Decision)
        {
            case AdLimitsDecision.Denied:
                return new ApproveAdProposalResponse(ErrorCodes.MarketingAdsLimitsDenied, AdProposalErrors.Reason(evaluation.Reason));
            case AdLimitsDecision.NeedsWebApproval:
                return new ApproveAdProposalResponse(ErrorCodes.MarketingAdsNeedsWebApproval, AdProposalErrors.Reason(evaluation.Reason));
        }

        var isFinal = evaluation.Decision == AdLimitsDecision.Allowed;
        proposal.RecordApproval(principal, resolution.Channel, now, isFinal);
        _audit.Append(AdAuditEvents.ForProposal(proposal, AdAuditEventType.Approved, principal, AdActorKind.Human, resolution.Channel,
            new { evaluation = evaluation.Decision, evaluation.Reason, awaitingSecondApprover = !isFinal }, now));

        if (!await TrySaveAsync(request.Id, cancellationToken)) return new ApproveAdProposalResponse(ErrorCodes.MarketingAdsConcurrentChange);

        var jobId = isFinal ? _enqueuer.EnqueueExecution(proposal.Id) : null;
        if (isFinal && jobId is null)
        {
            _logger.LogWarning("Ad proposal {ProposalId} approved but enqueueing failed; the expiry job re-enqueues it.", proposal.Id);
        }

        return new ApproveAdProposalResponse
        {
            ProposalId = proposal.Id,
            Version = proposal.CurrentVersion,
            Status = proposal.Status,
            AwaitingSecondApprover = !isFinal,
            ExecutionJobId = jobId,
        };
    }

    private async Task<bool> TrySaveAsync(int proposalId, CancellationToken cancellationToken)
    {
        try
        {
            await _proposals.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Concurrent change while approving ad proposal {ProposalId}", proposalId);
            _proposals.DiscardChanges();
            return false;
        }
    }
}
```

- [ ] **Step 4: Implement Reject**

`RejectAdProposalRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.RejectAdProposal;

public class RejectAdProposalRequest : IRequest<RejectAdProposalResponse>
{
    public const int MaxReasonLength = 2000;

    /// <summary>Set from the route by the controller.</summary>
    public int Id { get; set; }
    public int Version { get; set; }
    public string Reason { get; set; } = string.Empty;
}
```

`RejectAdProposalResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.RejectAdProposal;

public class RejectAdProposalResponse : BaseResponse
{
    public int ProposalId { get; set; }
    public int Version { get; set; }
    public AdProposalStatus Status { get; set; }

    public RejectAdProposalResponse() { }
    public RejectAdProposalResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

`RejectAdProposalHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Audit;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.RejectAdProposal;

public class RejectAdProposalHandler : IRequestHandler<RejectAdProposalRequest, RejectAdProposalResponse>
{
    private readonly IAdProposalRepository _proposals;
    private readonly IAdAuditLog _audit;
    private readonly IApprovalChannelResolver _channelResolver;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;
    private readonly ILogger<RejectAdProposalHandler> _logger;

    public RejectAdProposalHandler(IAdProposalRepository proposals, IAdAuditLog audit, IApprovalChannelResolver channelResolver,
        ICurrentUserService currentUser, TimeProvider time, ILogger<RejectAdProposalHandler> logger)
    {
        _proposals = proposals;
        _audit = audit;
        _channelResolver = channelResolver;
        _currentUser = currentUser;
        _time = time;
        _logger = logger;
    }

    public async Task<RejectAdProposalResponse> Handle(RejectAdProposalRequest request, CancellationToken cancellationToken)
    {
        var principal = _currentUser.GetCurrentUser().Id;
        if (string.IsNullOrWhiteSpace(principal)) return new RejectAdProposalResponse(ErrorCodes.Unauthorized);

        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0 || reason.Length > RejectAdProposalRequest.MaxReasonLength)
        {
            return new RejectAdProposalResponse(ErrorCodes.MarketingAdsInvalidProposal,
                AdProposalErrors.Params(new[] { $"Reason is required (max {RejectAdProposalRequest.MaxReasonLength} chars)." }));
        }

        var resolution = _channelResolver.Resolve();
        if (!resolution.Allowed) return new RejectAdProposalResponse(ErrorCodes.MarketingAdsApprovalChannelDenied, AdProposalErrors.Reason(resolution.DenyReason));

        var now = _time.GetUtcNow().UtcDateTime;
        var proposal = await _proposals.GetForUpdateAsync(request.Id, cancellationToken);
        if (AdProposalGuards.CheckPendingVersion(proposal, request.Version, now) is { } guard) return new RejectAdProposalResponse(guard);

        proposal!.Reject(principal, resolution.Channel, reason, now);
        _audit.Append(AdAuditEvents.ForProposal(proposal, AdAuditEventType.Rejected, principal, AdActorKind.Human, resolution.Channel,
            new { reason }, now));

        try
        {
            await _proposals.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Concurrent change while rejecting ad proposal {ProposalId}", request.Id);
            _proposals.DiscardChanges();
            return new RejectAdProposalResponse(ErrorCodes.MarketingAdsConcurrentChange);
        }

        return new RejectAdProposalResponse { ProposalId = proposal.Id, Version = proposal.CurrentVersion, Status = proposal.Status };
    }
}
```

- [ ] **Step 5: Run the tests**

```bash
MSBUILDDISABLENODEREUSE=1 dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~ApproveAdProposalHandlerTests|FullyQualifiedName~RejectAdProposalHandlerTests"
```

Expected: PASS (8 + 5).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingAds/Proposals/UseCases backend/test/Anela.Heblo.Tests/Features/MarketingAds/Proposals/UseCases
git commit -m "feat: version-bound approve and reject of ad proposals behind the approval channel resolver

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 11: Execution job — sequential actions, kill switch before every action, stale-state check

**Files:**
- Replace: `backend/src/Anela.Heblo.Application/Features/MarketingAds/Proposals/Execution/AdProposalExecutionJob.cs` (the Task 8 stub)
- Modify: `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — add `<ProjectReference Include="..\Anela.Heblo.MarketingAds.TestKit\Anela.Heblo.MarketingAds.TestKit.csproj" />` if missing
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/Proposals/Execution/AdProposalExecutionJobTests.cs`

**Interfaces:**
- Consumes: `IEnumerable<IAdActionExecutor>` (registered by the platform PRs; empty is valid), `IAdGovernanceRepository.GetKillSwitchesAsync`, `AdKillSwitchPolicy`, Task 2 aggregate methods.
- Produces: `AdProposalExecutionJob.RunAsync(int proposalId, CancellationToken cancellationToken)`; error texts `AdProposalExecutionJob.KillSwitchOff`, `.NoExecutor`, `.SkippedAfterStop`, `.Cancelled`.

Behaviour per spec 6.2/6.6: only an `Approved` proposal runs; it is *claimed* by moving to `Executing` in its own SaveChanges (a concurrent second delivery loses on the `Revision` token and exits). Per action, in order: re-read the kill switch (no tracking) → executor for the platform that supports the type → `ReadCurrentAsync` → `CurrentValue == OldValue` (ordinal) else `StaleState` and stop → `ExecuteAsync` → store before-state, response and outcome + audit `ActionExecuted`/`ActionFailed` → SaveChanges. A platform `Failed` result continues with the next action; stale state, kill switch, missing executor and transport/auth exceptions stop the run (remaining actions recorded `Skipped`). An exception while the job token is **not** cancelled (e.g. `TaskCanceledException` from an HTTP timeout) is a failure; real cancellation marks the rest `Skipped`, finalises the status, then rethrows. Final status: all succeeded → `Executed`, some → `PartiallyExecuted`, none → `Failed`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Execution;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.MarketingAds.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Execution;

public class AdProposalExecutionJobTests
{
    private static AdProposalExecutionJob Job(ProposalTestHarness h, params IAdActionExecutor[] executors) => new(
        h.Proposals, h.Governance, h.Audit, executors, h.Time, NullLogger<AdProposalExecutionJob>.Instance);

    private static Mock<IAdActionExecutor> Executor(AdPlatform platform = AdPlatform.GoogleAds)
    {
        var mock = new Mock<IAdActionExecutor>();
        mock.SetupGet(e => e.Platform).Returns(platform);
        mock.SetupGet(e => e.SupportedActions).Returns(new HashSet<AdActionType> { AdActionType.PauseAd, AdActionType.AddNegativeKeyword });
        mock.Setup(e => e.ReadCurrentAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdTargetState(true, AdActionValues.Enabled, """{"status":"ENABLED"}"""));
        mock.Setup(e => e.ExecuteAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AdAction a, CancellationToken _) => new AdExecutionResult(AdExecutionOutcome.Succeeded,
                AdActionValues.Enabled, AdActionValues.Paused, $"res-{a.TargetExternalId}", """{"ok":true}""", null));
        return mock;
    }

    private static async Task<(AdProposal Proposal, List<AdProposalActionExecution> Rows, List<AdAuditEvent> Audit)> LoadAsync(ProposalTestHarness h, int id)
    {
        await using var verify = h.NewContext();
        var proposal = await verify.AdProposals.Include(p => p.Executions).SingleAsync(p => p.Id == id);
        var audit = await verify.AdAuditEvents.Where(e => e.ProposalId == id).OrderBy(e => e.Id).ToListAsync();
        return (proposal, proposal.Executions.OrderBy(e => e.ActionIndex).ToList(), audit);
    }

    [Fact]
    public async Task RunAsync_AllActionsSucceed_StoresBeforeStateResponseAndExecutes()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        var id = await h.SeedApprovedAsync(actionCount: 2);
        var executor = Executor();

        await Job(h, executor.Object).RunAsync(id, CancellationToken.None);

        var (proposal, rows, audit) = await LoadAsync(h, id);
        proposal.Status.Should().Be(AdProposalStatus.Executed);
        rows.Should().HaveCount(2).And.OnlyContain(r => r.State == AdActionExecutionState.Succeeded
            && r.BeforeValue == AdActionValues.Enabled && r.BeforeRawJson == """{"status":"ENABLED"}"""
            && r.AfterValue == AdActionValues.Paused && r.PlatformResponseJson == """{"ok":true}""");
        rows[0].PlatformResourceId.Should().Be("res-ad-1");
        audit.Select(e => e.EventType).Should().Equal(AdAuditEventType.ExecutionStarted, AdAuditEventType.ActionExecuted, AdAuditEventType.ActionExecuted);
        audit.Should().OnlyContain(e => e.Channel == ApprovalChannel.System && e.ActorPrincipal == "system:executor");
    }

    [Fact]
    public async Task RunAsync_DefaultKillSwitchOff_SkipsEverythingAndNeverCallsThePlatform()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedApprovedAsync(actionCount: 2);
        var executor = Executor();

        await Job(h, executor.Object).RunAsync(id, CancellationToken.None);

        var (proposal, rows, _) = await LoadAsync(h, id);
        proposal.Status.Should().Be(AdProposalStatus.Failed);
        rows.Should().OnlyContain(r => r.State == AdActionExecutionState.Skipped);
        rows[0].Error.Should().Be(AdProposalExecutionJob.KillSwitchOff);
        executor.Verify(e => e.ReadCurrentAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_PlatformOverrideOff_BlocksThatPlatform()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        await h.SetKillSwitchAsync("GoogleAds", false);
        var id = await h.SeedApprovedAsync();
        var executor = Executor();

        await Job(h, executor.Object).RunAsync(id, CancellationToken.None);

        (await LoadAsync(h, id)).Proposal.Status.Should().Be(AdProposalStatus.Failed);
        executor.Verify(e => e.ExecuteAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_KillSwitchTurnedOffMidRun_SkipsRemainingActions()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        var id = await h.SeedApprovedAsync(actionCount: 3);
        var executor = Executor();
        executor.Setup(e => e.ExecuteAsync(It.Is<AdAction>(a => a.TargetExternalId == "ad-1"), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, false); // an admin flips the switch
                return new AdExecutionResult(AdExecutionOutcome.Succeeded, AdActionValues.Enabled, AdActionValues.Paused, "res-ad-1", "{}", null);
            });

        await Job(h, executor.Object).RunAsync(id, CancellationToken.None);

        var (proposal, rows, _) = await LoadAsync(h, id);
        proposal.Status.Should().Be(AdProposalStatus.PartiallyExecuted);
        rows.Select(r => r.State).Should().Equal(AdActionExecutionState.Succeeded, AdActionExecutionState.Skipped, AdActionExecutionState.Skipped);
        executor.Verify(e => e.ExecuteAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_CurrentValueDiffers_IsStaleAndStops()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        var id = await h.SeedApprovedAsync(actionCount: 2);
        var executor = Executor();
        executor.Setup(e => e.ReadCurrentAsync(It.Is<AdAction>(a => a.TargetExternalId == "ad-1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdTargetState(true, AdActionValues.Paused, "{}"));

        await Job(h, executor.Object).RunAsync(id, CancellationToken.None);

        var (proposal, rows, audit) = await LoadAsync(h, id);
        proposal.Status.Should().Be(AdProposalStatus.Failed);
        rows[0].State.Should().Be(AdActionExecutionState.StaleState);
        rows[0].BeforeValue.Should().Be(AdActionValues.Paused);
        rows[1].State.Should().Be(AdActionExecutionState.Skipped);
        audit.Should().Contain(e => e.EventType == AdAuditEventType.ActionFailed);
        executor.Verify(e => e.ExecuteAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_PlatformRejectsOneAction_ContinuesWithTheNext()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        var id = await h.SeedApprovedAsync(actionCount: 2);
        var executor = Executor();
        executor.Setup(e => e.ExecuteAsync(It.Is<AdAction>(a => a.TargetExternalId == "ad-1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdExecutionResult(AdExecutionOutcome.Failed, AdActionValues.Enabled, null, null, """{"error":"POLICY"}""", "POLICY"));

        await Job(h, executor.Object).RunAsync(id, CancellationToken.None);

        var (proposal, rows, _) = await LoadAsync(h, id);
        proposal.Status.Should().Be(AdProposalStatus.PartiallyExecuted);
        rows.Select(r => r.State).Should().Equal(AdActionExecutionState.Failed, AdActionExecutionState.Succeeded);
        rows[0].Error.Should().Be("POLICY");
    }

    [Fact]
    public async Task RunAsync_TransportException_FailsAndSkipsTheRest()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        var id = await h.SeedApprovedAsync(actionCount: 2);
        var executor = Executor();
        executor.Setup(e => e.ExecuteAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("401 Unauthorized"));

        await Job(h, executor.Object).RunAsync(id, CancellationToken.None);

        var (proposal, rows, _) = await LoadAsync(h, id);
        proposal.Status.Should().Be(AdProposalStatus.Failed);
        rows[0].State.Should().Be(AdActionExecutionState.Failed);
        rows[0].Error.Should().Contain("401 Unauthorized");
        rows[1].State.Should().Be(AdActionExecutionState.Skipped);
    }

    [Fact]
    public async Task RunAsync_HttpTimeout_IsRecordedAsFailureNotCancellation()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        var id = await h.SeedApprovedAsync();
        var executor = Executor();
        executor.Setup(e => e.ReadCurrentAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TaskCanceledException("HttpClient.Timeout"));

        var act = () => Job(h, executor.Object).RunAsync(id, CancellationToken.None);

        await act.Should().NotThrowAsync();
        var (proposal, rows, _) = await LoadAsync(h, id);
        proposal.Status.Should().Be(AdProposalStatus.Failed);
        rows.Single().State.Should().Be(AdActionExecutionState.Failed);
    }

    [Fact]
    public async Task RunAsync_JobCancelled_FinalisesStatusThenRethrows()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        var id = await h.SeedApprovedAsync(actionCount: 2);
        using var cts = new CancellationTokenSource();
        var executor = Executor();
        executor.Setup(e => e.ReadCurrentAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()))
            .Returns(() => { cts.Cancel(); throw new OperationCanceledException(cts.Token); });

        var act = () => Job(h, executor.Object).RunAsync(id, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        var (proposal, rows, _) = await LoadAsync(h, id);
        proposal.Status.Should().Be(AdProposalStatus.Failed);
        rows.Should().HaveCount(2).And.OnlyContain(r => r.State == AdActionExecutionState.Skipped && r.Error == AdProposalExecutionJob.Cancelled);
    }

    [Fact]
    public async Task RunAsync_NoExecutorForPlatform_Fails()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        var id = await h.SeedApprovedAsync(platform: AdPlatform.Sklik);

        await Job(h, Executor(AdPlatform.GoogleAds).Object).RunAsync(id, CancellationToken.None);

        var (proposal, rows, _) = await LoadAsync(h, id);
        proposal.Status.Should().Be(AdProposalStatus.Failed);
        rows.Single().Error.Should().Be(AdProposalExecutionJob.NoExecutor);
    }

    [Fact]
    public async Task RunAsync_RunTwice_ExecutesOnce()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        var id = await h.SeedApprovedAsync();
        var executor = Executor();

        await Job(h, executor.Object).RunAsync(id, CancellationToken.None);
        h.Context.ChangeTracker.Clear();
        await Job(h, executor.Object).RunAsync(id, CancellationToken.None);

        executor.Verify(e => e.ExecuteAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_PendingProposal_IsIgnored()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        var id = await h.SeedPendingAsync();
        var executor = Executor();

        await Job(h, executor.Object).RunAsync(id, CancellationToken.None);

        (await LoadAsync(h, id)).Proposal.Status.Should().Be(AdProposalStatus.Pending);
        executor.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_WithTheTestKitFakeExecutor_PausesTheAd()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        var id = await h.SeedApprovedAsync();
        // C1's in-memory executor: ctor (platform, params types), SeedAd(account, adId, status), ExecutedActions.
        var fake = new FakeAdActionExecutor(AdPlatform.GoogleAds).SeedAd("acc-1", "ad-1", AdActionValues.Enabled);

        await Job(h, fake).RunAsync(id, CancellationToken.None);

        (await LoadAsync(h, id)).Proposal.Status.Should().Be(AdProposalStatus.Executed);
        fake.ExecutedActions.Should().ContainSingle(a => a.TargetExternalId == "ad-1" && a.NewValue == AdActionValues.Paused);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

```bash
MSBUILDDISABLENODEREUSE=1 dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~AdProposalExecutionJobTests"
```

Expected: FAIL — compile error `'AdProposalExecutionJob' does not contain a constructor that takes 6 arguments`.

- [ ] **Step 3: Implement the job** (replace the stub):

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Audit;
using Anela.Heblo.Domain.Features.MarketingAds;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Execution;

/// <summary>Executes one Approved proposal (spec 6.2, 6.6). Enqueued after the final approval.</summary>
public class AdProposalExecutionJob
{
    public const string KillSwitchOff = "Execution is disabled by the kill switch.";
    public const string NoExecutor = "No executor is registered for this platform and action type.";
    public const string SkippedAfterStop = "Skipped: an earlier action stopped the execution.";
    public const string Cancelled = "Cancelled: the job stopped before this action ran.";
    private const int LockTimeoutSeconds = 600;
    private const int MaxAuditTextLength = 4000;

    private readonly IAdProposalRepository _proposals;
    private readonly IAdGovernanceRepository _governance;
    private readonly IAdAuditLog _audit;
    private readonly IEnumerable<IAdActionExecutor> _executors;
    private readonly TimeProvider _time;
    private readonly ILogger<AdProposalExecutionJob> _logger;

    public AdProposalExecutionJob(IAdProposalRepository proposals, IAdGovernanceRepository governance, IAdAuditLog audit,
        IEnumerable<IAdActionExecutor> executors, TimeProvider time, ILogger<AdProposalExecutionJob> logger)
    {
        _proposals = proposals;
        _governance = governance;
        _audit = audit;
        _executors = executors;
        _time = time;
        _logger = logger;
    }

    [DisableConcurrentExecution(LockTimeoutSeconds)]
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(int proposalId, CancellationToken cancellationToken)
    {
        var proposal = await _proposals.GetForUpdateAsync(proposalId, cancellationToken);
        if (proposal is null || proposal.Status != AdProposalStatus.Approved)
        {
            _logger.LogInformation("Ad proposal {ProposalId} is {Status}; nothing to execute.", proposalId, proposal?.Status);
            return;
        }
        if (!await TryClaimAsync(proposal, cancellationToken))
        {
            return;
        }

        var actions = AdActionJson.Deserialize(proposal.GetCurrentVersion().ActionsJson);
        var executor = _executors.FirstOrDefault(e => e.Platform.ToString() == proposal.Platform);
        await RunActionsAsync(proposal, actions, executor, cancellationToken);

        proposal.FinishExecution(Now());
        await _proposals.SaveChangesAsync(CancellationToken.None);
        _logger.LogInformation("Ad proposal {ProposalId} finished as {Status}.", proposalId, proposal.Status);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task<bool> TryClaimAsync(AdProposal proposal, CancellationToken cancellationToken)
    {
        var now = Now();
        proposal.StartExecution(now);
        _audit.Append(AdAuditEvents.ForProposal(proposal, AdAuditEventType.ExecutionStarted, AdSystemPrincipals.Executor,
            AdActorKind.System, ApprovalChannel.System, new { actionCount = proposal.GetCurrentVersion().ActionCount }, now));
        try
        {
            await _proposals.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogInformation("Ad proposal {ProposalId} was claimed by another execution run.", proposal.Id);
            _proposals.DiscardChanges();
            return false;
        }
    }

    private async Task RunActionsAsync(AdProposal proposal, IReadOnlyList<AdAction> actions, IAdActionExecutor? executor, CancellationToken cancellationToken)
    {
        string? stopReason = null;
        for (var index = 0; index < actions.Count; index++)
        {
            if (stopReason is not null)
            {
                Record(proposal, index, actions[index], AdActionExecutionState.Skipped, stopReason);
            }
            else
            {
                try
                {
                    if (await ExecuteActionAsync(proposal, index, actions[index], executor, cancellationToken))
                    {
                        stopReason = SkippedAfterStop;
                    }
                }
                catch (Exception) when (cancellationToken.IsCancellationRequested)
                {
                    Record(proposal, index, actions[index], AdActionExecutionState.Skipped, Cancelled);
                    stopReason = Cancelled;
                }
            }
            await _proposals.SaveChangesAsync(CancellationToken.None);
        }
    }

    /// <returns>true when the remaining actions must not run.</returns>
    private async Task<bool> ExecuteActionAsync(AdProposal proposal, int index, AdAction action, IAdActionExecutor? executor, CancellationToken cancellationToken)
    {
        if (!AdKillSwitchPolicy.IsExecutionEnabled(await _governance.GetKillSwitchesAsync(cancellationToken), proposal.Platform))
        {
            Record(proposal, index, action, AdActionExecutionState.Skipped, KillSwitchOff);
            return true;
        }
        if (executor is null || !executor.SupportedActions.Contains(action.Type))
        {
            Record(proposal, index, action, AdActionExecutionState.Failed, NoExecutor);
            return true;
        }

        AdTargetState before;
        try
        {
            before = await executor.ReadCurrentAsync(action, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "ReadCurrent failed for ad proposal {ProposalId} action {Index}", proposal.Id, index);
            Record(proposal, index, action, AdActionExecutionState.Failed, $"Reading the current state failed: {ex.Message}");
            return true;
        }

        if (!string.Equals(before.CurrentValue, action.OldValue, StringComparison.Ordinal))
        {
            Record(proposal, index, action, AdActionExecutionState.StaleState,
                $"Current value '{before.CurrentValue}' differs from the proposal's old value '{action.OldValue}'.", before);
            return true;
        }

        AdExecutionResult result;
        try
        {
            result = await executor.ExecuteAsync(action, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Execute failed for ad proposal {ProposalId} action {Index}", proposal.Id, index);
            Record(proposal, index, action, AdActionExecutionState.Failed, $"Execution failed: {ex.Message}", before);
            return true;
        }

        var state = result.Outcome switch
        {
            AdExecutionOutcome.Succeeded => AdActionExecutionState.Succeeded,
            AdExecutionOutcome.StaleState => AdActionExecutionState.StaleState,
            _ => AdActionExecutionState.Failed,
        };
        Record(proposal, index, action, state, result.Error, before, result);
        return state == AdActionExecutionState.StaleState;
    }

    private void Record(AdProposal proposal, int index, AdAction action, AdActionExecutionState state, string? error,
        AdTargetState? before = null, AdExecutionResult? result = null)
    {
        var now = Now();
        proposal.RecordExecution(new AdProposalActionExecution
        {
            ProposalId = proposal.Id,
            Version = proposal.CurrentVersion,
            ActionIndex = index,
            Platform = proposal.Platform,
            AccountExternalId = proposal.AccountExternalId,
            ActionType = proposal.ActionType,
            TargetLevel = action.TargetLevel.ToString(),
            TargetExternalId = action.TargetExternalId,
            PayloadJson = JsonSerializer.Serialize(action.Payload),
            State = state,
            BeforeValue = before?.CurrentValue,
            BeforeRawJson = before?.RawJson,
            AfterValue = result?.AfterValue,
            PlatformResourceId = result?.PlatformResourceId,
            PlatformResponseJson = result?.PlatformResponseJson,
            Error = error,
            ExecutedAt = now,
        });
        var eventType = state == AdActionExecutionState.Succeeded ? AdAuditEventType.ActionExecuted : AdAuditEventType.ActionFailed;
        _audit.Append(AdAuditEvents.ForProposal(proposal, eventType, AdSystemPrincipals.Executor, AdActorKind.System, ApprovalChannel.System,
            new
            {
                actionIndex = index,
                target = action.TargetExternalId,
                state,
                before = before?.CurrentValue,
                after = result?.AfterValue,
                platformResourceId = result?.PlatformResourceId,
                platformResponse = Truncate(result?.PlatformResponseJson),
                error = Truncate(error),
            }, now));
    }

    private static string? Truncate(string? value) =>
        value is null || value.Length <= MaxAuditTextLength ? value : value[..MaxAuditTextLength];

    private DateTime Now() => _time.GetUtcNow().UtcDateTime;
}
```

- [ ] **Step 4: Run the tests**

```bash
MSBUILDDISABLENODEREUSE=1 dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~AdProposalExecutionJobTests"
```

Expected: PASS (13 tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingAds/Proposals/Execution backend/test/Anela.Heblo.Tests
git commit -m "feat: ad proposal execution job with per-action kill switch and stale-state check

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 12: Revert from stored before-state (web only, admin)

**Files:**
- Create: `.../Proposals/UseCases/RevertAdProposal/{RevertAdProposalRequest,RevertAdProposalResponse,RevertAdProposalHandler}.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/Proposals/UseCases/RevertAdProposalHandlerTests.cs`

**Interfaces:**
- Produces: `RevertAdProposalRequest { int Id }` → `RevertAdProposalResponse { int ProposalId; AdProposalStatus Status; int RevertedActions; int FailedActions }`

Rules: channel must be allowed **and** `Web` (403 otherwise); status `Executed` or `PartiallyExecuted` (409 `MarketingAdsNotRevertible`); an executor for the platform must exist (409 `MarketingAdsNoExecutor`). The proposal is claimed with a `Touch` + SaveChanges (a concurrent revert gets 409). Succeeded, not-yet-reverted actions are reverted in **reverse order** via `IAdActionExecutor.RevertAsync(action, original)` where `original` is rebuilt from the stored row; each result is saved and audited (`Reverted` or `ActionFailed` with `phase = "revert"`). When every succeeded action is reverted the proposal becomes `Reverted`; otherwise it keeps its status and the handler returns 502 `MarketingAdsRevertFailed` — a retry reverts only what is left. Revert is a human restoring the account, so it is **not** gated by the kill switch (see Spec deviations). It runs synchronously (≤ 50 platform calls).

- [ ] **Step 1: Write the failing tests**

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Execution;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.RevertAdProposal;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.UseCases;

public class RevertAdProposalHandlerTests
{
    private readonly Mock<IAdActionExecutor> _executor = new();

    public RevertAdProposalHandlerTests()
    {
        _executor.SetupGet(e => e.Platform).Returns(AdPlatform.GoogleAds);
        _executor.SetupGet(e => e.SupportedActions).Returns(new HashSet<AdActionType> { AdActionType.PauseAd });
        _executor.Setup(e => e.ReadCurrentAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdTargetState(true, AdActionValues.Enabled, "{}"));
        _executor.Setup(e => e.ExecuteAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AdAction a, CancellationToken _) => new AdExecutionResult(AdExecutionOutcome.Succeeded, AdActionValues.Enabled, AdActionValues.Paused, $"res-{a.TargetExternalId}", "{}", null));
        _executor.Setup(e => e.RevertAsync(It.IsAny<AdAction>(), It.IsAny<AdExecutionResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdExecutionResult(AdExecutionOutcome.Succeeded, AdActionValues.Paused, AdActionValues.Enabled, null, """{"reverted":true}""", null));
    }

    private RevertAdProposalHandler Handler(ProposalTestHarness h) => new(
        h.Proposals, h.Audit, new[] { _executor.Object }, h.ChannelResolver.Object, h.CurrentUser.Object, h.Time,
        NullLogger<RevertAdProposalHandler>.Instance);

    private async Task<int> SeedExecutedAsync(ProposalTestHarness h, int actionCount)
    {
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        var id = await h.SeedApprovedAsync(actionCount);
        await new AdProposalExecutionJob(h.Proposals, h.Governance, h.Audit, new[] { _executor.Object }, h.Time,
            NullLogger<AdProposalExecutionJob>.Instance).RunAsync(id, CancellationToken.None);
        h.Context.ChangeTracker.Clear();
        return id;
    }

    [Fact]
    public async Task Revert_UsesStoredResultsInReverseOrder_AndMarksReverted()
    {
        using var h = new ProposalTestHarness();
        var id = await SeedExecutedAsync(h, 2);
        var order = new List<string>();
        _executor.Setup(e => e.RevertAsync(It.IsAny<AdAction>(), It.IsAny<AdExecutionResult>(), It.IsAny<CancellationToken>()))
            .Callback((AdAction a, AdExecutionResult original, CancellationToken _) =>
            {
                order.Add(a.TargetExternalId);
                original.PlatformResourceId.Should().Be($"res-{a.TargetExternalId}");
                original.BeforeValue.Should().Be(AdActionValues.Enabled);
            })
            .ReturnsAsync(new AdExecutionResult(AdExecutionOutcome.Succeeded, AdActionValues.Paused, AdActionValues.Enabled, null, "{}", null));

        var response = await Handler(h).Handle(new RevertAdProposalRequest { Id = id }, CancellationToken.None);

        response.Status.Should().Be(AdProposalStatus.Reverted);
        response.RevertedActions.Should().Be(2);
        order.Should().Equal("ad-2", "ad-1");
        await using var verify = h.NewContext();
        var rows = await verify.AdProposalActionExecutions.Where(e => e.ProposalId == id).ToListAsync();
        rows.Should().OnlyContain(r => r.RevertState == AdActionRevertState.Reverted);
        (await verify.AdAuditEvents.CountAsync(e => e.ProposalId == id && e.EventType == AdAuditEventType.Reverted)).Should().Be(2);
    }

    [Fact]
    public async Task PartialRevertFailure_KeepsStatus_AndRetryRevertsOnlyTheRest()
    {
        using var h = new ProposalTestHarness();
        var id = await SeedExecutedAsync(h, 2);
        _executor.Setup(e => e.RevertAsync(It.Is<AdAction>(a => a.TargetExternalId == "ad-1"), It.IsAny<AdExecutionResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdExecutionResult(AdExecutionOutcome.Failed, null, null, null, "{}", "RATE_LIMIT"));

        var first = await Handler(h).Handle(new RevertAdProposalRequest { Id = id }, CancellationToken.None);
        h.Context.ChangeTracker.Clear();
        _executor.Setup(e => e.RevertAsync(It.Is<AdAction>(a => a.TargetExternalId == "ad-1"), It.IsAny<AdExecutionResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdExecutionResult(AdExecutionOutcome.Succeeded, AdActionValues.Paused, AdActionValues.Enabled, null, "{}", null));
        var second = await Handler(h).Handle(new RevertAdProposalRequest { Id = id }, CancellationToken.None);

        first.ErrorCode.Should().Be(ErrorCodes.MarketingAdsRevertFailed);
        second.Status.Should().Be(AdProposalStatus.Reverted);
        _executor.Verify(e => e.RevertAsync(It.Is<AdAction>(a => a.TargetExternalId == "ad-2"), It.IsAny<AdExecutionResult>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PendingProposal_IsNotRevertible()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync();

        var response = await Handler(h).Handle(new RevertAdProposalRequest { Id = id }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsNotRevertible);
    }

    [Theory]
    [InlineData(ApprovalChannel.Mcp)]
    [InlineData(ApprovalChannel.System)]
    public async Task NonWebChannel_CannotRevert(ApprovalChannel channel)
    {
        using var h = new ProposalTestHarness();
        var id = await SeedExecutedAsync(h, 1);
        h.UseChannel(channel);

        var response = await Handler(h).Handle(new RevertAdProposalRequest { Id = id }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsApprovalChannelDenied);
    }

    [Fact]
    public async Task DenyAllResolver_CannotRevert()
    {
        using var h = new ProposalTestHarness();
        var id = await SeedExecutedAsync(h, 1);
        h.DenyChannel();

        var response = await Handler(h).Handle(new RevertAdProposalRequest { Id = id }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsApprovalChannelDenied);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `MSBUILDDISABLENODEREUSE=1 dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false`
Expected: FAIL — `The type or namespace name 'RevertAdProposal' does not exist`.

- [ ] **Step 3: Implement**

`RevertAdProposalRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.RevertAdProposal;

public class RevertAdProposalRequest : IRequest<RevertAdProposalResponse>
{
    /// <summary>Set from the route by the controller.</summary>
    public int Id { get; set; }
}
```

`RevertAdProposalResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.RevertAdProposal;

public class RevertAdProposalResponse : BaseResponse
{
    public int ProposalId { get; set; }
    public AdProposalStatus Status { get; set; }
    public int RevertedActions { get; set; }
    public int FailedActions { get; set; }

    public RevertAdProposalResponse() { }
    public RevertAdProposalResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

`RevertAdProposalHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Audit;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.RevertAdProposal;

public class RevertAdProposalHandler : IRequestHandler<RevertAdProposalRequest, RevertAdProposalResponse>
{
    private readonly IAdProposalRepository _proposals;
    private readonly IAdAuditLog _audit;
    private readonly IEnumerable<IAdActionExecutor> _executors;
    private readonly IApprovalChannelResolver _channelResolver;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;
    private readonly ILogger<RevertAdProposalHandler> _logger;

    public RevertAdProposalHandler(IAdProposalRepository proposals, IAdAuditLog audit, IEnumerable<IAdActionExecutor> executors,
        IApprovalChannelResolver channelResolver, ICurrentUserService currentUser, TimeProvider time, ILogger<RevertAdProposalHandler> logger)
    {
        _proposals = proposals;
        _audit = audit;
        _executors = executors;
        _channelResolver = channelResolver;
        _currentUser = currentUser;
        _time = time;
        _logger = logger;
    }

    public async Task<RevertAdProposalResponse> Handle(RevertAdProposalRequest request, CancellationToken cancellationToken)
    {
        var principal = _currentUser.GetCurrentUser().Id;
        if (string.IsNullOrWhiteSpace(principal)) return new RevertAdProposalResponse(ErrorCodes.Unauthorized);

        var resolution = _channelResolver.Resolve();
        if (!resolution.Allowed || resolution.Channel != ApprovalChannel.Web)
        {
            return new RevertAdProposalResponse(ErrorCodes.MarketingAdsApprovalChannelDenied, AdProposalErrors.Reason(resolution.DenyReason ?? "Revert is available in the web UI only."));
        }

        var proposal = await _proposals.GetForUpdateAsync(request.Id, cancellationToken);
        if (proposal is null) return new RevertAdProposalResponse(ErrorCodes.MarketingAdsProposalNotFound);
        if (proposal.Status is not (AdProposalStatus.Executed or AdProposalStatus.PartiallyExecuted)) return new RevertAdProposalResponse(ErrorCodes.MarketingAdsNotRevertible);
        var executor = _executors.FirstOrDefault(e => e.Platform.ToString() == proposal.Platform);
        if (executor is null) return new RevertAdProposalResponse(ErrorCodes.MarketingAdsNoExecutor);

        proposal.Touch(Now()); // claims the proposal: a concurrent revert fails on the Revision token
        if (!await TrySaveAsync(cancellationToken)) return new RevertAdProposalResponse(ErrorCodes.MarketingAdsConcurrentChange);

        var actions = AdActionJson.Deserialize(proposal.GetCurrentVersion().ActionsJson);
        var pending = proposal.Executions
            .Where(e => e.Version == proposal.CurrentVersion && e.State == AdActionExecutionState.Succeeded && e.RevertState != AdActionRevertState.Reverted)
            .OrderByDescending(e => e.ActionIndex)
            .ToList();
        var (reverted, failed) = await RevertAllAsync(proposal, pending, actions, executor, principal, cancellationToken);

        if (failed > 0)
        {
            return new RevertAdProposalResponse(ErrorCodes.MarketingAdsRevertFailed,
                new Dictionary<string, string> { ["Reverted"] = reverted.ToString(), ["Failed"] = failed.ToString() });
        }

        proposal.MarkReverted(Now());
        await _proposals.SaveChangesAsync(CancellationToken.None);
        return new RevertAdProposalResponse { ProposalId = proposal.Id, Status = proposal.Status, RevertedActions = reverted };
    }

    private async Task<(int Reverted, int Failed)> RevertAllAsync(AdProposal proposal, IReadOnlyList<AdProposalActionExecution> rows,
        IReadOnlyList<AdAction> actions, IAdActionExecutor executor, string principal, CancellationToken cancellationToken)
    {
        var reverted = 0;
        var failed = 0;
        foreach (var row in rows)
        {
            var action = actions[row.ActionIndex];
            var original = new AdExecutionResult(AdExecutionOutcome.Succeeded, row.BeforeValue, row.AfterValue,
                row.PlatformResourceId, row.PlatformResponseJson, null);
            var (ok, detail) = await RevertOneAsync(executor, action, original, cancellationToken);
            var now = Now();
            if (ok)
            {
                row.MarkReverted(now, detail);
                reverted++;
            }
            else
            {
                row.MarkRevertFailed(now, detail ?? "Revert failed.");
                failed++;
            }
            _audit.Append(AdAuditEvents.ForProposal(proposal, ok ? AdAuditEventType.Reverted : AdAuditEventType.ActionFailed,
                principal, AdActorKind.Human, ApprovalChannel.Web,
                new { phase = "revert", actionIndex = row.ActionIndex, target = row.TargetExternalId, detail }, now));
            await _proposals.SaveChangesAsync(CancellationToken.None);
        }
        return (reverted, failed);
    }

    private async Task<(bool Ok, string? Detail)> RevertOneAsync(IAdActionExecutor executor, AdAction action, AdExecutionResult original, CancellationToken cancellationToken)
    {
        try
        {
            var result = await executor.RevertAsync(action, original, cancellationToken);
            return result.Outcome == AdExecutionOutcome.Succeeded
                ? (true, result.PlatformResponseJson)
                : (false, result.Error ?? result.Outcome.ToString());
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Revert failed for target {Target}", action.TargetExternalId);
            return (false, ex.Message);
        }
    }

    private async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _proposals.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            _proposals.DiscardChanges();
            return false;
        }
    }

    private DateTime Now() => _time.GetUtcNow().UtcDateTime;
}
```

- [ ] **Step 4: Run the tests**

```bash
MSBUILDDISABLENODEREUSE=1 dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~RevertAdProposalHandlerTests"
```

Expected: PASS (5 + theory cases).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingAds/Proposals/UseCases/RevertAdProposal backend/test/Anela.Heblo.Tests/Features/MarketingAds/Proposals/UseCases/RevertAdProposalHandlerTests.cs
git commit -m "feat: revert executed ad proposals from stored before-state

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 13: Expiry command and recurring job

**Files:**
- Create: `.../Proposals/UseCases/ExpireAdProposals/{ExpireAdProposalsRequest,ExpireAdProposalsResponse,ExpireAdProposalsHandler}.cs`
- Create: `.../Proposals/Jobs/AdProposalExpiryJob.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/Proposals/UseCases/ExpireAdProposalsHandlerTests.cs`, `.../Jobs/AdProposalExpiryJobTests.cs`

**Interfaces:**
- Produces: `ExpireAdProposalsRequest` → `ExpireAdProposalsResponse { int ExpiredCount; int RequeuedCount }`; recurring job `AdProposalExpiryJob` with `Name = "marketing-ads-proposal-expiry"`, cron `Ads:ExpiryCronExpression` (default every 15 min, Europe/Prague), category `Marketing`.

Each expirable proposal is saved separately; a concurrency failure on one (someone approved it at the same moment) is logged, the context is cleared (`DiscardChanges`, so the failed entity does not poison the next save) and the loop continues. Approved proposals untouched for `Ads:StuckApprovedRequeueMinutes` are re-enqueued — safe because the execution job claims a proposal before running it.

- [ ] **Step 1: Write the failing tests**

`ExpireAdProposalsHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ExpireAdProposals;
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.UseCases;

public class ExpireAdProposalsHandlerTests
{
    private static ExpireAdProposalsHandler Handler(ProposalTestHarness h) => new(
        h.Proposals, h.Audit, h.Enqueuer.Object, h.Time, h.Options, NullLogger<ExpireAdProposalsHandler>.Instance);

    [Fact]
    public async Task PendingPastTtl_IsExpiredAndAuditedBySystem()
    {
        using var h = new ProposalTestHarness();
        var old = await h.SeedPendingAsync();
        h.Time.Advance(TimeSpan.FromHours(48));
        var fresh = await h.SeedPendingAsync();
        h.Time.Advance(TimeSpan.FromHours(25));

        var response = await Handler(h).Handle(new ExpireAdProposalsRequest(), CancellationToken.None);

        response.ExpiredCount.Should().Be(1);
        await using var verify = h.NewContext();
        (await verify.AdProposals.SingleAsync(p => p.Id == old)).Status.Should().Be(AdProposalStatus.Expired);
        (await verify.AdProposals.SingleAsync(p => p.Id == fresh)).Status.Should().Be(AdProposalStatus.Pending);
        var audit = await verify.AdAuditEvents.SingleAsync(e => e.ProposalId == old);
        audit.EventType.Should().Be(AdAuditEventType.Expired);
        audit.ActorPrincipal.Should().Be("system:expiry");
        audit.Channel.Should().Be(ApprovalChannel.System);
    }

    [Fact]
    public async Task ApprovedNotPickedUp_IsReEnqueued_FreshApprovedIsNot()
    {
        using var h = new ProposalTestHarness();
        var stuck = await h.SeedApprovedAsync();
        h.Time.Advance(TimeSpan.FromMinutes(20));
        var fresh = await h.SeedApprovedAsync();

        var response = await Handler(h).Handle(new ExpireAdProposalsRequest(), CancellationToken.None);

        response.RequeuedCount.Should().Be(1);
        h.Enqueuer.Verify(e => e.EnqueueExecution(stuck), Times.Once);
        h.Enqueuer.Verify(e => e.EnqueueExecution(fresh), Times.Never);
    }
}
```

`Jobs/AdProposalExpiryJobTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Proposals;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Jobs;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ExpireAdProposals;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Jobs;

public class AdProposalExpiryJobTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IRecurringJobStatusChecker> _status = new();

    private AdProposalExpiryJob Job() => new(_mediator.Object, _status.Object,
        Microsoft.Extensions.Options.Options.Create(new AdProposalOptions()), NullLogger<AdProposalExpiryJob>.Instance);

    [Fact]
    public void Metadata_IsAMarketingJobEvery15Minutes()
    {
        var metadata = Job().Metadata;

        metadata.JobName.Should().Be("marketing-ads-proposal-expiry");
        metadata.CronExpression.Should().Be("*/15 * * * *");
        metadata.Category.Should().Be(RecurringJobCategory.Marketing);
        metadata.DefaultIsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Enabled_SendsTheExpireCommand()
    {
        _status.Setup(s => s.IsJobEnabledAsync("marketing-ads-proposal-expiry", It.IsAny<CancellationToken>(), true)).ReturnsAsync(true);
        _mediator.Setup(m => m.Send(It.IsAny<ExpireAdProposalsRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ExpireAdProposalsResponse());

        await Job().ExecuteAsync();

        _mediator.Verify(m => m.Send(It.IsAny<ExpireAdProposalsRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Disabled_DoesNothing()
    {
        _status.Setup(s => s.IsJobEnabledAsync("marketing-ads-proposal-expiry", It.IsAny<CancellationToken>(), true)).ReturnsAsync(false);

        await Job().ExecuteAsync();

        _mediator.VerifyNoOtherCalls();
    }
}
```

Check the exact `IRecurringJobStatusChecker.IsJobEnabledAsync` parameter order in `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/` before running (`MarketingPerformanceRefreshJob` calls it as `IsJobEnabledAsync(Metadata.JobName, cancellationToken, Metadata.DefaultIsEnabled)`); adjust the two `Setup` lines if the signature differs.

- [ ] **Step 2: Run to verify they fail**

Run: `MSBUILDDISABLENODEREUSE=1 dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false`
Expected: FAIL — `The type or namespace name 'ExpireAdProposals' does not exist`.

- [ ] **Step 3: Implement**

`ExpireAdProposalsRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ExpireAdProposals;

public class ExpireAdProposalsRequest : IRequest<ExpireAdProposalsResponse>
{
}
```

`ExpireAdProposalsResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ExpireAdProposals;

public class ExpireAdProposalsResponse : BaseResponse
{
    public int ExpiredCount { get; set; }
    public int RequeuedCount { get; set; }

    public ExpireAdProposalsResponse() { }
    public ExpireAdProposalsResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

`ExpireAdProposalsHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Audit;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Execution;
using Anela.Heblo.Domain.Features.MarketingAds;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ExpireAdProposals;

public class ExpireAdProposalsHandler : IRequestHandler<ExpireAdProposalsRequest, ExpireAdProposalsResponse>
{
    private readonly IAdProposalRepository _proposals;
    private readonly IAdAuditLog _audit;
    private readonly IAdProposalExecutionEnqueuer _enqueuer;
    private readonly TimeProvider _time;
    private readonly IOptions<AdProposalOptions> _options;
    private readonly ILogger<ExpireAdProposalsHandler> _logger;

    public ExpireAdProposalsHandler(IAdProposalRepository proposals, IAdAuditLog audit, IAdProposalExecutionEnqueuer enqueuer,
        TimeProvider time, IOptions<AdProposalOptions> options, ILogger<ExpireAdProposalsHandler> logger)
    {
        _proposals = proposals;
        _audit = audit;
        _enqueuer = enqueuer;
        _time = time;
        _options = options;
        _logger = logger;
    }

    public async Task<ExpireAdProposalsResponse> Handle(ExpireAdProposalsRequest request, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var expired = 0;
        foreach (var id in await _proposals.GetPendingExpiredIdsAsync(now, cancellationToken))
        {
            if (await TryExpireAsync(id, now, cancellationToken)) expired++;
        }

        var requeued = 0;
        var stuckBefore = now.AddMinutes(-_options.Value.StuckApprovedRequeueMinutes);
        foreach (var id in await _proposals.GetStuckApprovedIdsAsync(stuckBefore, cancellationToken))
        {
            if (_enqueuer.EnqueueExecution(id) is not null) requeued++;
        }

        _logger.LogInformation("Ad proposal expiry: {Expired} expired, {Requeued} stuck approvals re-enqueued", expired, requeued);
        return new ExpireAdProposalsResponse { ExpiredCount = expired, RequeuedCount = requeued };
    }

    private async Task<bool> TryExpireAsync(int id, DateTime now, CancellationToken cancellationToken)
    {
        var proposal = await _proposals.GetForUpdateAsync(id, cancellationToken);
        if (proposal is null || !proposal.IsExpiredAt(now)) return false;

        proposal.Expire(now);
        _audit.Append(AdAuditEvents.ForProposal(proposal, AdAuditEventType.Expired, AdSystemPrincipals.Expiry, AdActorKind.System,
            ApprovalChannel.System, new { proposal.ExpiresAt }, now));
        try
        {
            await _proposals.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Ad proposal {ProposalId} changed while expiring; skipped", id);
            return false;
        }
        finally
        {
            _proposals.DiscardChanges();
        }
    }
}
```

`Jobs/AdProposalExpiryJob.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ExpireAdProposals;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Jobs;

/// <summary>Expires Pending ad proposals past their TTL and re-enqueues Approved ones whose execution never started.</summary>
public class AdProposalExpiryJob : IRecurringJob
{
    public const string Name = "marketing-ads-proposal-expiry";
    private const int LockTimeoutSeconds = 300;

    private readonly IMediator _mediator;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<AdProposalExpiryJob> _logger;

    public AdProposalExpiryJob(IMediator mediator, IRecurringJobStatusChecker statusChecker, IOptions<AdProposalOptions> options, ILogger<AdProposalExpiryJob> logger)
    {
        _mediator = mediator;
        _statusChecker = statusChecker;
        _logger = logger;
        Metadata = new RecurringJobMetadata
        {
            JobName = Name,
            Category = RecurringJobCategory.Marketing,
            DisplayName = "Marketing — expirace návrhů reklam",
            Description = "Každých 15 minut označí čekající návrhy změn reklam starší než 72 h jako expirované a znovu zařadí schválené návrhy, jejichž provedení se nespustilo.",
            CronExpression = options.Value.ExpiryCronExpression,
            DefaultIsEnabled = true,
        };
    }

    public RecurringJobMetadata Metadata { get; }

    [DisableConcurrentExecution(LockTimeoutSeconds)]
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken, Metadata.DefaultIsEnabled))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }
        await _mediator.Send(new ExpireAdProposalsRequest(), cancellationToken);
    }
}
```

- [ ] **Step 4: Run the tests**

```bash
MSBUILDDISABLENODEREUSE=1 dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~ExpireAdProposalsHandlerTests|FullyQualifiedName~AdProposalExpiryJobTests"
```

Expected: PASS (2 + 3).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingAds/Proposals backend/test/Anela.Heblo.Tests/Features/MarketingAds/Proposals
git commit -m "feat: recurring expiry of ad proposals and re-enqueue of stuck approvals

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 14: Settings — autonomy, limits, kill switch (read + audited admin updates)

**Files:**
- Create: `.../Proposals/Contracts/AdSettingsDtos.cs`
- Create: `.../Proposals/UseCases/GetAdSettings/{GetAdSettingsRequest,GetAdSettingsResponse,GetAdSettingsHandler}.cs`
- Create: `.../Proposals/UseCases/UpdateAdAutonomySetting/{Request,Response,Handler}.cs` (class names `UpdateAdAutonomySettingRequest` etc.)
- Create: `.../Proposals/UseCases/UpdateAdLimitSetting/{Request,Response,Handler}.cs`
- Create: `.../Proposals/UseCases/SetAdKillSwitch/{Request,Response,Handler}.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/Proposals/UseCases/AdSettingsHandlersTests.cs`

**Interfaces:**
- Produces:
  - DTOs: `AdAutonomySettingDto { AdPlatform Platform; AdActionType ActionType; AdAutonomyMode Mode; DateTime UpdatedAt; string UpdatedBy }`, `AdLimitSettingDto { AdPlatform Platform; AdActionType ActionType; string Key; int Value; DateTime UpdatedAt; string UpdatedBy }`, `AdKillSwitchDto { string Scope; bool ExecutionEnabled; DateTime UpdatedAt; string UpdatedBy }`, `AdApprovalRateDto { AdPlatform Platform; AdActionType ActionType; int Decided; int ApprovedWithoutEdit; decimal? Rate }`
  - `GetAdSettingsRequest` → `GetAdSettingsResponse { List<AdAutonomySettingDto> Autonomy; List<AdLimitSettingDto> Limits; List<AdKillSwitchDto> KillSwitches; bool IsExecutionEnabledGlobally; List<AdApprovalRateDto> ApprovalRates }`
  - `UpdateAdAutonomySettingRequest { AdPlatform Platform; AdActionType ActionType; AdAutonomyMode Mode }` → `UpdateAdAutonomySettingResponse`
  - `UpdateAdLimitSettingRequest { AdPlatform Platform; AdActionType ActionType; string Key; int Value }` → `UpdateAdLimitSettingResponse`
  - `SetAdKillSwitchRequest { AdPlatform? Platform; bool ExecutionEnabled }` (null platform = global) → `SetAdKillSwitchResponse`

Every mutation requires an allowed **Web** channel (403 otherwise — so with C3's deny-all resolver no setting can change and execution stays disabled until C4), validates input (400 `MarketingAdsInvalidSetting`: key ∉ `AdLimitKeys.All`, value < 0, `Enabled` ∉ {0,1}, undefined enum), captures the old value as a **primitive copy before mutating** the tracked row (gotcha: a tracked read aliases the "before" snapshot), writes `SettingChanged` / `KillSwitchChanged` with `{ old, new }` and saves once. Missing rows are created (upsert).

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.GetAdSettings;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.SetAdKillSwitch;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.UpdateAdAutonomySetting;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.UpdateAdLimitSetting;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.UseCases;

public class AdSettingsHandlersTests
{
    private static UpdateAdAutonomySettingHandler Autonomy(ProposalTestHarness h) => new(h.Governance, h.Audit, h.ChannelResolver.Object, h.CurrentUser.Object, h.Time);
    private static UpdateAdLimitSettingHandler Limit(ProposalTestHarness h) => new(h.Governance, h.Audit, h.ChannelResolver.Object, h.CurrentUser.Object, h.Time);
    private static SetAdKillSwitchHandler KillSwitch(ProposalTestHarness h) => new(h.Governance, h.Audit, h.ChannelResolver.Object, h.CurrentUser.Object, h.Time);

    [Fact]
    public async Task Get_ReturnsSeedsAndExecutionDisabled()
    {
        using var h = new ProposalTestHarness();

        var response = await new GetAdSettingsHandler(h.Governance, h.Proposals).Handle(new GetAdSettingsRequest(), CancellationToken.None);

        response.Autonomy.Should().HaveCount(6).And.OnlyContain(a => a.Mode == AdAutonomyMode.ProposeOnly);
        response.Limits.Should().HaveCount(27);
        response.IsExecutionEnabledGlobally.Should().BeFalse();
    }

    [Fact]
    public async Task Get_ComputesApprovalWithoutEditRate()
    {
        using var h = new ProposalTestHarness();
        await h.SeedApprovedAsync();
        var revised = await h.SeedPendingAsync(creator: "agent-1");
        await using (var ctx = h.NewContext())
        {
            var p = await ctx.AdProposals.Include(x => x.Versions).Include(x => x.Approvals).SingleAsync(x => x.Id == revised);
            p.Revise("[]", 1, "v2", "agent-1", h.Now, TimeSpan.FromHours(72));
            p.RecordApproval("chief", ApprovalChannel.Web, h.Now, isFinal: true);
            await ctx.SaveChangesAsync();
        }

        var response = await new GetAdSettingsHandler(h.Governance, h.Proposals).Handle(new GetAdSettingsRequest(), CancellationToken.None);

        var rate = response.ApprovalRates.Single(r => r.Platform == AdPlatform.GoogleAds && r.ActionType == AdActionType.PauseAd);
        rate.Decided.Should().Be(2);
        rate.ApprovedWithoutEdit.Should().Be(1);
        rate.Rate.Should().Be(0.5m);
    }

    [Fact]
    public async Task UpdateAutonomy_PersistsAndAuditsOldAndNew()
    {
        using var h = new ProposalTestHarness();

        var response = await Autonomy(h).Handle(new UpdateAdAutonomySettingRequest
        {
            Platform = AdPlatform.Sklik, ActionType = AdActionType.AddNegativeKeyword, Mode = AdAutonomyMode.AutoWithinLimits,
        }, CancellationToken.None);

        response.Success.Should().BeTrue();
        await using var verify = h.NewContext();
        var row = await verify.AdAutonomySettings.SingleAsync(s => s.Platform == "Sklik" && s.ActionType == "AddNegativeKeyword");
        row.Mode.Should().Be(AdAutonomyMode.AutoWithinLimits);
        row.UpdatedBy.Should().Be("chief");
        var audit = await verify.AdAuditEvents.SingleAsync();
        audit.EventType.Should().Be(AdAuditEventType.SettingChanged);
        using var payload = JsonDocument.Parse(audit.PayloadJson);
        payload.RootElement.GetProperty("old").GetString().Should().Be("ProposeOnly");
        payload.RootElement.GetProperty("new").GetString().Should().Be("AutoWithinLimits");
    }

    [Theory]
    [InlineData("NoSuchKey", 1)]
    [InlineData(AdLimitKeys.MaxActionsPerProposal, -1)]
    [InlineData(AdLimitKeys.Enabled, 2)]
    public async Task UpdateLimit_InvalidInput_IsRejected(string key, int value)
    {
        using var h = new ProposalTestHarness();

        var response = await Limit(h).Handle(new UpdateAdLimitSettingRequest
        {
            Platform = AdPlatform.GoogleAds, ActionType = AdActionType.PauseAd, Key = key, Value = value,
        }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsInvalidSetting);
    }

    [Fact]
    public async Task UpdateLimit_ChangesValueAndAudits()
    {
        using var h = new ProposalTestHarness();

        await Limit(h).Handle(new UpdateAdLimitSettingRequest
        {
            Platform = AdPlatform.GoogleAds, ActionType = AdActionType.PauseAd, Key = AdLimitKeys.McpMaxActionsPerProposal, Value = 5,
        }, CancellationToken.None);

        await using var verify = h.NewContext();
        (await verify.AdLimitSettings.SingleAsync(l => l.Platform == "GoogleAds" && l.ActionType == "PauseAd" && l.Key == AdLimitKeys.McpMaxActionsPerProposal)).Value.Should().Be(5);
        using var payload = JsonDocument.Parse((await verify.AdAuditEvents.SingleAsync()).PayloadJson);
        payload.RootElement.GetProperty("old").GetInt32().Should().Be(3);
        payload.RootElement.GetProperty("new").GetInt32().Should().Be(5);
    }

    [Fact]
    public async Task KillSwitch_GlobalOn_AndPlatformOverrideCreated()
    {
        using var h = new ProposalTestHarness();

        await KillSwitch(h).Handle(new SetAdKillSwitchRequest { ExecutionEnabled = true }, CancellationToken.None);
        h.Context.ChangeTracker.Clear();
        await KillSwitch(h).Handle(new SetAdKillSwitchRequest { Platform = AdPlatform.MetaAds, ExecutionEnabled = false }, CancellationToken.None);

        await using var verify = h.NewContext();
        var switches = await verify.AdKillSwitches.ToListAsync();
        AdKillSwitchPolicy.IsExecutionEnabled(switches, "GoogleAds").Should().BeTrue();
        AdKillSwitchPolicy.IsExecutionEnabled(switches, "MetaAds").Should().BeFalse();
        (await verify.AdAuditEvents.CountAsync(e => e.EventType == AdAuditEventType.KillSwitchChanged)).Should().Be(2);
    }

    [Fact]
    public async Task Mutations_FromMcpOrDeniedChannel_AreRejected()
    {
        using var h = new ProposalTestHarness();
        h.UseChannel(ApprovalChannel.Mcp);
        var viaMcp = await KillSwitch(h).Handle(new SetAdKillSwitchRequest { ExecutionEnabled = true }, CancellationToken.None);
        h.DenyChannel();
        var denied = await Autonomy(h).Handle(new UpdateAdAutonomySettingRequest { Platform = AdPlatform.GoogleAds, ActionType = AdActionType.PauseAd, Mode = AdAutonomyMode.Auto }, CancellationToken.None);

        viaMcp.ErrorCode.Should().Be(ErrorCodes.MarketingAdsApprovalChannelDenied);
        denied.ErrorCode.Should().Be(ErrorCodes.MarketingAdsApprovalChannelDenied);
        await using var verify = h.NewContext();
        (await verify.AdKillSwitches.SingleAsync()).ExecutionEnabled.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `MSBUILDDISABLENODEREUSE=1 dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false`
Expected: FAIL — `The type or namespace name 'GetAdSettings' does not exist`.

- [ ] **Step 3: Create the DTOs** — `Contracts/AdSettingsDtos.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Contracts;

public class AdAutonomySettingDto
{
    public AdPlatform Platform { get; set; }
    public AdActionType ActionType { get; set; }
    public AdAutonomyMode Mode { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = null!;
}

public class AdLimitSettingDto
{
    public AdPlatform Platform { get; set; }
    public AdActionType ActionType { get; set; }
    public string Key { get; set; } = null!;
    public int Value { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = null!;
}

public class AdKillSwitchDto
{
    /// <summary>"Global" or a platform name.</summary>
    public string Scope { get; set; } = null!;
    public bool ExecutionEnabled { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = null!;
}

public class AdApprovalRateDto
{
    public AdPlatform Platform { get; set; }
    public AdActionType ActionType { get; set; }
    public int Decided { get; set; }
    public int ApprovedWithoutEdit { get; set; }
    /// <summary>ApprovedWithoutEdit / Decided, null when nothing was decided yet.</summary>
    public decimal? Rate { get; set; }
}
```

- [ ] **Step 4: Implement Get**

`GetAdSettingsRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.GetAdSettings;

public class GetAdSettingsRequest : IRequest<GetAdSettingsResponse>
{
}
```

`GetAdSettingsResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.GetAdSettings;

public class GetAdSettingsResponse : BaseResponse
{
    public List<AdAutonomySettingDto> Autonomy { get; set; } = new();
    public List<AdLimitSettingDto> Limits { get; set; } = new();
    public List<AdKillSwitchDto> KillSwitches { get; set; } = new();
    public bool IsExecutionEnabledGlobally { get; set; }
    public List<AdApprovalRateDto> ApprovalRates { get; set; } = new();

    public GetAdSettingsResponse() { }
    public GetAdSettingsResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

`GetAdSettingsHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Contracts;
using Anela.Heblo.Domain.Features.MarketingAds;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.GetAdSettings;

/// <summary>Guardrails as data: also what C4's GetAdGuardrails MCP tool returns to agents.</summary>
public class GetAdSettingsHandler : IRequestHandler<GetAdSettingsRequest, GetAdSettingsResponse>
{
    private readonly IAdGovernanceRepository _governance;
    private readonly IAdProposalRepository _proposals;

    public GetAdSettingsHandler(IAdGovernanceRepository governance, IAdProposalRepository proposals)
    {
        _governance = governance;
        _proposals = proposals;
    }

    public async Task<GetAdSettingsResponse> Handle(GetAdSettingsRequest request, CancellationToken cancellationToken)
    {
        var autonomy = await _governance.GetAutonomySettingsAsync(cancellationToken);
        var limits = await _governance.GetLimitSettingsAsync(null, null, cancellationToken);
        var switches = await _governance.GetKillSwitchesAsync(cancellationToken);
        var outcomes = await _proposals.GetApprovalOutcomeCountsAsync(cancellationToken);

        return new GetAdSettingsResponse
        {
            Autonomy = autonomy.Where(a => IsKnown(a.Platform, a.ActionType)).Select(a => new AdAutonomySettingDto
            {
                Platform = Enum.Parse<AdPlatform>(a.Platform), ActionType = Enum.Parse<AdActionType>(a.ActionType),
                Mode = a.Mode, UpdatedAt = a.UpdatedAt, UpdatedBy = a.UpdatedBy,
            }).ToList(),
            Limits = limits.Where(l => IsKnown(l.Platform, l.ActionType)).Select(l => new AdLimitSettingDto
            {
                Platform = Enum.Parse<AdPlatform>(l.Platform), ActionType = Enum.Parse<AdActionType>(l.ActionType),
                Key = l.Key, Value = l.Value, UpdatedAt = l.UpdatedAt, UpdatedBy = l.UpdatedBy,
            }).ToList(),
            KillSwitches = switches.Select(s => new AdKillSwitchDto
            {
                Scope = s.Scope, ExecutionEnabled = s.ExecutionEnabled, UpdatedAt = s.UpdatedAt, UpdatedBy = s.UpdatedBy,
            }).ToList(),
            IsExecutionEnabledGlobally = switches.Any(s => s.Scope == AdKillSwitch.GlobalScope && s.ExecutionEnabled),
            ApprovalRates = outcomes.Where(o => IsKnown(o.Platform, o.ActionType)).Select(o => new AdApprovalRateDto
            {
                Platform = Enum.Parse<AdPlatform>(o.Platform), ActionType = Enum.Parse<AdActionType>(o.ActionType),
                Decided = o.Decided, ApprovedWithoutEdit = o.ApprovedWithoutEdit,
                Rate = o.Decided == 0 ? null : Math.Round((decimal)o.ApprovedWithoutEdit / o.Decided, 4),
            }).ToList(),
        };
    }

    private static bool IsKnown(string platform, string actionType) =>
        Enum.TryParse<AdPlatform>(platform, out _) && Enum.TryParse<AdActionType>(actionType, out _);
}
```

- [ ] **Step 5: Implement the three mutations**

`UpdateAdAutonomySetting/UpdateAdAutonomySettingRequest.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Domain.Features.MarketingAds;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.UpdateAdAutonomySetting;

public class UpdateAdAutonomySettingRequest : IRequest<UpdateAdAutonomySettingResponse>
{
    public AdPlatform Platform { get; set; }
    public AdActionType ActionType { get; set; }
    public AdAutonomyMode Mode { get; set; }
}
```

`UpdateAdAutonomySettingResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.UpdateAdAutonomySetting;

public class UpdateAdAutonomySettingResponse : BaseResponse
{
    public UpdateAdAutonomySettingResponse() { }
    public UpdateAdAutonomySettingResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

`UpdateAdAutonomySettingHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Audit;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.UpdateAdAutonomySetting;

public class UpdateAdAutonomySettingHandler : IRequestHandler<UpdateAdAutonomySettingRequest, UpdateAdAutonomySettingResponse>
{
    private readonly IAdGovernanceRepository _governance;
    private readonly IAdAuditLog _audit;
    private readonly IApprovalChannelResolver _channelResolver;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;

    public UpdateAdAutonomySettingHandler(IAdGovernanceRepository governance, IAdAuditLog audit, IApprovalChannelResolver channelResolver,
        ICurrentUserService currentUser, TimeProvider time)
    {
        _governance = governance;
        _audit = audit;
        _channelResolver = channelResolver;
        _currentUser = currentUser;
        _time = time;
    }

    public async Task<UpdateAdAutonomySettingResponse> Handle(UpdateAdAutonomySettingRequest request, CancellationToken cancellationToken)
    {
        var principal = _currentUser.GetCurrentUser().Id;
        if (string.IsNullOrWhiteSpace(principal)) return new UpdateAdAutonomySettingResponse(ErrorCodes.Unauthorized);
        var resolution = _channelResolver.Resolve();
        if (!resolution.Allowed || resolution.Channel != ApprovalChannel.Web) return new UpdateAdAutonomySettingResponse(ErrorCodes.MarketingAdsApprovalChannelDenied);
        if (!Enum.IsDefined(request.Platform) || !Enum.IsDefined(request.ActionType) || !Enum.IsDefined(request.Mode)) return new UpdateAdAutonomySettingResponse(ErrorCodes.MarketingAdsInvalidSetting);

        var now = _time.GetUtcNow().UtcDateTime;
        var platform = request.Platform.ToString();
        var actionType = request.ActionType.ToString();
        var setting = await _governance.FindAutonomySettingAsync(platform, actionType, cancellationToken);
        var oldMode = setting?.Mode; // primitive copy taken before the tracked row is mutated
        if (setting is null)
        {
            setting = new AdAutonomySetting { Platform = platform, ActionType = actionType };
            _governance.AddAutonomySetting(setting);
        }
        setting.Mode = request.Mode;
        setting.UpdatedAt = now;
        setting.UpdatedBy = principal;

        _audit.Append(AdAuditEvents.ForSetting(AdAuditEventType.SettingChanged, principal, resolution.Channel,
            new { setting = "autonomy", platform, actionType, old = oldMode?.ToString(), @new = request.Mode.ToString() }, now));
        await _governance.SaveChangesAsync(cancellationToken);
        return new UpdateAdAutonomySettingResponse();
    }
}
```

`UpdateAdLimitSetting/UpdateAdLimitSettingRequest.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.UpdateAdLimitSetting;

public class UpdateAdLimitSettingRequest : IRequest<UpdateAdLimitSettingResponse>
{
    public AdPlatform Platform { get; set; }
    public AdActionType ActionType { get; set; }
    public string Key { get; set; } = string.Empty;
    public int Value { get; set; }
}
```

`UpdateAdLimitSettingResponse.cs` — same shape as `UpdateAdAutonomySettingResponse` with class name `UpdateAdLimitSettingResponse` in namespace `...UseCases.UpdateAdLimitSetting`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.UpdateAdLimitSetting;

public class UpdateAdLimitSettingResponse : BaseResponse
{
    public UpdateAdLimitSettingResponse() { }
    public UpdateAdLimitSettingResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

`UpdateAdLimitSettingHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Audit;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.UpdateAdLimitSetting;

public class UpdateAdLimitSettingHandler : IRequestHandler<UpdateAdLimitSettingRequest, UpdateAdLimitSettingResponse>
{
    private readonly IAdGovernanceRepository _governance;
    private readonly IAdAuditLog _audit;
    private readonly IApprovalChannelResolver _channelResolver;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;

    public UpdateAdLimitSettingHandler(IAdGovernanceRepository governance, IAdAuditLog audit, IApprovalChannelResolver channelResolver,
        ICurrentUserService currentUser, TimeProvider time)
    {
        _governance = governance;
        _audit = audit;
        _channelResolver = channelResolver;
        _currentUser = currentUser;
        _time = time;
    }

    public async Task<UpdateAdLimitSettingResponse> Handle(UpdateAdLimitSettingRequest request, CancellationToken cancellationToken)
    {
        var principal = _currentUser.GetCurrentUser().Id;
        if (string.IsNullOrWhiteSpace(principal)) return new UpdateAdLimitSettingResponse(ErrorCodes.Unauthorized);
        var resolution = _channelResolver.Resolve();
        if (!resolution.Allowed || resolution.Channel != ApprovalChannel.Web) return new UpdateAdLimitSettingResponse(ErrorCodes.MarketingAdsApprovalChannelDenied);
        if (!IsValid(request)) return new UpdateAdLimitSettingResponse(ErrorCodes.MarketingAdsInvalidSetting);

        var now = _time.GetUtcNow().UtcDateTime;
        var platform = request.Platform.ToString();
        var actionType = request.ActionType.ToString();
        var setting = await _governance.FindLimitSettingAsync(platform, actionType, request.Key, cancellationToken);
        int? oldValue = setting?.Value; // primitive copy taken before the tracked row is mutated
        if (setting is null)
        {
            setting = new AdLimitSetting { Platform = platform, ActionType = actionType, Key = request.Key };
            _governance.AddLimitSetting(setting);
        }
        setting.Value = request.Value;
        setting.UpdatedAt = now;
        setting.UpdatedBy = principal;

        _audit.Append(AdAuditEvents.ForSetting(AdAuditEventType.SettingChanged, principal, resolution.Channel,
            new { setting = "limit", platform, actionType, key = request.Key, old = oldValue, @new = request.Value }, now));
        await _governance.SaveChangesAsync(cancellationToken);
        return new UpdateAdLimitSettingResponse();
    }

    private static bool IsValid(UpdateAdLimitSettingRequest r) =>
        Enum.IsDefined(r.Platform) && Enum.IsDefined(r.ActionType)
        && AdLimitKeys.All.Contains(r.Key) && r.Value >= 0
        && (r.Key != AdLimitKeys.Enabled || r.Value is 0 or 1);
}
```

`SetAdKillSwitch/SetAdKillSwitchRequest.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.SetAdKillSwitch;

public class SetAdKillSwitchRequest : IRequest<SetAdKillSwitchResponse>
{
    /// <summary>null = the global switch; a platform = an override that can only disable that platform.</summary>
    public AdPlatform? Platform { get; set; }
    public bool ExecutionEnabled { get; set; }
}
```

`SetAdKillSwitchResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.SetAdKillSwitch;

public class SetAdKillSwitchResponse : BaseResponse
{
    public SetAdKillSwitchResponse() { }
    public SetAdKillSwitchResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

`SetAdKillSwitchHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Audit;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.SetAdKillSwitch;

public class SetAdKillSwitchHandler : IRequestHandler<SetAdKillSwitchRequest, SetAdKillSwitchResponse>
{
    private readonly IAdGovernanceRepository _governance;
    private readonly IAdAuditLog _audit;
    private readonly IApprovalChannelResolver _channelResolver;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;

    public SetAdKillSwitchHandler(IAdGovernanceRepository governance, IAdAuditLog audit, IApprovalChannelResolver channelResolver,
        ICurrentUserService currentUser, TimeProvider time)
    {
        _governance = governance;
        _audit = audit;
        _channelResolver = channelResolver;
        _currentUser = currentUser;
        _time = time;
    }

    public async Task<SetAdKillSwitchResponse> Handle(SetAdKillSwitchRequest request, CancellationToken cancellationToken)
    {
        var principal = _currentUser.GetCurrentUser().Id;
        if (string.IsNullOrWhiteSpace(principal)) return new SetAdKillSwitchResponse(ErrorCodes.Unauthorized);
        var resolution = _channelResolver.Resolve();
        if (!resolution.Allowed || resolution.Channel != ApprovalChannel.Web) return new SetAdKillSwitchResponse(ErrorCodes.MarketingAdsApprovalChannelDenied);
        if (request.Platform is { } p && !Enum.IsDefined(p)) return new SetAdKillSwitchResponse(ErrorCodes.MarketingAdsInvalidSetting);

        var now = _time.GetUtcNow().UtcDateTime;
        var scope = request.Platform?.ToString() ?? AdKillSwitch.GlobalScope;
        var killSwitch = await _governance.FindKillSwitchAsync(scope, cancellationToken);
        bool? oldValue = killSwitch?.ExecutionEnabled; // primitive copy taken before mutation
        if (killSwitch is null)
        {
            killSwitch = new AdKillSwitch { Scope = scope };
            _governance.AddKillSwitch(killSwitch);
        }
        killSwitch.ExecutionEnabled = request.ExecutionEnabled;
        killSwitch.UpdatedAt = now;
        killSwitch.UpdatedBy = principal;

        _audit.Append(AdAuditEvents.ForSetting(AdAuditEventType.KillSwitchChanged, principal, resolution.Channel,
            new { scope, old = oldValue, @new = request.ExecutionEnabled }, now));
        await _governance.SaveChangesAsync(cancellationToken);
        return new SetAdKillSwitchResponse();
    }
}
```

- [ ] **Step 6: Run the tests**

```bash
MSBUILDDISABLENODEREUSE=1 dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~AdSettingsHandlersTests"
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingAds/Proposals backend/test/Anela.Heblo.Tests/Features/MarketingAds/Proposals/UseCases/AdSettingsHandlersTests.cs
git commit -m "feat: audited ad autonomy, limit and kill switch settings

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 15: Queries and agent runs — list/get proposals, audit log, start/finish/list agent runs

**Files:**
- Create: `.../Proposals/Contracts/AdActivityDtos.cs`
- Create (`.../Proposals/UseCases/`): `ListAdProposals/*`, `GetAdProposal/*`, `ListAdAuditEvents/*`, `StartAgentRun/*`, `FinishAgentRun/*`, `ListAgentRuns/*` (Request/Response/Handler each)
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/Proposals/UseCases/AdProposalQueriesTests.cs`, `AgentRunHandlersTests.cs`

**Interfaces:**
- Produces:
  - `ListAdProposalsRequest { AdProposalStatus? Status; AdPlatform? Platform; bool Mine; int Page = 1; int PageSize = 50 }` → `ListAdProposalsResponse { List<AdProposalSummaryDto> Items; int TotalCount }`
  - `GetAdProposalRequest { int Id }` → `GetAdProposalResponse { AdProposalDetailDto Proposal }`
  - `ListAdAuditEventsRequest { int? ProposalId; AdAuditEventType? EventType; int Page = 1; int PageSize = 50 }` → `ListAdAuditEventsResponse { List<AdAuditEventDto> Items; int TotalCount }`
  - `StartAgentRunRequest { string AgentName; List<string> DataScopesRead }` → `StartAgentRunResponse { Guid AgentRunId }`
  - `FinishAgentRunRequest { Guid AgentRunId; AgentRunStatus Status; string? Summary; List<string> DataScopesRead; long? InputTokens; long? OutputTokens; decimal? CostCzk }` → `FinishAgentRunResponse { AgentRunDto Run }`
  - `ListAgentRunsRequest { int Page = 1; int PageSize = 50 }` → `ListAgentRunsResponse { List<AgentRunDto> Items; int TotalCount }`
  - DTOs `AdAuditEventDto { long Id; int? ProposalId; int? Version; AdAuditEventType EventType; string ActorPrincipal; AdActorKind ActorKind; ApprovalChannel Channel; string PayloadJson; DateTime OccurredAt }`, `AgentRunDto { Guid Id; string AgentName; string Principal; DateTime StartedAt; DateTime? FinishedAt; AgentRunStatus Status; string? Summary; List<string> DataScopesRead; int ProposalsCreated; long? InputTokens; long? OutputTokens; decimal? CostCzk }`

Agent runs: only the principal who started a run may finish it (another principal gets 404 — the run's existence is not revealed); finishing twice or with `Running` → 400 `MarketingAdsInvalidAgentRun`; `ProposalsCreated` is counted from proposals carrying the run id. Limits: `AgentName` 1–100 chars, `Summary` ≤ 4000, ≤ 50 scopes of ≤ 200 chars, non-negative tokens/cost.

- [ ] **Step 1: Write the failing tests**

`AdProposalQueriesTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.GetAdProposal;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ListAdAuditEvents;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ListAdProposals;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.UseCases;

public class AdProposalQueriesTests
{
    [Fact]
    public async Task List_Mine_ReturnsOnlyTheCallersProposalsWithDiffLines()
    {
        using var h = new ProposalTestHarness();
        await h.SeedPendingAsync(actionCount: 2, creator: "agent-1");
        await h.SeedPendingAsync(creator: "agent-2");
        h.ActAs("agent-1");

        var response = await new ListAdProposalsHandler(h.Proposals, h.ReadModel.Object, h.CurrentUser.Object)
            .Handle(new ListAdProposalsRequest { Mine = true }, CancellationToken.None);

        response.TotalCount.Should().Be(1);
        response.Items.Single().DiffLines.Should().HaveCount(2);
        response.Items.Single().Platform.Should().Be(AdPlatform.GoogleAds);
    }

    [Fact]
    public async Task Get_ReturnsVersionsReasoningSeparatelyAndCurrentEvaluation()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync();
        h.ReadModel.Setup(r => r.GetEntityNamesAsync(AdPlatform.GoogleAds, "acc-1", It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["ad-1"] = "Jarní krém" });

        var response = await new GetAdProposalHandler(h.Proposals, h.ReadModel.Object, h.ContextBuilder, h.Evaluator, h.CurrentUser.Object)
            .Handle(new GetAdProposalRequest { Id = id }, CancellationToken.None);

        var detail = response.Proposal;
        detail.Summary.DiffLines.Single().Should().Contain("„Jarní krém“ [ad-1]").And.NotContain("reason");
        detail.Versions.Single().Reasoning.Should().Be("reason");
        detail.CurrentEvaluation!.Decision.Should().Be(AdLimitsDecision.Allowed);
    }

    [Fact]
    public async Task Get_Unknown_Returns404()
    {
        using var h = new ProposalTestHarness();

        var response = await new GetAdProposalHandler(h.Proposals, h.ReadModel.Object, h.ContextBuilder, h.Evaluator, h.CurrentUser.Object)
            .Handle(new GetAdProposalRequest { Id = 42 }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsProposalNotFound);
    }

    [Fact]
    public async Task ListAudit_FiltersByProposal()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedApprovedAsync();
        await using (var ctx = h.NewContext())
        {
            ctx.AdAuditEvents.Add(new AdAuditEvent { ProposalId = id, Version = 1, EventType = AdAuditEventType.Approved, ActorPrincipal = "chief", ActorKind = AdActorKind.Human, Channel = ApprovalChannel.Web, OccurredAt = h.Now });
            ctx.AdAuditEvents.Add(new AdAuditEvent { EventType = AdAuditEventType.KillSwitchChanged, ActorPrincipal = "admin", ActorKind = AdActorKind.Human, Channel = ApprovalChannel.Web, OccurredAt = h.Now });
            await ctx.SaveChangesAsync();
        }

        var response = await new ListAdAuditEventsHandler(h.Audit).Handle(new ListAdAuditEventsRequest { ProposalId = id }, CancellationToken.None);

        response.TotalCount.Should().Be(1);
        response.Items.Single().EventType.Should().Be(AdAuditEventType.Approved);
    }
}
```

`AgentRunHandlersTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.FinishAgentRun;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ListAgentRuns;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.StartAgentRun;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.UseCases;

public class AgentRunHandlersTests
{
    private static StartAgentRunHandler Start(ProposalTestHarness h) => new(h.AgentRuns, h.CurrentUser.Object, h.Time);
    private static FinishAgentRunHandler Finish(ProposalTestHarness h) => new(h.AgentRuns, h.Proposals, h.CurrentUser.Object, h.Time);

    [Fact]
    public async Task StartThenFinish_RecordsTheRunAndCountsItsProposals()
    {
        using var h = new ProposalTestHarness();
        h.ActAs("agent-1");
        var started = await Start(h).Handle(new StartAgentRunRequest { AgentName = "paid-search", DataScopesRead = new() { "ads.search_terms" } }, CancellationToken.None);
        await using (var ctx = h.NewContext())
        {
            var p = AdProposal.Create(new AdProposalDraft("GoogleAds", "acc-1", "PauseAd", "agent-1", AdActorKind.Agent, started.AgentRunId, "[]", 1, "r"), h.Now, TimeSpan.FromHours(72));
            ctx.AdProposals.Add(p);
            await ctx.SaveChangesAsync();
        }
        h.Time.Advance(TimeSpan.FromMinutes(3));

        var finished = await Finish(h).Handle(new FinishAgentRunRequest
        {
            AgentRunId = started.AgentRunId, Status = AgentRunStatus.Succeeded, Summary = "1 proposal",
            DataScopesRead = new() { "ads.search_terms", "ads.daily_facts" }, InputTokens = 12000, OutputTokens = 800, CostCzk = 1.25m,
        }, CancellationToken.None);

        finished.Success.Should().BeTrue();
        await using var verify = h.NewContext();
        var run = await verify.AgentRuns.SingleAsync(r => r.Id == started.AgentRunId);
        run.Status.Should().Be(AgentRunStatus.Succeeded);
        run.ProposalsCreated.Should().Be(1);
        run.FinishedAt.Should().Be(h.Now);
        run.CostCzk.Should().Be(1.25m);
        finished.Run.DataScopesRead.Should().Equal("ads.search_terms", "ads.daily_facts");
    }

    [Fact]
    public async Task Finish_ByAnotherPrincipal_LooksLikeNotFound()
    {
        using var h = new ProposalTestHarness();
        h.ActAs("agent-1");
        var started = await Start(h).Handle(new StartAgentRunRequest { AgentName = "paid-search" }, CancellationToken.None);
        h.ActAs("agent-2");

        var response = await Finish(h).Handle(new FinishAgentRunRequest { AgentRunId = started.AgentRunId, Status = AgentRunStatus.Failed }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsAgentRunNotFound);
    }

    [Fact]
    public async Task Finish_Twice_IsInvalid()
    {
        using var h = new ProposalTestHarness();
        var started = await Start(h).Handle(new StartAgentRunRequest { AgentName = "paid-search" }, CancellationToken.None);
        await Finish(h).Handle(new FinishAgentRunRequest { AgentRunId = started.AgentRunId, Status = AgentRunStatus.Succeeded }, CancellationToken.None);

        var again = await Finish(h).Handle(new FinishAgentRunRequest { AgentRunId = started.AgentRunId, Status = AgentRunStatus.Failed }, CancellationToken.None);

        again.ErrorCode.Should().Be(ErrorCodes.MarketingAdsInvalidAgentRun);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Start_WithoutName_IsInvalid(string name)
    {
        using var h = new ProposalTestHarness();

        var response = await Start(h).Handle(new StartAgentRunRequest { AgentName = name }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsInvalidAgentRun);
    }

    [Fact]
    public async Task List_ReturnsNewestFirst()
    {
        using var h = new ProposalTestHarness();
        await Start(h).Handle(new StartAgentRunRequest { AgentName = "first" }, CancellationToken.None);
        h.Time.Advance(TimeSpan.FromMinutes(1));
        await Start(h).Handle(new StartAgentRunRequest { AgentName = "second" }, CancellationToken.None);

        var response = await new ListAgentRunsHandler(h.AgentRuns).Handle(new ListAgentRunsRequest(), CancellationToken.None);

        response.Items.Select(r => r.AgentName).Should().Equal("second", "first");
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `MSBUILDDISABLENODEREUSE=1 dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false`
Expected: FAIL — `The type or namespace name 'ListAdProposals' does not exist`.

- [ ] **Step 3: Create the activity DTOs** — `Contracts/AdActivityDtos.cs`:

```csharp
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Contracts;

public class AdAuditEventDto
{
    public long Id { get; set; }
    public int? ProposalId { get; set; }
    public int? Version { get; set; }
    public AdAuditEventType EventType { get; set; }
    public string ActorPrincipal { get; set; } = null!;
    public AdActorKind ActorKind { get; set; }
    public ApprovalChannel Channel { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTime OccurredAt { get; set; }
}

public class AgentRunDto
{
    public Guid Id { get; set; }
    public string AgentName { get; set; } = null!;
    public string Principal { get; set; } = null!;
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public AgentRunStatus Status { get; set; }
    public string? Summary { get; set; }
    public List<string> DataScopesRead { get; set; } = new();
    public int ProposalsCreated { get; set; }
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }
    public decimal? CostCzk { get; set; }
}
```

Add to `AdProposalDtoMapper.cs` (add `using System.Text.Json;`):

```csharp
    public static AdAuditEventDto ToDto(AdAuditEvent e) => new()
    {
        Id = e.Id, ProposalId = e.ProposalId, Version = e.Version, EventType = e.EventType, ActorPrincipal = e.ActorPrincipal,
        ActorKind = e.ActorKind, Channel = e.Channel, PayloadJson = e.PayloadJson, OccurredAt = e.OccurredAt,
    };

    public static AgentRunDto ToDto(AgentRun run) => new()
    {
        Id = run.Id, AgentName = run.AgentName, Principal = run.Principal, StartedAt = run.StartedAt, FinishedAt = run.FinishedAt,
        Status = run.Status, Summary = run.Summary,
        DataScopesRead = JsonSerializer.Deserialize<List<string>>(run.DataScopesReadJson) ?? new List<string>(),
        ProposalsCreated = run.ProposalsCreated, InputTokens = run.InputTokens, OutputTokens = run.OutputTokens, CostCzk = run.CostCzk,
    };
```

- [ ] **Step 4: Implement the proposal queries**

`ListAdProposals/ListAdProposalsRequest.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Domain.Features.MarketingAds;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ListAdProposals;

public class ListAdProposalsRequest : IRequest<ListAdProposalsResponse>
{
    public AdProposalStatus? Status { get; set; }
    public AdPlatform? Platform { get; set; }
    /// <summary>Only proposals created by the caller.</summary>
    public bool Mine { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
```

`ListAdProposals/ListAdProposalsResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ListAdProposals;

public class ListAdProposalsResponse : BaseResponse
{
    public List<AdProposalSummaryDto> Items { get; set; } = new();
    public int TotalCount { get; set; }

    public ListAdProposalsResponse() { }
    public ListAdProposalsResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

`ListAdProposals/ListAdProposalsHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.ReadModel;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ListAdProposals;

public class ListAdProposalsHandler : IRequestHandler<ListAdProposalsRequest, ListAdProposalsResponse>
{
    private readonly IAdProposalRepository _proposals;
    private readonly IAdsReadModel _readModel;
    private readonly ICurrentUserService _currentUser;

    public ListAdProposalsHandler(IAdProposalRepository proposals, IAdsReadModel readModel, ICurrentUserService currentUser)
    {
        _proposals = proposals;
        _readModel = readModel;
        _currentUser = currentUser;
    }

    public async Task<ListAdProposalsResponse> Handle(ListAdProposalsRequest request, CancellationToken cancellationToken)
    {
        string? creator = null;
        if (request.Mine)
        {
            creator = _currentUser.GetCurrentUser().Id;
            if (string.IsNullOrWhiteSpace(creator)) return new ListAdProposalsResponse(ErrorCodes.Unauthorized);
        }

        var (items, total) = await _proposals.ListAsync(new AdProposalQuery
        {
            Status = request.Status,
            Platform = request.Platform?.ToString(),
            CreatedByPrincipal = creator,
            Page = request.Page,
            PageSize = request.PageSize,
        }, cancellationToken);

        var summaries = new List<AdProposalSummaryDto>();
        foreach (var group in items.GroupBy(p => (p.Platform, p.AccountExternalId)))
        {
            var names = await _readModel.GetEntityNamesAsync(Enum.Parse<AdPlatform>(group.Key.Platform), group.Key.AccountExternalId,
                group.SelectMany(AdProposalDtoMapper.TargetIds).Distinct(StringComparer.Ordinal).ToList(), cancellationToken);
            summaries.AddRange(group.Select(p => AdProposalDtoMapper.ToSummary(p, names)));
        }

        return new ListAdProposalsResponse
        {
            Items = summaries.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id).ToList(),
            TotalCount = total,
        };
    }
}
```

`GetAdProposal/GetAdProposalRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.GetAdProposal;

public class GetAdProposalRequest : IRequest<GetAdProposalResponse>
{
    public int Id { get; set; }
}
```

`GetAdProposal/GetAdProposalResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.GetAdProposal;

public class GetAdProposalResponse : BaseResponse
{
    public AdProposalDetailDto Proposal { get; set; } = new();

    public GetAdProposalResponse() { }
    public GetAdProposalResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

`GetAdProposal/GetAdProposalHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.ReadModel;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.GetAdProposal;

public class GetAdProposalHandler : IRequestHandler<GetAdProposalRequest, GetAdProposalResponse>
{
    private readonly IAdProposalRepository _proposals;
    private readonly IAdsReadModel _readModel;
    private readonly IAdLimitsContextBuilder _limitsContext;
    private readonly IAdLimitsEvaluator _evaluator;
    private readonly ICurrentUserService _currentUser;

    public GetAdProposalHandler(IAdProposalRepository proposals, IAdsReadModel readModel, IAdLimitsContextBuilder limitsContext,
        IAdLimitsEvaluator evaluator, ICurrentUserService currentUser)
    {
        _proposals = proposals;
        _readModel = readModel;
        _limitsContext = limitsContext;
        _evaluator = evaluator;
        _currentUser = currentUser;
    }

    public async Task<GetAdProposalResponse> Handle(GetAdProposalRequest request, CancellationToken cancellationToken)
    {
        var proposal = await _proposals.GetReadOnlyAsync(request.Id, cancellationToken);
        if (proposal is null) return new GetAdProposalResponse(ErrorCodes.MarketingAdsProposalNotFound);

        var platform = Enum.Parse<AdPlatform>(proposal.Platform);
        var names = await _readModel.GetEntityNamesAsync(platform, proposal.AccountExternalId, AdProposalDtoMapper.TargetIds(proposal), cancellationToken);

        AdLimitsEvaluation? evaluation = null;
        if (proposal.Status == AdProposalStatus.Pending)
        {
            var actions = AdActionJson.Deserialize(proposal.GetCurrentVersion().ActionsJson);
            var approver = _currentUser.GetCurrentUser().Id;
            evaluation = _evaluator.Evaluate(await _limitsContext.BuildAsync(proposal, actions, ApprovalChannel.Web, approver, cancellationToken));
        }

        return new GetAdProposalResponse { Proposal = AdProposalDtoMapper.ToDetail(proposal, names, evaluation) };
    }
}
```

`ListAdAuditEvents/ListAdAuditEventsRequest.cs`, `ListAdAuditEventsResponse.cs`, `ListAdAuditEventsHandler.cs`:

```csharp
using Anela.Heblo.Domain.Features.MarketingAds;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ListAdAuditEvents;

public class ListAdAuditEventsRequest : IRequest<ListAdAuditEventsResponse>
{
    public int? ProposalId { get; set; }
    public AdAuditEventType? EventType { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
```

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ListAdAuditEvents;

public class ListAdAuditEventsResponse : BaseResponse
{
    public List<AdAuditEventDto> Items { get; set; } = new();
    public int TotalCount { get; set; }

    public ListAdAuditEventsResponse() { }
    public ListAdAuditEventsResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

```csharp
using Anela.Heblo.Domain.Features.MarketingAds;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ListAdAuditEvents;

public class ListAdAuditEventsHandler : IRequestHandler<ListAdAuditEventsRequest, ListAdAuditEventsResponse>
{
    private readonly IAdAuditLog _audit;

    public ListAdAuditEventsHandler(IAdAuditLog audit) => _audit = audit;

    public async Task<ListAdAuditEventsResponse> Handle(ListAdAuditEventsRequest request, CancellationToken cancellationToken)
    {
        var (items, total) = await _audit.ListAsync(new AdAuditQuery
        {
            ProposalId = request.ProposalId, EventType = request.EventType, Page = request.Page, PageSize = request.PageSize,
        }, cancellationToken);
        return new ListAdAuditEventsResponse { Items = items.Select(AdProposalDtoMapper.ToDto).ToList(), TotalCount = total };
    }
}
```

- [ ] **Step 5: Implement agent runs**

`StartAgentRun/StartAgentRunRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.StartAgentRun;

public class StartAgentRunRequest : IRequest<StartAgentRunResponse>
{
    public const int MaxAgentNameLength = 100;
    public const int MaxScopes = 50;
    public const int MaxScopeLength = 200;

    public string AgentName { get; set; } = string.Empty;
    public List<string> DataScopesRead { get; set; } = new();
}
```

`StartAgentRun/StartAgentRunResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.StartAgentRun;

public class StartAgentRunResponse : BaseResponse
{
    public Guid AgentRunId { get; set; }

    public StartAgentRunResponse() { }
    public StartAgentRunResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

`StartAgentRun/StartAgentRunHandler.cs`:

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.StartAgentRun;

public class StartAgentRunHandler : IRequestHandler<StartAgentRunRequest, StartAgentRunResponse>
{
    private readonly IAgentRunRepository _agentRuns;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;

    public StartAgentRunHandler(IAgentRunRepository agentRuns, ICurrentUserService currentUser, TimeProvider time)
    {
        _agentRuns = agentRuns;
        _currentUser = currentUser;
        _time = time;
    }

    public async Task<StartAgentRunResponse> Handle(StartAgentRunRequest request, CancellationToken cancellationToken)
    {
        var principal = _currentUser.GetCurrentUser().Id;
        if (string.IsNullOrWhiteSpace(principal)) return new StartAgentRunResponse(ErrorCodes.Unauthorized);
        var name = request.AgentName?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > StartAgentRunRequest.MaxAgentNameLength || !AgentRunScopes.AreValid(request.DataScopesRead))
        {
            return new StartAgentRunResponse(ErrorCodes.MarketingAdsInvalidAgentRun);
        }

        var run = AgentRun.Start(Guid.NewGuid(), name, principal, JsonSerializer.Serialize(request.DataScopesRead ?? new()), _time.GetUtcNow().UtcDateTime);
        _agentRuns.Add(run);
        await _agentRuns.SaveChangesAsync(cancellationToken);
        return new StartAgentRunResponse { AgentRunId = run.Id };
    }
}
```

`StartAgentRun/AgentRunScopes.cs` (shared validation, used by start and finish):

```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.StartAgentRun;

public static class AgentRunScopes
{
    public static bool AreValid(IReadOnlyCollection<string>? scopes) =>
        scopes is null
        || (scopes.Count <= StartAgentRunRequest.MaxScopes
            && scopes.All(s => !string.IsNullOrWhiteSpace(s) && s.Length <= StartAgentRunRequest.MaxScopeLength && !s.Any(char.IsControl)));
}
```

`FinishAgentRun/FinishAgentRunRequest.cs`:

```csharp
using Anela.Heblo.Domain.Features.MarketingAds;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.FinishAgentRun;

public class FinishAgentRunRequest : IRequest<FinishAgentRunResponse>
{
    public const int MaxSummaryLength = 4000;

    public Guid AgentRunId { get; set; }
    public AgentRunStatus Status { get; set; }
    public string? Summary { get; set; }
    public List<string> DataScopesRead { get; set; } = new();
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }
    public decimal? CostCzk { get; set; }
}
```

`FinishAgentRun/FinishAgentRunResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.FinishAgentRun;

public class FinishAgentRunResponse : BaseResponse
{
    public AgentRunDto Run { get; set; } = new();

    public FinishAgentRunResponse() { }
    public FinishAgentRunResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

`FinishAgentRun/FinishAgentRunHandler.cs`:

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.StartAgentRun;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.FinishAgentRun;

public class FinishAgentRunHandler : IRequestHandler<FinishAgentRunRequest, FinishAgentRunResponse>
{
    private readonly IAgentRunRepository _agentRuns;
    private readonly IAdProposalRepository _proposals;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;

    public FinishAgentRunHandler(IAgentRunRepository agentRuns, IAdProposalRepository proposals, ICurrentUserService currentUser, TimeProvider time)
    {
        _agentRuns = agentRuns;
        _proposals = proposals;
        _currentUser = currentUser;
        _time = time;
    }

    public async Task<FinishAgentRunResponse> Handle(FinishAgentRunRequest request, CancellationToken cancellationToken)
    {
        var principal = _currentUser.GetCurrentUser().Id;
        if (string.IsNullOrWhiteSpace(principal)) return new FinishAgentRunResponse(ErrorCodes.Unauthorized);

        var run = await _agentRuns.FindAsync(request.AgentRunId, cancellationToken);
        if (run is null || run.Principal != principal) return new FinishAgentRunResponse(ErrorCodes.MarketingAdsAgentRunNotFound);
        if (!IsValid(request) || run.Status != AgentRunStatus.Running) return new FinishAgentRunResponse(ErrorCodes.MarketingAdsInvalidAgentRun);

        var proposalsCreated = await _proposals.CountProposalsForRunAsync(run.Id, cancellationToken);
        run.Finish(request.Status, request.Summary?.Trim(), JsonSerializer.Serialize(request.DataScopesRead ?? new()), proposalsCreated,
            request.InputTokens, request.OutputTokens, request.CostCzk, _time.GetUtcNow().UtcDateTime);
        await _agentRuns.SaveChangesAsync(cancellationToken);
        return new FinishAgentRunResponse { Run = AdProposalDtoMapper.ToDto(run) };
    }

    private static bool IsValid(FinishAgentRunRequest r) =>
        r.Status is AgentRunStatus.Succeeded or AgentRunStatus.Failed
        && (r.Summary is null || r.Summary.Length <= FinishAgentRunRequest.MaxSummaryLength)
        && AgentRunScopes.AreValid(r.DataScopesRead)
        && r.InputTokens is null or >= 0
        && r.OutputTokens is null or >= 0
        && r.CostCzk is null or >= 0;
}
```

`ListAgentRuns/ListAgentRunsRequest.cs`, `ListAgentRunsResponse.cs`, `ListAgentRunsHandler.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ListAgentRuns;

public class ListAgentRunsRequest : IRequest<ListAgentRunsResponse>
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
```

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ListAgentRuns;

public class ListAgentRunsResponse : BaseResponse
{
    public List<AgentRunDto> Items { get; set; } = new();
    public int TotalCount { get; set; }

    public ListAgentRunsResponse() { }
    public ListAgentRunsResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

```csharp
using Anela.Heblo.Domain.Features.MarketingAds;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ListAgentRuns;

public class ListAgentRunsHandler : IRequestHandler<ListAgentRunsRequest, ListAgentRunsResponse>
{
    private readonly IAgentRunRepository _agentRuns;

    public ListAgentRunsHandler(IAgentRunRepository agentRuns) => _agentRuns = agentRuns;

    public async Task<ListAgentRunsResponse> Handle(ListAgentRunsRequest request, CancellationToken cancellationToken)
    {
        var (items, total) = await _agentRuns.ListAsync(request.Page, request.PageSize, cancellationToken);
        return new ListAgentRunsResponse { Items = items.Select(AdProposalDtoMapper.ToDto).ToList(), TotalCount = total };
    }
}
```

- [ ] **Step 6: Run the tests**

```bash
MSBUILDDISABLENODEREUSE=1 dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~AdProposalQueriesTests|FullyQualifiedName~AgentRunHandlersTests"
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingAds/Proposals backend/test/Anela.Heblo.Tests/Features/MarketingAds/Proposals/UseCases
git commit -m "feat: ad proposal queries, audit log listing and agent run reporting

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 16: Replace C2's no-op `IAdExecutionLookup` with the real lookup

C2's out-of-band matcher asks "which Heblo executions touched this account in this window?". C2 ships the contract plus `Sync/NoOpAdExecutionLookup.cs`; C3 owns the executions table, so it supplies the implementation and removes the no-op.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingAds/Proposals/Execution/AdExecutionLookup.cs`
- Delete: `backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/NoOpAdExecutionLookup.cs` and its DI registration line (location recorded in *Before you start*)
- Modify: `.../Proposals/MarketingAdsProposalsModule.cs` (register)
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/Proposals/Execution/AdExecutionLookupTests.cs`

**Interfaces:**
- Consumes (C2, namespace `Anela.Heblo.Application.Features.MarketingAds.Contracts` — verify against main; if a member differs, keep this query and adapt only the mapping):

```csharp
public interface IAdExecutionLookup
{
    Task<IReadOnlyList<AdExecutedAction>> GetExecutedActionsAsync(
        AdPlatform platform, string accountExternalId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
public sealed record AdExecutedAction(string ExecutionId, AdActionType Type, AdPlatform Platform, string AccountExternalId,
    AdEntityLevel TargetLevel, string TargetExternalId, string? PlatformResourceId, DateTimeOffset ExecutedAt,
    IReadOnlyDictionary<string, string> Payload);
```

- Produces: `AdExecutionLookup : IAdExecutionLookup` (scoped). `ExecutionId` = the `AdProposalActionExecutions.Id` as a string. Only **succeeded executions** are returned: C2's record cannot express a revert (opposite change), so a revert's platform event is classified out-of-band — documented as a known quirk in the process doc.

- [ ] **Step 1: Write the failing test**

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Execution;
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Execution;

public class AdExecutionLookupTests
{
    private static AdProposalActionExecution Row(int index, AdActionExecutionState state, DateTime executedAt) => new()
    {
        Version = 1, ActionIndex = index, Platform = "GoogleAds", AccountExternalId = "acc-1", ActionType = "AddNegativeKeyword",
        TargetLevel = "Campaign", TargetExternalId = $"cmp-{index}", State = state, ExecutedAt = executedAt,
        PlatformResourceId = $"res-{index}", PayloadJson = """{"text":"zdarma","matchType":"Exact"}""",
    };

    [Fact]
    public async Task ReturnsOnlySucceededExecutionsInsideTheWindow()
    {
        using var h = new ProposalTestHarness();
        var t = h.Now;
        var proposal = AdProposal.Create(new AdProposalDraft("GoogleAds", "acc-1", "AddNegativeKeyword", "a", AdActorKind.Agent, null, "[]", 3, "r"), t, TimeSpan.FromHours(72));
        proposal.RecordExecution(Row(0, AdActionExecutionState.Succeeded, t.AddMinutes(10)));
        proposal.RecordExecution(Row(1, AdActionExecutionState.Failed, t.AddMinutes(10)));
        proposal.RecordExecution(Row(2, AdActionExecutionState.Succeeded, t.AddDays(-2)));
        h.Proposals.Add(proposal);
        await h.Proposals.SaveChangesAsync(CancellationToken.None);

        var result = await new AdExecutionLookup(h.Proposals).GetExecutedActionsAsync(
            AdPlatform.GoogleAds, "acc-1", new DateTimeOffset(t), new DateTimeOffset(t.AddHours(1)), CancellationToken.None);

        var executed = result.Should().ContainSingle().Subject;
        executed.Type.Should().Be(AdActionType.AddNegativeKeyword);
        executed.Platform.Should().Be(AdPlatform.GoogleAds);
        executed.AccountExternalId.Should().Be("acc-1");
        executed.TargetLevel.Should().Be(AdEntityLevel.Campaign);
        executed.TargetExternalId.Should().Be("cmp-0");
        executed.PlatformResourceId.Should().Be("res-0");
        executed.ExecutedAt.Should().Be(new DateTimeOffset(t.AddMinutes(10)));
        executed.Payload.Should().Contain(new KeyValuePair<string, string>("text", "zdarma"));
        long.Parse(executed.ExecutionId).Should().BePositive();
    }

    [Fact]
    public async Task RevertInsideTheWindowIsReturnedAsARevertRow()
    {
        using var h = new ProposalTestHarness();
        var t = h.Now;
        var proposal = AdProposal.Create(new AdProposalDraft("GoogleAds", "acc-1", "AddNegativeKeyword", "a", AdActorKind.Agent, null, "[]", 1, "r"), t, TimeSpan.FromHours(72));
        var execution = Row(0, AdActionExecutionState.Succeeded, t.AddDays(-2));   // executed outside the window
        proposal.RecordExecution(execution);
        execution.MarkReverted(t.AddMinutes(5), "{}");                               // reverted inside it
        h.Proposals.Add(proposal);
        await h.Proposals.SaveChangesAsync(CancellationToken.None);

        var result = await new AdExecutionLookup(h.Proposals).GetExecutedActionsAsync(
            AdPlatform.GoogleAds, "acc-1", new DateTimeOffset(t), new DateTimeOffset(t.AddHours(1)), CancellationToken.None);

        var revert = result.Should().ContainSingle().Subject;
        revert.IsRevert.Should().BeTrue();
        revert.ExecutedAt.Should().Be(new DateTimeOffset(t.AddMinutes(5)));
    }

    [Fact]
    public async Task OtherAccountsAreIgnored()
    {
        using var h = new ProposalTestHarness();

        var result = await new AdExecutionLookup(h.Proposals).GetExecutedActionsAsync(
            AdPlatform.Sklik, "nope", DateTimeOffset.MinValue, DateTimeOffset.MaxValue, CancellationToken.None);

        result.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `MSBUILDDISABLENODEREUSE=1 dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false`
Expected: FAIL — `The type or namespace name 'AdExecutionLookup' could not be found`.

- [ ] **Step 3: Implement**

```csharp
using System.Globalization;
using System.Text.Json;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Execution;

/// <summary>Heblo-originated platform changes in a window, for C2's out-of-band matcher. Times are stored as UTC 'timestamp'.</summary>
public sealed class AdExecutionLookup : IAdExecutionLookup
{
    private readonly IAdProposalRepository _proposals;

    public AdExecutionLookup(IAdProposalRepository proposals) => _proposals = proposals;

    public async Task<IReadOnlyList<AdExecutedAction>> GetExecutedActionsAsync(AdPlatform platform, string accountExternalId,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var fromUtc = from.UtcDateTime;
        var toUtc = to.UtcDateTime;
        var rows = await _proposals.GetExecutionsInWindowAsync(platform.ToString(), accountExternalId, fromUtc, toUtc, ct);
        var executions = rows
            .Where(r => r.State == AdActionExecutionState.Succeeded && r.ExecutedAt >= fromUtc && r.ExecutedAt <= toUtc)
            .Select(r => ToAction(r, platform, r.ExecutedAt, isRevert: false));
        // A revert is its own Heblo-originated platform change (ad re-enabled / negative removed), so C2's matcher
        // must see it too — otherwise every revert is flagged out-of-band.
        var reverts = rows
            .Where(r => r.RevertState == AdActionRevertState.Reverted && r.RevertedAt is { } at && at >= fromUtc && at <= toUtc)
            .Select(r => ToAction(r, platform, r.RevertedAt!.Value, isRevert: true));
        return executions.Concat(reverts).ToList();
    }

    private static AdExecutedAction ToAction(AdProposalActionExecution r, AdPlatform platform, DateTime atUtc, bool isRevert) =>
        new(
            r.Id.ToString(CultureInfo.InvariantCulture),
            Enum.Parse<AdActionType>(r.ActionType),
            platform,
            r.AccountExternalId,
            Enum.Parse<AdEntityLevel>(r.TargetLevel),
            r.TargetExternalId,
            r.PlatformResourceId,
            new DateTimeOffset(DateTime.SpecifyKind(atUtc, DateTimeKind.Utc)),
            JsonSerializer.Deserialize<Dictionary<string, string>>(r.PayloadJson) ?? new Dictionary<string, string>(),
            IsRevert: isRevert);
    }
}
```

- [ ] **Step 4: Swap the registration** — delete `Sync/NoOpAdExecutionLookup.cs` and C2's `services.Add…<IAdExecutionLookup, NoOpAdExecutionLookup>()` line; in `MarketingAdsProposalsModule.AddMarketingAdsProposalsModule` add before `return services;`:

```csharp
        // Real lookup over AdProposalActionExecutions; replaces C2's NoOpAdExecutionLookup.
        services.AddScoped<IAdExecutionLookup, AdExecutionLookup>();
```

Confirm exactly one implementation remains:

```bash
grep -rn "IAdExecutionLookup\|NoOpAdExecutionLookup" backend/src backend/test --include='*.cs'
```

Expected: the interface, the matcher/sync usages, `AdExecutionLookup` and its single registration — no `NoOpAdExecutionLookup`. If a C2 test referenced the no-op directly, switch it to a `Mock<IAdExecutionLookup>`.

- [ ] **Step 5: Run the tests** (C2's matcher/sync tests must still pass)

```bash
MSBUILDDISABLENODEREUSE=1 dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~MarketingAds"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A backend/src/Anela.Heblo.Application/Features/MarketingAds backend/test/Anela.Heblo.Tests/Features/MarketingAds
git commit -m "feat: real IAdExecutionLookup over executed ad actions

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 17: REST controllers for the web UI

**Files:**
- Create: `backend/src/Anela.Heblo.API/Controllers/MarketingAdsProposalsController.cs`, `MarketingAdsSettingsController.cs`, `MarketingAdsActivityController.cs`
- Regenerated: `frontend/src/api/generated/api-client.ts`
- Test: `backend/test/Anela.Heblo.Tests/Authorization/MarketingAdsControllersAuthorizationTests.cs`

**Interfaces:**
- Consumes: Task 9–15 requests/responses; `Feature.Marketing_Ads`, `Feature.Marketing_AdApprovals` (Task 1).
- Produces (for C5):

| Method | Route | Gate (class AND method) | Request |
|---|---|---|---|
| GET | `api/marketing-ads/proposals` | Ads R | `ListAdProposalsRequest` (query) |
| GET | `api/marketing-ads/proposals/{id}` | Ads R | — |
| POST | `api/marketing-ads/proposals` | Ads R + Ads W | `SubmitAdProposalRequest` |
| POST | `api/marketing-ads/proposals/{id}/revisions` | Ads R + Ads W | `ReviseAdProposalRequest` |
| POST | `api/marketing-ads/proposals/{id}/approve` | Ads R + AdApprovals W | `ApproveAdProposalRequest` (body `{ version }`) |
| POST | `api/marketing-ads/proposals/{id}/reject` | Ads R + AdApprovals W | `RejectAdProposalRequest` (body `{ version, reason }`) |
| POST | `api/marketing-ads/proposals/{id}/revert` | Ads R + AdApprovals Admin | — |
| GET | `api/marketing-ads/settings` | Ads R | — |
| PUT | `api/marketing-ads/settings/autonomy` | Ads R + AdApprovals Admin | `UpdateAdAutonomySettingRequest` |
| PUT | `api/marketing-ads/settings/limits` | Ads R + AdApprovals Admin | `UpdateAdLimitSettingRequest` |
| PUT | `api/marketing-ads/settings/kill-switch` | Ads R + AdApprovals Admin | `SetAdKillSwitchRequest` |
| GET | `api/marketing-ads/activity/audit` | Ads R | `ListAdAuditEventsRequest` (query) |
| GET | `api/marketing-ads/activity/agent-runs` | Ads R | `ListAgentRunsRequest` (query) |

`[FeatureAuthorize]` on class and method are both enforced (AND). The attribute gate is necessary but not sufficient: the handlers additionally require the approval channel (deny-all until C4). Agent-run start/finish have no REST endpoint — agents use MCP (C4).

- [ ] **Step 1: Write the failing test**

```csharp
using System.Reflection;
using Anela.Heblo.API.Controllers;
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class MarketingAdsControllersAuthorizationTests
{
    [Theory]
    [InlineData(typeof(MarketingAdsProposalsController))]
    [InlineData(typeof(MarketingAdsSettingsController))]
    [InlineData(typeof(MarketingAdsActivityController))]
    public void Controller_IsGatedByAdsRead(Type controller)
    {
        var attribute = controller.GetCustomAttribute<FeatureAuthorizeAttribute>();

        attribute.Should().NotBeNull();
        attribute!.Feature.Should().Be(Feature.Marketing_Ads);
        attribute.Level.Should().Be(AccessLevel.Read);
    }

    [Theory]
    [InlineData(typeof(MarketingAdsProposalsController), nameof(MarketingAdsProposalsController.SubmitProposal), Feature.Marketing_Ads, AccessLevel.Write)]
    [InlineData(typeof(MarketingAdsProposalsController), nameof(MarketingAdsProposalsController.ReviseProposal), Feature.Marketing_Ads, AccessLevel.Write)]
    [InlineData(typeof(MarketingAdsProposalsController), nameof(MarketingAdsProposalsController.ApproveProposal), Feature.Marketing_AdApprovals, AccessLevel.Write)]
    [InlineData(typeof(MarketingAdsProposalsController), nameof(MarketingAdsProposalsController.RejectProposal), Feature.Marketing_AdApprovals, AccessLevel.Write)]
    [InlineData(typeof(MarketingAdsProposalsController), nameof(MarketingAdsProposalsController.RevertProposal), Feature.Marketing_AdApprovals, AccessLevel.Admin)]
    [InlineData(typeof(MarketingAdsSettingsController), nameof(MarketingAdsSettingsController.UpdateAutonomy), Feature.Marketing_AdApprovals, AccessLevel.Admin)]
    [InlineData(typeof(MarketingAdsSettingsController), nameof(MarketingAdsSettingsController.UpdateLimit), Feature.Marketing_AdApprovals, AccessLevel.Admin)]
    [InlineData(typeof(MarketingAdsSettingsController), nameof(MarketingAdsSettingsController.SetKillSwitch), Feature.Marketing_AdApprovals, AccessLevel.Admin)]
    public void Mutation_RequiresItsOwnGate(Type controller, string method, Feature feature, AccessLevel level)
    {
        var attribute = controller.GetMethod(method)!.GetCustomAttribute<FeatureAuthorizeAttribute>();

        attribute.Should().NotBeNull($"{method} mutates state and must not inherit only the class-level Read gate");
        attribute!.Feature.Should().Be(feature);
        attribute.Level.Should().Be(level);
    }

    [Theory]
    [InlineData(typeof(MarketingAdsProposalsController), nameof(MarketingAdsProposalsController.ListProposals))]
    [InlineData(typeof(MarketingAdsProposalsController), nameof(MarketingAdsProposalsController.GetProposal))]
    [InlineData(typeof(MarketingAdsSettingsController), nameof(MarketingAdsSettingsController.GetSettings))]
    [InlineData(typeof(MarketingAdsActivityController), nameof(MarketingAdsActivityController.ListAuditEvents))]
    [InlineData(typeof(MarketingAdsActivityController), nameof(MarketingAdsActivityController.ListAgentRuns))]
    public void Reads_StayAtTheClassLevelGate(Type controller, string method)
    {
        controller.GetMethod(method)!.GetCustomAttributes<FeatureAuthorizeAttribute>(inherit: false).Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `MSBUILDDISABLENODEREUSE=1 dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false`
Expected: FAIL — `The type or namespace name 'MarketingAdsProposalsController' could not be found`.

- [ ] **Step 3: Implement the controllers**

`MarketingAdsProposalsController.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ApproveAdProposal;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.GetAdProposal;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ListAdProposals;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.RejectAdProposal;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ReviseAdProposal;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.RevertAdProposal;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.SubmitAdProposal;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

/// <summary>Marketing → ad proposals: inbox, detail, submit/revise, version-bound approve/reject, revert.</summary>
[FeatureAuthorize(Feature.Marketing_Ads)]
[ApiController]
[Route("api/marketing-ads/proposals")]
public class MarketingAdsProposalsController : BaseApiController
{
    private readonly IMediator _mediator;

    public MarketingAdsProposalsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [ProducesResponseType(typeof(ListAdProposalsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ListAdProposalsResponse>> ListProposals([FromQuery] ListAdProposalsRequest request) =>
        HandleResponse(await _mediator.Send(request));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(GetAdProposalResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GetAdProposalResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<GetAdProposalResponse>> GetProposal(int id) =>
        HandleResponse(await _mediator.Send(new GetAdProposalRequest { Id = id }));

    [HttpPost]
    [FeatureAuthorize(Feature.Marketing_Ads, AccessLevel.Write)]
    [ProducesResponseType(typeof(SubmitAdProposalResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SubmitAdProposalResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(SubmitAdProposalResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SubmitAdProposalResponse>> SubmitProposal([FromBody] SubmitAdProposalRequest request) =>
        HandleResponse(await _mediator.Send(request));

    [HttpPost("{id:int}/revisions")]
    [FeatureAuthorize(Feature.Marketing_Ads, AccessLevel.Write)]
    [ProducesResponseType(typeof(ReviseAdProposalResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ReviseAdProposalResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ReviseAdProposalResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ReviseAdProposalResponse>> ReviseProposal(int id, [FromBody] ReviseAdProposalRequest request)
    {
        request.Id = id;
        return HandleResponse(await _mediator.Send(request));
    }

    [HttpPost("{id:int}/approve")]
    [FeatureAuthorize(Feature.Marketing_AdApprovals, AccessLevel.Write)]
    [ProducesResponseType(typeof(ApproveAdProposalResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApproveAdProposalResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApproveAdProposalResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApproveAdProposalResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApproveAdProposalResponse>> ApproveProposal(int id, [FromBody] ApproveAdProposalRequest request)
    {
        request.Id = id;
        return HandleResponse(await _mediator.Send(request));
    }

    [HttpPost("{id:int}/reject")]
    [FeatureAuthorize(Feature.Marketing_AdApprovals, AccessLevel.Write)]
    [ProducesResponseType(typeof(RejectAdProposalResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RejectAdProposalResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(RejectAdProposalResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<RejectAdProposalResponse>> RejectProposal(int id, [FromBody] RejectAdProposalRequest request)
    {
        request.Id = id;
        return HandleResponse(await _mediator.Send(request));
    }

    [HttpPost("{id:int}/revert")]
    [FeatureAuthorize(Feature.Marketing_AdApprovals, AccessLevel.Admin)]
    [ProducesResponseType(typeof(RevertAdProposalResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RevertAdProposalResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(RevertAdProposalResponse), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(RevertAdProposalResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<RevertAdProposalResponse>> RevertProposal(int id) =>
        HandleResponse(await _mediator.Send(new RevertAdProposalRequest { Id = id }));
}
```

`MarketingAdsSettingsController.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.GetAdSettings;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.SetAdKillSwitch;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.UpdateAdAutonomySetting;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.UpdateAdLimitSetting;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

/// <summary>Marketing → ad guardrails: autonomy modes, limits and the execution kill switch.</summary>
[FeatureAuthorize(Feature.Marketing_Ads)]
[ApiController]
[Route("api/marketing-ads/settings")]
public class MarketingAdsSettingsController : BaseApiController
{
    private readonly IMediator _mediator;

    public MarketingAdsSettingsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [ProducesResponseType(typeof(GetAdSettingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<GetAdSettingsResponse>> GetSettings() =>
        HandleResponse(await _mediator.Send(new GetAdSettingsRequest()));

    [HttpPut("autonomy")]
    [FeatureAuthorize(Feature.Marketing_AdApprovals, AccessLevel.Admin)]
    [ProducesResponseType(typeof(UpdateAdAutonomySettingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(UpdateAdAutonomySettingResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(UpdateAdAutonomySettingResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<UpdateAdAutonomySettingResponse>> UpdateAutonomy([FromBody] UpdateAdAutonomySettingRequest request) =>
        HandleResponse(await _mediator.Send(request));

    [HttpPut("limits")]
    [FeatureAuthorize(Feature.Marketing_AdApprovals, AccessLevel.Admin)]
    [ProducesResponseType(typeof(UpdateAdLimitSettingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(UpdateAdLimitSettingResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(UpdateAdLimitSettingResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<UpdateAdLimitSettingResponse>> UpdateLimit([FromBody] UpdateAdLimitSettingRequest request) =>
        HandleResponse(await _mediator.Send(request));

    [HttpPut("kill-switch")]
    [FeatureAuthorize(Feature.Marketing_AdApprovals, AccessLevel.Admin)]
    [ProducesResponseType(typeof(SetAdKillSwitchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SetAdKillSwitchResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(SetAdKillSwitchResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SetAdKillSwitchResponse>> SetKillSwitch([FromBody] SetAdKillSwitchRequest request) =>
        HandleResponse(await _mediator.Send(request));
}
```

`MarketingAdsActivityController.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ListAdAuditEvents;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ListAgentRuns;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

/// <summary>Marketing → ad activity: append-only audit log and reported agent runs.</summary>
[FeatureAuthorize(Feature.Marketing_Ads)]
[ApiController]
[Route("api/marketing-ads/activity")]
public class MarketingAdsActivityController : BaseApiController
{
    private readonly IMediator _mediator;

    public MarketingAdsActivityController(IMediator mediator) => _mediator = mediator;

    [HttpGet("audit")]
    [ProducesResponseType(typeof(ListAdAuditEventsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ListAdAuditEventsResponse>> ListAuditEvents([FromQuery] ListAdAuditEventsRequest request) =>
        HandleResponse(await _mediator.Send(request));

    [HttpGet("agent-runs")]
    [ProducesResponseType(typeof(ListAgentRunsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ListAgentRunsResponse>> ListAgentRuns([FromQuery] ListAgentRunsRequest request) =>
        HandleResponse(await _mediator.Send(request));
}
```

- [ ] **Step 4: Run backend tests, then regenerate the TypeScript client**

```bash
MSBUILDDISABLENODEREUSE=1 dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~Anela.Heblo.Tests.Authorization"
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
git diff --stat frontend/src/api/generated/api-client.ts
```

Expected: tests PASS (incl. `AllControllerRoles_AreKnownMatrixRoles`); the generated client gains `MarketingAdsProposalsClient`-style methods and the 39XX `ErrorCodes` members.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/MarketingAds*.cs backend/test/Anela.Heblo.Tests/Authorization/MarketingAdsControllersAuthorizationTests.cs frontend/src/api/generated/api-client.ts
git commit -m "feat: REST endpoints for ad proposals, guardrail settings and activity

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 18: Process doc, full validation, PR

**Files:**
- Create: `docs/processes/flow-ad-proposal-approval.md`
- Modify or create: `docs/processes/module-marketing-ads.md` (C2 normally created it — add one line under *Processes*; create it from the content below only if missing)
- Regenerated: `docs/processes/INDEX.md`

- [ ] **Step 1: Write the process doc** — `docs/processes/flow-ad-proposal-approval.md` (replace `<sha>` with `git rev-parse --short HEAD` at the time of writing):

```markdown
---
process: flow-ad-proposal-approval
kind: workflow
module: marketing-ads
summary: Agents and people submit versioned ad-change proposals; server-side limits, autonomy and an approval channel decide who may approve; approved proposals execute action by action behind a kill switch, with append-only audit and revert from stored before-state.
owns:
  - backend/src/Anela.Heblo.Application/Features/MarketingAds/Proposals/**
  - backend/src/Anela.Heblo.Domain/Features/MarketingAds/**
  - backend/src/Anela.Heblo.Persistence/MarketingAds/**
  - backend/src/Anela.Heblo.API/Controllers/MarketingAds*.cs
verified_at: "<sha>"
related: []
---

# Ad proposal → approval → execution

## Purpose
Lets AI agents (and people) change live ad accounts (Google Ads, Meta, Sklik) only through a
structured, human-approved and fully audited path. The chief of marketing approves in the web UI
(`/marketing/ads/proposals`, PR C5) or via Claude/MCP (PR C4). Admins tune autonomy, limits and
the kill switch on `/marketing/ads/settings`.

## Trigger
- Submit / revise: `POST /api/marketing-ads/proposals[/{id}/revisions]` (C4: MCP `SubmitAdProposal`, `ReviseAdProposal`).
- Approve / reject: `POST /api/marketing-ads/proposals/{id}/approve|reject` with `{ version }`.
- Execution: Hangfire fire-and-forget `AdProposalExecutionJob.RunAsync(id)` enqueued after the final approval.
- Expiry: recurring job `marketing-ads-proposal-expiry`, cron `Ads:ExpiryCronExpression` (default `*/15 * * * *`, Europe/Prague).
- Revert: `POST /api/marketing-ads/proposals/{id}/revert` (web only, `Marketing_AdApprovals` Admin).
- States: Pending → Approved → Executing → Executed | PartiallyExecuted | Failed → Reverted; Pending → Rejected | Expired; revise keeps Pending at version+1.

## Data flow
1. Submit: shape validation → actions built with fixed old/new values → limits evaluated for Web → rejected if Denied, else `AdProposals` + `AdProposalVersions` (v1) + `AdAuditEvents(Submitted)`; autonomy may auto-approve as `system:autonomy` in the same save.
2. Approve: `IApprovalChannelResolver` → version check → limits for that channel/approver → `AdProposalApprovals` + audit; final approval enqueues execution.
3. Execute: claim (status Executing, `Revision` token) → per action: kill switch (`AdKillSwitches`) → executor `ReadCurrentAsync` → compare with OldValue → `ExecuteAsync` → `AdProposalActionExecutions` + audit.
4. Revert: per succeeded action, newest first, `IAdActionExecutor.RevertAsync` with the stored result.
5. C2's out-of-band matcher reads executed/reverted actions through `IAdExecutionLookup`.

## Logic & formulas
- Limits (`AdLimitSettings`, per platform × action type): `Enabled` 0/1, `MaxActionsPerProposal` (negatives 50, pauses 10), `McpMaxActionsPerProposal` (20 / 3), `MaxPausedAdsPer7Days` 20 (succeeded pauses of the account in a rolling 7 days + this proposal), `SecondApproverAbove` (seeded = max, i.e. off). Pausing the last enabled ad of an ad group (or an ad unknown to the `ads` schema) needs the web channel.
- Order: Denied → NeedsWebApproval → NeedsSecondApprover → Allowed. Unmanaged/unknown account → Denied.
- Autonomy (`AdAutonomySettings`): ProposeOnly (seed) / AutoWithinLimits (auto only when Allowed) / Auto (auto unless Denied).
- Kill switch: execution allowed only when the `Global` row is enabled and no platform row disables it. Seeded **disabled**.
- Platform `Failed` on one action continues; stale state, kill switch, missing executor or transport error stops and skips the rest.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `Ads:ProposalTtlHours` | 72 | Pending proposal lifetime; reset by a revision |
| `Ads:ExpiryCronExpression` | `*/15 * * * *` | Expiry job schedule |
| `Ads:StuckApprovedRequeueMinutes` | 15 | Approved proposals untouched this long are re-enqueued |

## Runtime facts
None yet — fill in after the first staging run.

## Known quirks
- Until PR C4 deploys, `DenyAllApprovalChannelResolver` blocks every approve/reject/revert/settings change, so execution stays disabled.
- Audit rows and proposal versions are protected by Postgres triggers (`marketing_ads_reject_mutation`); a DB owner can still disable triggers.
- New permissions (`marketing.ads.*`, `marketing.ad_approvals.*`) are not granted to existing prod groups by the seeder; C4's rollout grants them.
- Revert is not gated by the kill switch (it restores the account) and runs synchronously.
- `IAdExecutionLookup` returns executions only; a revert's platform change event is therefore classified out-of-band by C2's matcher.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Proposals/Limits/AdLimitsEvaluator.cs` — every limit rule
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Proposals/Execution/AdProposalExecutionJob.cs` — execution pipeline
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Proposals/UseCases/ApproveAdProposal/ApproveAdProposalHandler.cs` — approval rules
- `backend/src/Anela.Heblo.Domain/Features/MarketingAds/AdProposal.cs` — lifecycle
- `backend/src/Anela.Heblo.Persistence/MarketingAds/MarketingAdsSql.cs` — append-only triggers
```

If C2's process docs already own `backend/src/Anela.Heblo.Application/Features/MarketingAds/**` as a whole, narrow C2's glob is **not** your call — leave it; overlapping ownership is allowed. If the checker reports a dead glob for `Controllers/MarketingAds*.cs`, it means Task 17 is not committed yet.

- [ ] **Step 2: Link it from the module doc** — in `docs/processes/module-marketing-ads.md` under `## Processes` add:

```markdown
- `flow-ad-proposal-approval` — proposals, limits, autonomy, approval, execution behind the kill switch, revert, audit, agent runs; on demand + 15-min expiry job.
```

If the module doc does not exist, create it from `docs/processes/_TEMPLATE_MODULE.md` with `process: module-marketing-ads`, `module: marketing-ads`, `owns: []`, summary "Ad-platform data backbone and the agent proposal & approval layer for Google Ads, Meta and Sklik.", and the line above under *Processes*.

- [ ] **Step 3: Regenerate and check the index**

```bash
python3 scripts/process-docs/check.py index
python3 scripts/process-docs/check.py check
```

Expected: `wrote docs/processes/INDEX.md (N processes)` then `OK: N process docs` (warnings for unrelated orphans are fine; no `ERROR`).

- [ ] **Step 4: Full backend validation**

```bash
dotnet build-server shutdown
MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_DISABLE_BUILD_SERVERS=1 dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet format Anela.Heblo.sln
git diff --stat
dotnet format Anela.Heblo.sln --verify-no-changes
dotnet test Anela.Heblo.sln --no-build --filter "Category!=Playwright&Category!=Integration"
podman machine start
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "Category=Integration&FullyQualifiedName~MarketingAds"
bash scripts/check-no-managed-tx.sh
```

Expected: build 0 errors; format leaves no diff on the second run (commit what the first run changed); all non-integration tests pass (the same filter CI uses); the MarketingAds integration tests pass; the managed-transaction check passes.

- [ ] **Step 5: Frontend validation** (i18n and the regenerated client changed). In a fresh worktree first run `cd frontend && npm install --legacy-peer-deps`.

```bash
cd frontend && CI=false npm run build && npm run lint && npx react-scripts test --watchAll=false src/utils/__tests__/errorHandler.test.ts; cd ..
```

Expected: build succeeds (the `prebuild` step regenerates the client — commit any further diff), lint has no new errors, the error-handler test passes.

- [ ] **Step 6: Commit the docs and any format/client changes**

```bash
git add docs/processes frontend/src/api/generated/api-client.ts
git add -u backend
git commit -m "docs: process doc for the ad proposal approval flow

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 7: Push and open the PR**

```bash
git push -u origin feature/marketing-ads-c3-proposals
gh pr create --title "feat: marketing ads proposal & approval layer (C3)" --body "$(cat <<'EOF'
## Summary
- Proposal aggregate with immutable versions, approvals, executions; lifecycle state machine; `Revision` concurrency token
- Pure limits engine (spec 6.3) with table-driven tests; autonomy (seeded ProposeOnly); kill switch (seeded disabled)
- Execution Hangfire job: kill switch before every action, ReadCurrent vs OldValue → StaleState, before-state + response stored, PartiallyExecuted
- Revert from stored before-state (web, AdApprovals Admin); expiry recurring job; agent runs; append-only audit enforced by a Postgres trigger
- `IApprovalChannelResolver` contract + deny-all default — **nothing can be approved until C4**
- Adds `Marketing_Ads`, `Marketing_AdApprovals`, `Marketing_AdApprovalsViaMcp` features (no group grants — C4)
- Replaces C2's no-op `IAdExecutionLookup`
- Migration `AddMarketingAdsProposals` (9 tables in `public`, seeds, triggers) — apply manually on staging before deploying

## Test plan
- [ ] `dotnet test` (CI filter) green
- [ ] MarketingAds integration tests green locally (`Category=Integration`)
- [ ] Staging: apply migration, check seeds (6 autonomy, 27 limits, kill switch off)
- [ ] Staging: `GET /api/marketing-ads/settings` as admin returns guardrails; approve returns 403 (deny-all until C4)

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
gh pr checks --watch
```

Expected: PR created; BE/FE/Docker checks green (an empty "no checks reported" right after creation only means they are not registered yet).

---

## Spec deviations

1. **Feature values added in C3, not C4.** The generated `Feature` enum must contain the values C3's controllers reference. C3 adds only the three `access-matrix.json` entries + regeneration; seed groups, grants, agent identity, `azp` channel resolution and MCP tools stay in C4.
2. **Table names are PascalCase in `public`** (`AdProposals`, `AdProposalVersions`, `AdProposalApprovals`, `AdProposalActionExecutions`, `AdAuditEvents`, `AgentRuns`, `AdAutonomySettings`, `AdLimitSettings`, `AdKillSwitches`) following the codebase convention (`StandardizeTableNamingToPascalCase`), instead of the spec's `ad_audit_events` / `agent_runs` / `ad_limit_settings` / `ad_autonomy_settings`.
3. **Platform and action type are stored as enum-name strings** on Domain entities: the Domain project cannot reference the C1 contracts that live in Application.
4. **Extra table `AdProposalActionExecutions`** holds per-action before-state, platform response and revert result (spec 6.2 requires storing them; it names no table). It also feeds the weekly pause limit and `IAdExecutionLookup`.
5. **Append-only trigger also covers `AdProposalVersions`** and blocks `TRUNCATE` (spec: versions are immutable).
6. **`IApprovalChannelResolver.Resolve()`** returns the class `ApprovalChannelResolution { Allowed, Channel, DenyReason }` (the shape requested for C3; a class, not a record). C3 also applies it to revert and settings mutations (Web channel required). C4's draft plan assumed `record ApprovalChannelResolution(bool IsAllowed, ApprovalChannel? Channel, string? DenyReason)` — C4 must adapt to C3's merged shape.
7. **Submissions denied by hard limits are rejected (422) and not stored**; NeedsWebApproval / NeedsSecondApprover proposals are stored.
8. **`Auto` autonomy** (undefined in the spec) auto-approves everything the hard limits do not deny; `AutoWithinLimits` only `Allowed`.
9. **Only the creator may revise**, and a revision resets `expires_at` to now + TTL.
10. **Created-by kind** is `Agent` when the submission names the caller's own running agent run, otherwise `Human`; the audit channel of Submitted/Revised is inferred from it until C4 resolves channels from the token.
11. **Limit defaults not in the spec:** `SecondApproverAbove` seeded equal to the per-proposal maximum (inactive); Meta `AddNegativeKeyword` seeded disabled; an ad the `ads` schema does not know counts as "would empty its ad group".
12. **Kill switch off at execution fails the proposal** (it does not wait for the switch); **revert is not gated by the kill switch** and runs synchronously.
13. **A platform `Failed` result on one action continues with the next**; only stale state, kill switch, missing executor and transport exceptions stop the run.
14. **The expiry job also re-enqueues `Approved` proposals** whose execution never started (lost enqueue); the job claim makes this idempotent.
15. **Error codes use the 39XX range** (`MarketingAds…`).
16. **Migrations:** `CLAUDE.md` says migrations are manual; `docs/development/setup.md` says production applies them at startup. Either way the PR description asks to apply the migration on staging before deploying.
