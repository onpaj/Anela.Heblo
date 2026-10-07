# Marketing Core C4 — Identity, Approval Channel, Groups, MCP Tools Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give AI marketing agents their own non-approving service identity, decide per request which approval channel (Web / MCP / denied) a caller is on, seed the marketing-ads permission groups, rate-limit agents on `/mcp`, and expose the whole ads surface (data, proposals, agent runs, chief approvals) as MCP tools.

**Architecture:** `ICurrentUserService` learns the token's client app (`azp`/`appid`), whether the token is app-only, and whether it is a bearer token. The real `ApprovalChannelResolver` (Application layer) turns that into `Web`, `Mcp` or a denial and replaces C3's deny-all default, so every approve/reject handler C3 wrote gets the rules without changes. `/mcp` gets an ASP.NET fixed-window rate limiter partitioned by `oid` for app-only callers. `MarketingAdsMcpTools` is a thin wrapper over the MediatR handlers C2/C3 shipped. Entra setup is a documented runbook executed by Ondrej, not code.

**Tech Stack:** .NET 8, ASP.NET Core rate limiting (`Microsoft.AspNetCore.RateLimiting`, in the shared framework), Microsoft.Identity.Web, MediatR, ModelContextProtocol.AspNetCore 1.0.0, xUnit 2.9 + Moq + FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-10-07-marketing-agents-platform-design.md` — section 7 (entire) and section 12 (canonical names, binding). Context: `docs/handoff/marketing-agents-platform.md` sections 2–3 (binding).

## Global Constraints

- Prerequisites: PRs **C1, C2, C3 merged on `origin/main`**. C3 already added the features `Marketing_Ads` "Reklamní kampaně" (`hasWrite`), `Marketing_AdApprovals` "Schvalování kampaní" (`hasWrite`, `hasAdmin`), `Marketing_AdApprovalsViaMcp` "Schvalování přes Claude" (read only) to `access-matrix.json`, and the `IApprovalChannelResolver` interface with a deny-all default. This PR does **not** add features.
- Permission strings (generator `ToSnake`): `marketing.ads.read`, `marketing.ads.write`, `marketing.ad_approvals.read`, `marketing.ad_approvals.write`, `marketing.ad_approvals.admin`, `marketing.ad_approvals_via_mcp.read`. Levels are independent (`.write` does not imply `.read`).
- Seed groups (spec 7.2): `Marketing_Agent` (Ads W), `Marketer` gains Ads W, `Marketing_Specialist` (+ AdApprovals W), `Marketing_Chief` (+ AdApprovalsViaMcp R), `Spravce` gains all incl. AdApprovals Admin.
- Channel rules (spec 7.4), in this order: (1) app-only token (no `scp`, or `idtyp = app`) → **deny**, whatever permissions; (2) `azp`/`appid` ∈ `Ads:Approval:WebClientIds` → `Web`, needs `Marketing_AdApprovals` Write; (3) ∈ `Ads:Approval:McpClientIds` (default includes `AzureAd:ClientId`) → `Mcp`, needs AdApprovals Write **and** `Marketing_AdApprovalsViaMcp` Read; (4) cookie session → `Web`; any other bearer client → **deny**.
- `super_user` must never be assigned to an agent identity; even if it is, app-only callers are denied approval.
- `/mcp` rate limit: partitioned by `oid` for app-only callers, `Ads:AgentRateLimitPerMinute`, default **60**.
- MCP tools: file `backend/src/Anela.Heblo.API/MCP/Tools/MarketingAdsMcpTools.cs`, output JSON via `McpJsonOptions.Default`, gates via `EnsureFeatureAccess`, errors as `McpException("[<ErrorCode>] …")`. No tool returns customer PII.
- DTOs are classes, never records (internal domain types may be records). Application `*Response` types inherit `BaseResponse`.
- ADR-005: identity is read only through `ICurrentUserService`; handlers never touch `IHttpContextAccessor`.
- Secrets only in Key Vault (`kv-heblo-stg`, `kv-heblo-prod`, separator `--`). The agent's client secret lives in the agent runtime, never in Heblo.
- New permissions do **not** reach existing prod/staging groups (`JsonGroupSeeder` is insert-if-missing). The rollout grant step is documented and performed by Ondrej.
- Process changes update their `docs/processes/` doc in the same PR; run `python3 scripts/process-docs/check.py index`.
- Conventional commits; every commit message ends with a blank line then `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- dotnet commands (avoid the known build-server deadlock + AccessMatrixGen noise) — always from repo root:
  - Build tests: `DOTNET_CLI_DISABLE_BUILD_SERVERS=1 MSBUILDDISABLENODEREUSE=1 dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -nodeReuse:false -p:UseSharedCompilation=false`
  - Run tests: `DOTNET_CLI_DISABLE_BUILD_SERVERS=1 MSBUILDDISABLENODEREUSE=1 dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "<filter>"`
  - An `AccessMatrixGen` `JsonException` / MSB3073 warning during build is known, non-fatal noise.

## Review Focus

1. **App-only token holding `super_user` and every permission calls `ApproveAdProposal` over MCP** → refused, and the resolver never even consults roles. Pinned in Task 3 (`ApprovalChannelResolverTests` + `TokenToApprovalChannelTests`).
2. **Production SPA client id differs from the repo default** → every web approval would be refused (fail-closed, but it blocks the chief). Pinned by the resolver test "unknown bearer client is denied with a reason naming the client id" (Task 3) and the rollout step that reads `azp` from a real web token (Task 8 runbook).
3. **Client-id lists overridden from Key Vault with different casing, whitespace or duplicates** (the config binder merges arrays index-wise) → still match. Pinned in Task 3 (`Resolve_MatchesClientIdsIgnoringCaseAndWhitespace`).
4. **A looping agent fires hundreds of `/mcp` calls** → HTTP 429 after 60/min for that service principal only; people are never throttled; two agents do not share a bucket. Pinned in Task 4.
5. **An LLM sends malformed dates, `from > to`, a blank reject reason or an empty action list** → `[ValidationError]` McpException, handler never called. Pinned in Tasks 5–7.

## Spec deviations

1. `ICurrentUserService` gets a third member, `IsBearerToken`, besides `ClientAppId` and `IsAppOnly`. Rule 4 needs to tell a cookie session (→ Web) from a bearer token with no client id (→ deny), and `IsAppOnly` must never fire for a cookie session (the web-app OIDC cookie and E2E session also lack `scp`).
2. `ApproveAdProposal` / `RejectAdProposal` / `ListPendingAdProposals` additionally gate on `Marketing_AdApprovals` Write in the tool (defence in depth). The handler + resolver stay the enforcement point (spec 7.4), so REST and MCP behave identically.
3. `Marketing_Specialist`, `Marketing_Chief` and `Spravce` also receive `marketing.ad_approvals.read` (spec table shows "—" for Read). The generator always emits `.read`, levels are independent, and C5 pages need a read gate.
4. MCP date parameters are ISO strings (`yyyy-MM-dd`, ISO-8601 instants) parsed and validated in the tool — no existing MCP tool takes `DateOnly`, and LLM clients produce strings reliably.
5. `Ads:Approval:WebClientIds` defaults to the SPA client id found in the repo (`87193df0-3128-44d2-8673-580e97631a07`, `frontend/.env`); the runbook verifies it against a real token per environment. HebloMCP's client id is not in the repo, so `McpClientIds` defaults to empty (+ `AzureAd:ClientId`, added in code) and HebloMCP's id is added per environment through Key Vault.
6. Grant step: the three new groups are created by the **non-destructive** `scripts/seed-authorization.sh <env>` (insert-if-missing); `Marketer` and `Spravce` get their new roles at `/admin/access`. `--reset-group` is explicitly forbidden.

---

## Before you start

- [ ] **Read** `CLAUDE.md`; spec sections 7 and 12; handoff sections 2–3; `docs/integrations/mcp-server.md`; `docs/processes/calc-permission-resolution.md`; `memory/patterns/adding-a-new-permission.md`; `docs/architecture/development_guidelines.md` (ADR-005, DTO rules).

- [ ] **Verify prerequisites on origin/main** (stop and report to Ondrej if any check fails — do not build C1–C3 pieces yourself):

```bash
git fetch origin main
git show origin/main:access-matrix.json | grep -cE '"key": "Marketing_(Ads|AdApprovals|AdApprovalsViaMcp)"'
git grep -n "interface IApprovalChannelResolver" origin/main -- backend/src
git ls-tree -r --name-only origin/main backend/src/Anela.Heblo.Application/Features/MarketingAds | grep -cE "Request\.cs$"
```

Expected: `3`; exactly one match; a count ≥ 15.

- [ ] **Create the branch**

```bash
git switch -c feature/marketing-core-c4-identity-mcp origin/main
```

- [ ] **Map the C2/C3 contract.** This plan was written before C2/C3 existed. It uses the *expected* names below. List what really exists and keep a mapping table (it goes into the PR description under "C2/C3 handler mapping"):

```bash
grep -rhoE "public (sealed )?class \w+Request\b[^{]*" backend/src/Anela.Heblo.Application/Features/MarketingAds | sort
grep -rn "IApprovalChannelResolver\|ApprovalChannelResolution\|enum ApprovalChannel" backend/src backend/test
grep -rn "enum AdProposalStatus\|enum AgentRunStatus\|enum AdChangeOrigin\|enum BlendedGranularity\|class AdActionInputDto\|class AdProposalSummaryDto" backend/src
grep -rn "IApprovalChannelResolver" backend/src/Anela.Heblo.Application/Features/MarketingAds --include='*Handler.cs'
```

Expected contract (what the code in this plan compiles against):

| Tool | Request (expected) | Request properties used | Gate |
|---|---|---|---|
| `ListAdAccounts` | `GetAdAccountsRequest` | `AdPlatform? Platform` | Ads R |
| `GetAdPerformance` | `GetAdPerformanceRequest` | `AdPlatform Platform`, `string AccountExternalId`, `AdEntityLevel Level`, `DateOnly From`, `DateOnly To`, `string? ParentExternalId`, `int PageNumber`, `int PageSize` | Ads R |
| `GetAdSearchTerms` | `GetAdSearchTermsRequest` | `Platform`, `AccountExternalId`, `AdEntityLevel ParentLevel`, `string ParentExternalId`, `From`, `To`, `PageNumber`, `PageSize` | Ads R |
| `GetBlendedPerformance` | `GetBlendedPerformanceRequest` | `DateOnly From`, `DateOnly To`, `BlendedGranularity Granularity` (`Daily`, `Monthly`) | Ads R |
| `GetAdChangeHistory` | `GetAdChangeHistoryRequest` | `AdPlatform? Platform`, `string? AccountExternalId`, `DateTimeOffset? Since`, `DateTimeOffset? Until`, `AdChangeOrigin? Origin`, `AdChangeActorKind? ActorKind`, `PageNumber`, `PageSize` | Ads R |
| `GetAdGuardrails` | `GetAdGuardrailsRequest` | — | Ads R |
| `SubmitAdProposal` | `SubmitAdProposalRequest` | `List<AdActionInputDto> Actions`, `string Reasoning`, `Guid? AgentRunId` | Ads W |
| `ReviseAdProposal` | `ReviseAdProposalRequest` | `Guid ProposalId`, `int ExpectedVersion`, `Actions`, `Reasoning` | Ads W |
| `ListAdProposals` | `GetAdProposalsRequest` | `AdProposalStatus? Status`, `bool OnlyMine`, `PageNumber`, `PageSize` | Ads R |
| `GetAdProposal` | `GetAdProposalRequest` | `Guid ProposalId` | Ads R |
| `StartAgentRun` | `StartAgentRunRequest` | `string AgentName`, `string? Purpose` | Ads W |
| `FinishAgentRun` | `FinishAgentRunRequest` | `Guid AgentRunId`, `AgentRunStatus Status`, `string? Summary`, `List<string>? DataScopesRead`, `long? TokensUsed`, `decimal? CostCzk` | Ads W |
| `ListPendingAdProposals` | `GetAdProposalsRequest` (`Status = Pending`) | response `Items` of `AdProposalSummaryDto`: `Id`, `CurrentVersion`, `Platform`, `AccountExternalId`, `ActionType`, `ActionCount`, `CreatedByPrincipal`, `CreatedByKind`, `CreatedAt`, `ExpiresAt`, `List<string> DiffLines`; `TotalCount` | AdApprovals W |
| `ApproveAdProposal` | `ApproveAdProposalRequest` | `Guid ProposalId`, `int Version` | AdApprovals W + handler channel rules |
| `RejectAdProposal` | `RejectAdProposalRequest` | `Guid ProposalId`, `int Version`, `string Reason` | AdApprovals W + handler channel rules |

Expected C3 resolver contract (C3's plan puts it in `Application/Features/MarketingAds/Proposals/Approval/` — `ApprovalChannelResolution.cs`, `IApprovalChannelResolver.cs`, `DenyAllApprovalChannelResolver.cs` — and the `ApprovalChannel` enum in `Domain/Features/MarketingAds/`). This plan uses C3's namespace `Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval` (verify it on main) and adds `using Anela.Heblo.Domain.Features.MarketingAds;` where `ApprovalChannel` is used:

```csharp
public enum ApprovalChannel { Web = 1, Mcp = 2, System = 3 }   // C3 plan: Domain/Features/MarketingAds
public interface IApprovalChannelResolver { ApprovalChannelResolution Resolve(); }
public sealed class ApprovalChannelResolution   // defined by C3 — do not redefine
{
    public bool Allowed { get; init; }
    public ApprovalChannel Channel { get; init; }   // meaningful only when Allowed
    public string? DenyReason { get; init; }
    public static ApprovalChannelResolution Allow(ApprovalChannel channel);
    public static ApprovalChannelResolution Deny(string reason);
}
```

Adaptation rules:
- C3's plan places use cases under `Application/Features/MarketingAds/Proposals/UseCases/<UseCase>/`, so expect namespaces `Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.<UseCase>` for proposal/agent-run/guardrail requests; fix the `using` lines accordingly.
- Every tool returns the **whole** handler response serialised (like `PricingSimulatorMcpTools`), so only *request* property names and namespaces need adapting. Rename the identifiers in this plan's code to the real ones; keep tool names, parameters, gates and descriptions.
- If C3's resolver contract differs, **keep C3's types** and adapt only the `Allow`/`Deny` construction in `ApprovalChannelResolver` and the accessors in the tests (`Allowed`, `Channel`, `DenyReason`). The test cases stay identical in meaning.
- If C3's pending-list summary has no rendered diff, add a `List<string> DiffLines` to C3's summary DTO and fill it in C3's list handler with C3's existing diff renderer (one renderer call per item, no extra queries). Do not call `GetAdProposal` per item (N+1).
- If the last grep shows that C3's approve/reject handlers do **not** call `IApprovalChannelResolver`, stop and report to Ondrej: C3 is incomplete and C4 must not paper over it.
- If a request type for a tool does not exist at all, stop and report — do not invent handlers.

---

## File map

| File | Responsibility |
|---|---|
| `access-matrix.json` (modify) | three new seed groups, new roles for `Marketer`, `Spravce` |
| `access-matrix-entra.generated.json` (regenerate) | generated; groups section changes |
| `backend/test/Anela.Heblo.Tests/Authorization/AccessMatrixJsonTests.cs` (modify) | pins the seed-group grants |
| `backend/src/Anela.Heblo.Domain/Features/Users/ICurrentUserService.cs` (modify) | `ClientAppId`, `IsAppOnly`, `IsBearerToken` |
| `backend/src/Anela.Heblo.API/Infrastructure/Authentication/TokenClientClaims.cs` (create) | pure claim/header readers shared by `CurrentUserService` and the rate limiter |
| `backend/src/Anela.Heblo.API/Features/Users/CurrentUserService.cs` (modify) | implements the three members |
| `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialCrudHandlerTests.cs` (modify) | `StubCurrentUserService` implements new members |
| `backend/test/Anela.Heblo.Tests/Features/Users/CurrentUserServiceClientClaimsTests.cs` (create) | token claim tests |
| `backend/src/Anela.Heblo.Application/Features/MarketingAds/Approvals/ApprovalChannelOptions.cs` (create) | `Ads:Approval` options |
| `backend/src/Anela.Heblo.Application/Features/MarketingAds/Approvals/ApprovalChannelResolver.cs` (create) | spec 7.4 rules |
| `backend/src/Anela.Heblo.Application/Features/MarketingAds/Approvals/ApprovalChannelRegistration.cs` (create) | options binding + replaces C3's deny-all registration |
| `backend/src/Anela.Heblo.Application/Features/MarketingAds/MarketingAdsModule.cs` (modify) | calls `AddApprovalChannel` |
| `backend/test/Anela.Heblo.Tests/Features/MarketingAds/Approvals/ApprovalChannelResolverTests.cs` (create) | table-driven rules |
| `backend/test/Anela.Heblo.Tests/Features/MarketingAds/Approvals/TokenToApprovalChannelTests.cs` (create) | real claims → real `CurrentUserService` → real resolver |
| `backend/test/Anela.Heblo.Tests/Features/MarketingAds/Approvals/ApprovalChannelRegistrationTests.cs` (create) | DI replacement + binding |
| `backend/src/Anela.Heblo.API/MCP/McpAgentRateLimiting.cs` (create) | rate-limit policy + partitioner |
| `backend/src/Anela.Heblo.API/Program.cs`, `Extensions/ApplicationBuilderExtensions.cs` (modify) | register + use the limiter on `/mcp` |
| `backend/test/Anela.Heblo.Tests/MCP/McpAgentRateLimitingTests.cs` (create) | partition + limit tests |
| `backend/src/Anela.Heblo.API/MCP/Tools/MarketingAdsMcpTools.cs` (create) | 15 tools |
| `backend/src/Anela.Heblo.API/MCP/McpModule.cs` (modify) | `.WithTools<MarketingAdsMcpTools>()` |
| `backend/test/Anela.Heblo.Tests/MCP/Tools/MarketingAdsMcpTools*Tests.cs` (create, 4 files) | tool tests |
| `backend/src/Anela.Heblo.API/appsettings.json` (modify) | `Ads:Approval`, `Ads:AgentRateLimitPerMinute` |
| `docs/integrations/marketing-agents-identity.md` (create) | Entra runbook, grant step, token, connection |
| `docs/integrations/mcp-server.md`, `CLAUDE.md`, `docs/processes/calc-permission-resolution.md` (modify) | tool list/count, permission doc |

---

## Task 1: Seed groups and grants in the access matrix

**Files:**
- Modify: `access-matrix.json` (`seedGroups`)
- Regenerate: `access-matrix-entra.generated.json`
- Test: `backend/test/Anela.Heblo.Tests/Authorization/AccessMatrixJsonTests.cs`
- Modify: `docs/processes/calc-permission-resolution.md`

**Interfaces:**
- Consumes: C3's three feature entries.
- Produces: seed groups `Marketing_Agent`, `Marketing_Specialist`, `Marketing_Chief`; role strings listed in Global Constraints (used by Task 8's runbook).

- [ ] **Step 1: Write the failing tests** — append inside class `AccessMatrixJsonTests` (before the final `}`):

```csharp
    private const string AdsRead = "marketing.ads.read";
    private const string AdsWrite = "marketing.ads.write";
    private const string ApprovalsRead = "marketing.ad_approvals.read";
    private const string ApprovalsWrite = "marketing.ad_approvals.write";
    private const string ApprovalsAdmin = "marketing.ad_approvals.admin";
    private const string ApproveViaMcp = "marketing.ad_approvals_via_mcp.read";

    public static TheoryData<string, string[]> MarketingAdsSeedGroupRoles => new()
    {
        { "Marketing_Agent", new[] { AdsRead, AdsWrite } },
        { "Marketer", new[] { AdsRead, AdsWrite } },
        { "Marketing_Specialist", new[] { AdsRead, AdsWrite, ApprovalsRead, ApprovalsWrite } },
        { "Marketing_Chief", new[] { AdsRead, AdsWrite, ApprovalsRead, ApprovalsWrite, ApproveViaMcp } },
        { "Spravce", new[] { AdsRead, AdsWrite, ApprovalsRead, ApprovalsWrite, ApprovalsAdmin, ApproveViaMcp } },
    };

    [Theory]
    [MemberData(nameof(MarketingAdsSeedGroupRoles))]
    public void MarketingAds_SeedGroups_HoldExactlyTheAgreedAdsRoles(string groupName, string[] expected)
    {
        var m = LoadManifest();

        var group = m.SeedGroups.SingleOrDefault(g => g.Name == groupName);

        group.Should().NotBeNull($"seed group '{groupName}' must exist");
        group!.Roles.Where(r => r.StartsWith("marketing.ad", StringComparison.Ordinal))
            .Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void MarketingAgent_SeedGroup_HoldsNothingButAdsReadAndWrite()
    {
        var m = LoadManifest();

        var agent = m.SeedGroups.Single(g => g.Name == "Marketing_Agent");

        agent.Roles.Should().BeEquivalentTo(new[] { AdsRead, AdsWrite });
    }

    [Fact]
    public void OnlySpravce_HoldsAdApprovalsAdmin()
    {
        var m = LoadManifest();

        var holders = m.SeedGroups.Where(g => g.Roles.Contains(ApprovalsAdmin)).Select(g => g.Name);

        holders.Should().BeEquivalentTo(new[] { "Spravce" });
    }

    [Fact]
    public void EveryGroupThatMayApproveViaMcp_AlsoHoldsWebApprovalWrite()
    {
        var m = LoadManifest();

        var violators = m.SeedGroups
            .Where(g => g.Roles.Contains(ApproveViaMcp) && !g.Roles.Contains(ApprovalsWrite))
            .Select(g => g.Name)
            .ToList();

        violators.Should().BeEmpty("approve-via-MCP requires Marketing_AdApprovals Write as well (spec 7.2)");
    }
```

- [ ] **Step 2: Build and run — expect failure**

Run the build command, then tests with `--filter "FullyQualifiedName~AccessMatrixJsonTests"`.
Expected: FAIL — `seed group 'Marketing_Agent' must exist` and Marketer/Spravce assertions fail; the existing tests pass.

- [ ] **Step 3: Edit `access-matrix.json`** with a guarded script (each anchor must match exactly once; if an assertion fires, C3 changed those lines — inspect and adapt the anchor, do not loosen the assertion):

```bash
python3 - <<'EOF'
import pathlib
p = pathlib.Path("access-matrix.json")
s = p.read_text(encoding="utf-8")

def once(old, new):
    global s
    n = s.count(old)
    assert n == 1, f"expected exactly one match, got {n}: {old[:70]}"
    s = s.replace(old, new)

# Spravce: everything incl. admin and approve-via-MCP
once('"marketing.performance.read", "marketing.performance.write",',
     '"marketing.performance.read", "marketing.performance.write", "marketing.ads.read", "marketing.ads.write", '
     '"marketing.ad_approvals.read", "marketing.ad_approvals.write", "marketing.ad_approvals.admin", '
     '"marketing.ad_approvals_via_mcp.read",')

# Marketer gains Ads W, and the three new groups follow it
once('"marketing.performance.read", "anela.process_docs.read"] },',
     '"marketing.performance.read", "marketing.ads.read", "marketing.ads.write", "anela.process_docs.read"] },\n'
     '    { "name": "Marketing_Agent", "roles": ["marketing.ads.read", "marketing.ads.write"] },\n'
     '    { "name": "Marketing_Specialist", "roles": ["marketing.ads.read", "marketing.ads.write", '
     '"marketing.ad_approvals.read", "marketing.ad_approvals.write"] },\n'
     '    { "name": "Marketing_Chief", "roles": ["marketing.ads.read", "marketing.ads.write", '
     '"marketing.ad_approvals.read", "marketing.ad_approvals.write", "marketing.ad_approvals_via_mcp.read"] },')

p.write_text(s, encoding="utf-8")
print("ok")
EOF
python3 -c "import json; json.load(open('access-matrix.json')); print('valid json')"
```

Expected: `ok`, `valid json`. `Marketing_Agent` deliberately lacks `anela.process_docs.read` (agents get nothing beyond ads).

- [ ] **Step 4: Regenerate the artifacts**

```bash
dotnet run --project backend/tools/Anela.Heblo.AccessMatrixGen -- access-matrix.json \
  backend/src/Anela.Heblo.Domain/Features/Authorization/Feature.generated.cs \
  backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrix.generated.cs \
  backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs \
  frontend/src/auth/accessMatrix.generated.ts \
  access-matrix-entra.generated.json
git status --short
```

Expected: `Wrote …`; `git status` lists only `access-matrix.json` and `access-matrix-entra.generated.json` (seed groups feed only the Entra manifest). If `frontend/src/auth/accessMatrix.generated.ts` also changed, C3 forgot to regenerate: keep the change and run `cd frontend && npm install --legacy-peer-deps && CI=false npm run build && npm run lint` before committing.

- [ ] **Step 5: Run the tests — expect pass**

Build, then `--filter "FullyQualifiedName~AccessMatrixJsonTests"`. Expected: all pass (including `SeedGroup_Roles_AreValidPermissionStrings`, which proves the new strings exist as features with the right levels).

- [ ] **Step 6: Update `docs/processes/calc-permission-resolution.md`.** Get the real numbers:

```bash
python3 - <<'EOF'
import json
d = json.load(open('access-matrix.json'))
total = sum(1 + bool(f.get('hasWrite')) + bool(f.get('hasAdmin')) for f in d['features'])
print('features', len(d['features']), 'permissions', total, 'groups', len(d['seedGroups']))
for g in sorted(d['seedGroups'], key=lambda g: -len(g['roles'])):
    print(g['name'], len(g['roles']))
EOF
```

Then edit the doc:
- Configuration table: `features[]` row → the printed feature count; `seedGroups[]` row → `16 groups`.
- "Seed groups" paragraph: rewrite the counts from the script output and add `Marketing_Chief`, `Marketing_Specialist`, `Marketing_Agent` (with "ads read/write only — for agent service identities").
- Data-flow step A3 ("**70** permission strings (41 `.read`, …)"): replace with the script's numbers (`.admin` now also includes `marketing.ad_approvals.admin`).
- Add to **Known quirks**:

```markdown
- **Agent service identities are ordinary AppUsers.** A client-credentials (app-only) token is
  materialised on its first call like a person — `Email` and `DisplayName` are the service
  principal's `oid`, because app tokens carry no name claims — and gets permissions only from its
  groups. Only `Marketing_Agent` belongs on it. `ICurrentUserService.IsAppOnly` marks such calls,
  and the ad-proposal approve/reject paths refuse them whatever they hold, even `super_user`.
  Runbook: `docs/integrations/marketing-agents-identity.md`.
```

- [ ] **Step 7: Commit**

```bash
git add access-matrix.json access-matrix-entra.generated.json backend/test/Anela.Heblo.Tests/Authorization/AccessMatrixJsonTests.cs docs/processes/calc-permission-resolution.md
git commit -m "feat: seed marketing ads permission groups

Adds Marketing_Agent, Marketing_Specialist and Marketing_Chief seed groups and
grants the ads roles to Marketer and Spravce (spec 7.2).

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 2: `ICurrentUserService` — client app id, app-only, bearer

**Files:**
- Create: `backend/src/Anela.Heblo.API/Infrastructure/Authentication/TokenClientClaims.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Users/ICurrentUserService.cs`
- Modify: `backend/src/Anela.Heblo.API/Features/Users/CurrentUserService.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialCrudHandlerTests.cs` (and any other hand-written implementation)
- Test: `backend/test/Anela.Heblo.Tests/Features/Users/CurrentUserServiceClientClaimsTests.cs`

**Interfaces:**
- Produces:
  - `string? ICurrentUserService.ClientAppId { get; }` — `azp` (v2) else `appid` (v1), null when absent.
  - `bool ICurrentUserService.IsAppOnly { get; }` — authenticated **bearer** request whose token has `idtyp = app` or no `scp` claim.
  - `bool ICurrentUserService.IsBearerToken { get; }` — authenticated request with an `Authorization: Bearer …` header.
  - `static class TokenClientClaims` (`Anela.Heblo.API.Infrastructure.Authentication`): `string? GetClientAppId(ClaimsPrincipal?)`, `bool IsBearerRequest(HttpContext?)`, `bool IsAppOnly(HttpContext?)` — Task 4 reuses `IsAppOnly`.

- [ ] **Step 1: Write the failing tests** — create `CurrentUserServiceClientClaimsTests.cs`:

```csharp
using System.Security.Claims;
using Anela.Heblo.API.Features.Users;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Users;

public class CurrentUserServiceClientClaimsTests
{
    private const string WebClientId = "87193df0-3128-44d2-8673-580e97631a07";
    private const string ApiClientId = "8b34be89-f86f-422f-af40-7dbcd30cb66a";
    private const string AgentClientId = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string Bearer = "Bearer eyJ0eXAi.fake.token";
    private const string MappedScopeClaim = "http://schemas.microsoft.com/identity/claims/scope";

    private static CurrentUserService CreateService(ClaimsPrincipal principal, string? authorizationHeader)
    {
        var httpContext = new DefaultHttpContext { User = principal };
        if (authorizationHeader is not null)
        {
            httpContext.Request.Headers.Authorization = authorizationHeader;
        }

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);
        return new CurrentUserService(accessor.Object);
    }

    private static ClaimsPrincipal Authenticated(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "TestAuth", "name", "roles"));

    [Fact]
    public void V2DelegatedToken_ExposesAzp_AndIsNotAppOnly()
    {
        var service = CreateService(
            Authenticated(new Claim("azp", WebClientId), new Claim("scp", "access_as_user")), Bearer);

        service.ClientAppId.Should().Be(WebClientId);
        service.IsAppOnly.Should().BeFalse();
        service.IsBearerToken.Should().BeTrue();
    }

    [Fact]
    public void V1DelegatedToken_FallsBackToAppId()
    {
        var service = CreateService(
            Authenticated(new Claim("appid", ApiClientId), new Claim("scp", "access_as_user")), Bearer);

        service.ClientAppId.Should().Be(ApiClientId);
        service.IsAppOnly.Should().BeFalse();
    }

    [Fact]
    public void AzpWins_WhenBothAzpAndAppIdArePresent()
    {
        var service = CreateService(
            Authenticated(new Claim("azp", WebClientId), new Claim("appid", ApiClientId), new Claim("scp", "x")), Bearer);

        service.ClientAppId.Should().Be(WebClientId);
    }

    [Fact]
    public void MappedScopeClaimType_CountsAsDelegated()
    {
        var service = CreateService(
            Authenticated(new Claim("azp", WebClientId), new Claim(MappedScopeClaim, "access_as_user")), Bearer);

        service.IsAppOnly.Should().BeFalse();
    }

    [Fact]
    public void ClientCredentialsToken_WithoutScp_IsAppOnly_EvenWithSuperUserRole()
    {
        var service = CreateService(
            Authenticated(
                new Claim("azp", AgentClientId),
                new Claim("oid", "agent-sp-oid"),
                new Claim("roles", "Marketing.Agent"),
                new Claim("roles", "super_user")),
            Bearer);

        service.IsAppOnly.Should().BeTrue();
        service.ClientAppId.Should().Be(AgentClientId);
    }

    [Fact]
    public void IdtypApp_IsAppOnly()
    {
        var service = CreateService(
            Authenticated(new Claim("azp", AgentClientId), new Claim("idtyp", "app")), Bearer);

        service.IsAppOnly.Should().BeTrue();
    }

    [Fact]
    public void CookieSession_WithoutScp_IsNeitherAppOnlyNorBearer()
    {
        // Web-app OIDC cookie / E2E cookie session: no Authorization header.
        var service = CreateService(Authenticated(new Claim("oid", "user-oid")), authorizationHeader: null);

        service.IsAppOnly.Should().BeFalse();
        service.IsBearerToken.Should().BeFalse();
        service.ClientAppId.Should().BeNull();
    }

    [Fact]
    public void UnauthenticatedRequest_WithBearerHeader_IsNeitherAppOnlyNorBearer()
    {
        var service = CreateService(new ClaimsPrincipal(new ClaimsIdentity()), Bearer);

        service.IsAppOnly.Should().BeFalse();
        service.IsBearerToken.Should().BeFalse();
    }

    [Fact]
    public void NonBearerAuthorizationScheme_IsNotBearer()
    {
        var service = CreateService(Authenticated(new Claim("oid", "user-oid")), "Basic dXNlcjpwYXNz");

        service.IsBearerToken.Should().BeFalse();
        service.IsAppOnly.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Build — expect failure**

Run the build command. Expected: `error CS1061: 'CurrentUserService' does not contain a definition for 'ClientAppId'`.

- [ ] **Step 3: Extend the interface** — replace `ICurrentUserService.cs` with:

```csharp
namespace Anela.Heblo.Domain.Features.Users;

public interface ICurrentUserService
{
    CurrentUser GetCurrentUser();
    bool IsInRole(string role);

    /// <summary>Client application that obtained the caller's token: <c>azp</c> (v2) or
    /// <c>appid</c> (v1). Null when the token carries neither (cookie sessions, anonymous).</summary>
    string? ClientAppId { get; }

    /// <summary>True for an authenticated bearer request whose token was issued to an
    /// application itself (client credentials): <c>idtyp = app</c> or no <c>scp</c> claim.
    /// Such callers are service identities and may never approve anything.</summary>
    bool IsAppOnly { get; }

    /// <summary>True when the request authenticated with an <c>Authorization: Bearer</c>
    /// header; false for cookie sessions (web-app login, E2E session).</summary>
    bool IsBearerToken { get; }
}
```

- [ ] **Step 4: Create `TokenClientClaims.cs`**

```csharp
using System.Security.Claims;

namespace Anela.Heblo.API.Infrastructure.Authentication;

/// <summary>Reads which client application obtained the caller's token and whether the token is
/// app-only. Pure functions over the request, shared by CurrentUserService and the /mcp rate limiter.</summary>
public static class TokenClientClaims
{
    public const string AuthorizedPartyClaim = "azp";
    public const string AppIdClaim = "appid";
    public const string IdentityTypeClaim = "idtyp";
    public const string ScopeClaim = "scp";
    public const string MappedScopeClaim = "http://schemas.microsoft.com/identity/claims/scope";

    private const string AppIdentityType = "app";
    private const string BearerPrefix = "Bearer ";

    public static string? GetClientAppId(ClaimsPrincipal? user) =>
        NonBlank(user?.FindFirst(AuthorizedPartyClaim)?.Value)
        ?? NonBlank(user?.FindFirst(AppIdClaim)?.Value);

    public static bool IsBearerRequest(HttpContext? httpContext)
    {
        if (httpContext?.User?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var header = httpContext.Request.Headers.Authorization.ToString();
        return header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAppOnly(HttpContext? httpContext)
    {
        if (!IsBearerRequest(httpContext))
        {
            return false;
        }

        var user = httpContext!.User;
        var identityType = user.FindFirst(IdentityTypeClaim)?.Value;
        if (string.Equals(identityType, AppIdentityType, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !user.HasClaim(c => c.Type is ScopeClaim or MappedScopeClaim);
    }

    private static string? NonBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
```

- [ ] **Step 5: Implement in `CurrentUserService.cs`** — add `using Anela.Heblo.API.Infrastructure.Authentication;` and, after `IsInRole`:

```csharp
    public string? ClientAppId => TokenClientClaims.GetClientAppId(_httpContextAccessor.HttpContext?.User);

    public bool IsAppOnly => TokenClientClaims.IsAppOnly(_httpContextAccessor.HttpContext);

    public bool IsBearerToken => TokenClientClaims.IsBearerRequest(_httpContextAccessor.HttpContext);
```

- [ ] **Step 6: Update every hand-written implementation.** Find them:

```bash
grep -rnE "class \w+\s*:\s*[^{]*ICurrentUserService" backend --include='*.cs' | grep -v /obj/
```

For each test stub (e.g. `StubCurrentUserService` in `PackingMaterialCrudHandlerTests.cs`, and any stub C2/C3 added) add the members returning the safe interactive defaults:

```csharp
    public string? ClientAppId => null;
    public bool IsAppOnly => false;
    public bool IsBearerToken => false;
```

- [ ] **Step 7: Build and run — expect pass**

Build, then `--filter "FullyQualifiedName~CurrentUserService"`. Expected: all pass (the new class plus the existing `CurrentUserServiceTests` / `CurrentUserServiceIsInRoleTests`).

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Users/ICurrentUserService.cs backend/src/Anela.Heblo.API/Infrastructure/Authentication/TokenClientClaims.cs backend/src/Anela.Heblo.API/Features/Users/CurrentUserService.cs backend/test
git commit -m "feat: expose token client app and app-only flag on ICurrentUserService

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
## Task 3: The real `ApprovalChannelResolver`

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingAds/Approvals/ApprovalChannelOptions.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingAds/Approvals/ApprovalChannelResolver.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingAds/Approvals/ApprovalChannelRegistration.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/MarketingAds/MarketingAdsModule.cs`
- Modify: `backend/src/Anela.Heblo.API/appsettings.json`
- Delete (if only used as placeholder): C3's deny-all resolver class and its test
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingAds/Approvals/ApprovalChannelResolverTests.cs`, `TokenToApprovalChannelTests.cs`, `ApprovalChannelRegistrationTests.cs`

Put the three new files in the folder where C3 put `IApprovalChannelResolver` (C3 plan: `Application/Features/MarketingAds/Proposals/Approval/`) (namespace `Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval`, used below); tests then go to the matching `backend/test/Anela.Heblo.Tests/Features/MarketingAds/Proposals/Approval/` folder. `ApprovalChannel` lives in `Anela.Heblo.Domain.Features.MarketingAds` — add that `using` to the resolver and test files.

**Interfaces:**
- Consumes: Task 2's `ICurrentUserService.ClientAppId/IsAppOnly/IsBearerToken`; C3's `IApprovalChannelResolver`, `ApprovalChannelResolution`, `ApprovalChannel`.
- Produces:
  - `sealed class ApprovalChannelOptions { const string SectionName = "Ads:Approval"; const string ApiClientIdConfigKey = "AzureAd:ClientId"; List<string> WebClientIds; List<string> McpClientIds; string? ApiClientId; }`
  - `sealed class ApprovalChannelResolver : IApprovalChannelResolver` (ctor `ICurrentUserService, IOptions<ApprovalChannelOptions>`).
  - `static IServiceCollection AddApprovalChannel(this IServiceCollection, IConfiguration)` — binds options and **replaces** any existing `IApprovalChannelResolver` registration with a scoped `ApprovalChannelResolver`.

- [ ] **Step 1: Write the table-driven resolver tests** — `ApprovalChannelResolverTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Approval;

public class ApprovalChannelResolverTests
{
    private const string WebClient = "web-spa-client";
    private const string HebloMcpClient = "heblo-mcp-client";
    private const string ApiClient = "api-client";
    private const string AgentClient = "agent-client";
    private const string AllRoles = "*";

    private static readonly string ApproveWrite = AccessRoles.For(Feature.Marketing_AdApprovals, AccessLevel.Write);
    private static readonly string ApproveViaMcp = AccessRoles.For(Feature.Marketing_AdApprovalsViaMcp, AccessLevel.Read);

    public sealed record ChannelCase(
        string Name,
        bool IsAuthenticated,
        bool IsAppOnly,
        bool IsBearerToken,
        string? ClientAppId,
        string[] GrantedRoles,
        bool ExpectedAllowed,
        ApprovalChannel? ExpectedChannel)
    {
        public override string ToString() => Name;
    }

    public static IEnumerable<object[]> Cases() => new[]
    {
        new ChannelCase("web SPA token with approve write", true, false, true, WebClient, new[] { ApproveWrite }, true, ApprovalChannel.Web),
        new ChannelCase("web SPA token without approve write", true, false, true, WebClient, Array.Empty<string>(), false, null),
        new ChannelCase("web SPA token with only approve-via-MCP", true, false, true, WebClient, new[] { ApproveViaMcp }, false, null),
        new ChannelCase("Claude connector (API client id) with write + via-MCP", true, false, true, ApiClient, new[] { ApproveWrite, ApproveViaMcp }, true, ApprovalChannel.Mcp),
        new ChannelCase("Claude connector with write but no via-MCP", true, false, true, ApiClient, new[] { ApproveWrite }, false, null),
        new ChannelCase("Claude connector with via-MCP but no write", true, false, true, ApiClient, new[] { ApproveViaMcp }, false, null),
        new ChannelCase("HebloMCP client with write + via-MCP", true, false, true, HebloMcpClient, new[] { ApproveWrite, ApproveViaMcp }, true, ApprovalChannel.Mcp),
        new ChannelCase("service principal with all permissions still denied", true, true, true, AgentClient, new[] { AllRoles }, false, null),
        new ChannelCase("super_user role still denied for app-only on the web client id", true, true, true, WebClient, new[] { AllRoles }, false, null),
        new ChannelCase("super_user role still denied for app-only on the API client id", true, true, true, ApiClient, new[] { AllRoles }, false, null),
        new ChannelCase("cookie session with approve write", true, false, false, null, new[] { ApproveWrite }, true, ApprovalChannel.Web),
        new ChannelCase("cookie session without approve write", true, false, false, null, Array.Empty<string>(), false, null),
        new ChannelCase("bearer token from an unknown client with all permissions", true, false, true, "some-other-client", new[] { AllRoles }, false, null),
        new ChannelCase("bearer token without any client id", true, false, true, null, new[] { AllRoles }, false, null),
        new ChannelCase("unauthenticated caller", false, false, false, null, new[] { AllRoles }, false, null),
    }.Select(c => new object[] { c });

    private static Mock<ICurrentUserService> CurrentUser(ChannelCase c)
    {
        var mock = new Mock<ICurrentUserService>();
        mock.Setup(s => s.GetCurrentUser()).Returns(new CurrentUser("oid-1", "Someone", null, c.IsAuthenticated));
        mock.SetupGet(s => s.IsAppOnly).Returns(c.IsAppOnly);
        mock.SetupGet(s => s.IsBearerToken).Returns(c.IsBearerToken);
        mock.SetupGet(s => s.ClientAppId).Returns(c.ClientAppId);
        mock.Setup(s => s.IsInRole(It.IsAny<string>()))
            .Returns<string>(role => c.GrantedRoles.Contains(AllRoles) || c.GrantedRoles.Contains(role));
        return mock;
    }

    private static ApprovalChannelResolver CreateResolver(ICurrentUserService currentUser, ApprovalChannelOptions? options = null) =>
        new(currentUser, Options.Create(options ?? new ApprovalChannelOptions
        {
            WebClientIds = new List<string> { WebClient },
            McpClientIds = new List<string> { HebloMcpClient },
            ApiClientId = ApiClient,
        }));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Resolve_AppliesChannelRules(ChannelCase c)
    {
        // Arrange
        var resolver = CreateResolver(CurrentUser(c).Object);

        // Act
        var result = resolver.Resolve();

        // Assert
        result.Allowed.Should().Be(c.ExpectedAllowed);
        if (c.ExpectedChannel is { } expectedChannel)
        {
            result.Channel.Should().Be(expectedChannel);
        }
        if (!c.ExpectedAllowed)
        {
            result.DenyReason.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void Resolve_DeniesAppOnly_WithoutEverConsultingRoles()
    {
        // Arrange
        var c = new ChannelCase("app-only", true, true, true, WebClient, new[] { AllRoles }, false, null);
        var currentUser = CurrentUser(c);

        // Act
        var result = CreateResolver(currentUser.Object).Resolve();

        // Assert
        result.Allowed.Should().BeFalse();
        currentUser.Verify(s => s.IsInRole(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Resolve_MatchesClientIdsIgnoringCaseAndWhitespace()
    {
        // Arrange — config overridden from Key Vault with odd casing, padding and a duplicate
        var c = new ChannelCase("padded", true, false, true, WebClient, new[] { ApproveWrite }, true, ApprovalChannel.Web);
        var options = new ApprovalChannelOptions
        {
            WebClientIds = new List<string> { "  WEB-SPA-CLIENT ", "web-spa-client", "" },
            McpClientIds = new List<string>(),
            ApiClientId = null,
        };

        // Act
        var result = CreateResolver(CurrentUser(c).Object, options).Resolve();

        // Assert
        result.Allowed.Should().BeTrue();
        result.Channel.Should().Be(ApprovalChannel.Web);
    }

    [Fact]
    public void Resolve_DeniesWebBearerApproval_WhenNoWebClientIdsConfigured_AndNamesTheClient()
    {
        // Arrange — production misconfiguration: the SPA id was never set
        var c = new ChannelCase("unconfigured", true, false, true, WebClient, new[] { AllRoles }, false, null);
        var options = new ApprovalChannelOptions { ApiClientId = ApiClient };

        // Act
        var result = CreateResolver(CurrentUser(c).Object, options).Resolve();

        // Assert
        result.Allowed.Should().BeFalse();
        result.DenyReason.Should().Contain(WebClient);
    }

    [Fact]
    public void Resolve_TreatsApiClientIdAsMcp_EvenWhenMcpListIsEmpty()
    {
        // Arrange
        var c = new ChannelCase("api default", true, false, true, ApiClient, new[] { ApproveWrite, ApproveViaMcp }, true, ApprovalChannel.Mcp);
        var options = new ApprovalChannelOptions { WebClientIds = new List<string> { WebClient }, ApiClientId = ApiClient };

        // Act
        var result = CreateResolver(CurrentUser(c).Object, options).Resolve();

        // Assert
        result.Channel.Should().Be(ApprovalChannel.Mcp);
    }
}
```

- [ ] **Step 2: Write the end-to-end token → channel tests** — `TokenToApprovalChannelTests.cs` (real claims, real `CurrentUserService`, real resolver; covers spec 10 "API/MCP: web token vs MCP token vs app-only token"):

```csharp
using System.Security.Claims;
using Anela.Heblo.API.Features.Users;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Approval;

public class TokenToApprovalChannelTests
{
    private const string WebClientId = "87193df0-3128-44d2-8673-580e97631a07";
    private const string ApiClientId = "8b34be89-f86f-422f-af40-7dbcd30cb66a";
    private const string AgentClientId = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string Bearer = "Bearer eyJ0eXAi.fake.token";

    private static readonly string ApproveWrite = AccessRoles.For(Feature.Marketing_AdApprovals, AccessLevel.Write);
    private static readonly string ApproveViaMcp = AccessRoles.For(Feature.Marketing_AdApprovalsViaMcp, AccessLevel.Read);

    private static ApprovalChannelResolution Resolve(IEnumerable<Claim> claims, IEnumerable<string> roles, string? authorizationHeader)
    {
        var allClaims = claims.Concat(roles.Select(r => new Claim("roles", r)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(allClaims, "TestAuth", "name", "roles"));
        var httpContext = new DefaultHttpContext { User = principal };
        if (authorizationHeader is not null)
        {
            httpContext.Request.Headers.Authorization = authorizationHeader;
        }

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(httpContext);
        var options = Options.Create(new ApprovalChannelOptions
        {
            WebClientIds = new List<string> { WebClientId },
            ApiClientId = ApiClientId,
        });
        return new ApprovalChannelResolver(new CurrentUserService(accessor.Object), options).Resolve();
    }

    private static IEnumerable<string> EveryPermissionAndSuperUser() =>
        AccessMatrix.AllRoleValues().Append(AccessRoles.Base).Append(AccessRoles.SuperUser);

    [Fact]
    public void WebSpaUserToken_IsWebChannel()
    {
        var result = Resolve(
            new[] { new Claim("azp", WebClientId), new Claim("scp", "access_as_user"), new Claim("oid", "u1") },
            new[] { ApproveWrite }, Bearer);

        result.Channel.Should().Be(ApprovalChannel.Web);
    }

    [Fact]
    public void ClaudeConnectorUserToken_IsMcpChannel()
    {
        var result = Resolve(
            new[] { new Claim("azp", ApiClientId), new Claim("scp", "access_as_user"), new Claim("oid", "u1") },
            new[] { ApproveWrite, ApproveViaMcp }, Bearer);

        result.Channel.Should().Be(ApprovalChannel.Mcp);
    }

    [Fact]
    public void V1UserTokenFromWebSpa_IsWebChannel()
    {
        var result = Resolve(
            new[] { new Claim("appid", WebClientId), new Claim("scp", "access_as_user"), new Claim("oid", "u1") },
            new[] { ApproveWrite }, Bearer);

        result.Channel.Should().Be(ApprovalChannel.Web);
    }

    [Fact]
    public void ServicePrincipalToken_WithEveryPermissionAndSuperUser_IsDenied()
    {
        var result = Resolve(
            new[] { new Claim("azp", AgentClientId), new Claim("oid", "agent-sp"), new Claim("roles", "Marketing.Agent") },
            EveryPermissionAndSuperUser(), Bearer);

        result.Allowed.Should().BeFalse();
    }

    [Fact]
    public void ServicePrincipalToken_SpoofingTheWebClientId_IsDenied()
    {
        var result = Resolve(
            new[] { new Claim("azp", WebClientId), new Claim("idtyp", "app"), new Claim("oid", "agent-sp") },
            EveryPermissionAndSuperUser(), Bearer);

        result.Allowed.Should().BeFalse();
    }

    [Fact]
    public void E2ECookieSession_IsWebChannel()
    {
        var result = Resolve(
            new[] { new Claim("oid", "e2e-test-object-id"), new Claim("scp", "access_as_user") },
            new[] { AccessRoles.SuperUser, ApproveWrite }, authorizationHeader: null);

        result.Channel.Should().Be(ApprovalChannel.Web);
    }
}
```

- [ ] **Step 3: Write the registration test** — `ApprovalChannelRegistrationTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Approval;

public class ApprovalChannelRegistrationTests
{
    private sealed class PlaceholderResolver : IApprovalChannelResolver
    {
        public ApprovalChannelResolution Resolve() => throw new NotSupportedException();
    }

    [Fact]
    public void AddApprovalChannel_ReplacesExistingResolver_AndBindsOptionsWithApiClientId()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureAd:ClientId"] = "api-client",
            ["Ads:Approval:WebClientIds:0"] = "web-client",
            ["Ads:Approval:McpClientIds:0"] = "heblo-mcp-client",
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<ICurrentUserService>());
        services.AddScoped<IApprovalChannelResolver, PlaceholderResolver>(); // stands in for C3's deny-all default

        // Act
        services.AddApprovalChannel(configuration);

        // Assert
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetServices<IApprovalChannelResolver>()
            .Should().ContainSingle().Which.Should().BeOfType<ApprovalChannelResolver>();
        var options = provider.GetRequiredService<IOptions<ApprovalChannelOptions>>().Value;
        options.ApiClientId.Should().Be("api-client");
        options.WebClientIds.Should().Equal("web-client");
        options.McpClientIds.Should().Equal("heblo-mcp-client");
    }
}
```

- [ ] **Step 4: Build — expect failure**

Expected: `error CS0246: The type or namespace name 'ApprovalChannelResolver' could not be found` (and `ApprovalChannelOptions`, `AddApprovalChannel`).

- [ ] **Step 5: Create `ApprovalChannelOptions.cs`**

```csharp
namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;

/// <summary>Which client applications count as the web UI and which as MCP (Claude) when a
/// person approves or rejects an ad proposal (spec 7.4). Client ids are not secrets.</summary>
public sealed class ApprovalChannelOptions
{
    public const string SectionName = "Ads:Approval";
    public const string ApiClientIdConfigKey = "AzureAd:ClientId";

    /// <summary>azp/appid values of the Heblo web SPA.</summary>
    public List<string> WebClientIds { get; set; } = new();

    /// <summary>Extra azp/appid values that are MCP clients (e.g. HebloMCP). The API's own
    /// client id is always added — Claude connectors to /mcp carry it.</summary>
    public List<string> McpClientIds { get; set; } = new();

    /// <summary>Filled from <c>AzureAd:ClientId</c> by <see cref="ApprovalChannelRegistration"/>.</summary>
    public string? ApiClientId { get; set; }
}
```

- [ ] **Step 6: Create `ApprovalChannelResolver.cs`** (adapt only the four `ApprovalChannelResolution` constructions if C3's type differs):

```csharp
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;

/// <summary>Decides on which channel the current caller approves/rejects an ad proposal, or
/// refuses. Order matters: app-only callers are refused before any permission is looked at.</summary>
public sealed class ApprovalChannelResolver : IApprovalChannelResolver
{
    private static readonly string ApproveWriteRole = AccessRoles.For(Feature.Marketing_AdApprovals, AccessLevel.Write);
    private static readonly string ApproveViaMcpRole = AccessRoles.For(Feature.Marketing_AdApprovalsViaMcp, AccessLevel.Read);

    private readonly ICurrentUserService _currentUser;
    private readonly IReadOnlySet<string> _webClientIds;
    private readonly IReadOnlySet<string> _mcpClientIds;

    public ApprovalChannelResolver(ICurrentUserService currentUser, IOptions<ApprovalChannelOptions> options)
    {
        _currentUser = currentUser;
        var value = options.Value;
        _webClientIds = ToClientIdSet(value.WebClientIds);
        _mcpClientIds = ToClientIdSet(value.McpClientIds.Append(value.ApiClientId));
    }

    public ApprovalChannelResolution Resolve()
    {
        if (!_currentUser.GetCurrentUser().IsAuthenticated)
        {
            return Deny("The caller is not authenticated.");
        }

        if (_currentUser.IsAppOnly)
        {
            return Deny("Service identities (app-only tokens) can never approve or reject ad proposals.");
        }

        if (!_currentUser.IsBearerToken)
        {
            return RequireWeb(); // cookie session: the web app's own login or the E2E session
        }

        var clientId = _currentUser.ClientAppId;
        if (clientId is null)
        {
            return Deny("The bearer token carries no client application id (azp/appid).");
        }

        if (_webClientIds.Contains(clientId))
        {
            return RequireWeb();
        }

        if (_mcpClientIds.Contains(clientId))
        {
            return RequireMcp();
        }

        return Deny($"Client application '{clientId}' is not allowed to approve or reject ad proposals.");
    }

    private ApprovalChannelResolution RequireWeb() =>
        _currentUser.IsInRole(ApproveWriteRole)
            ? Allow(ApprovalChannel.Web)
            : Deny($"Approving ad proposals requires {ApproveWriteRole}.");

    private ApprovalChannelResolution RequireMcp()
    {
        if (!_currentUser.IsInRole(ApproveWriteRole))
        {
            return Deny($"Approving ad proposals requires {ApproveWriteRole}.");
        }

        return _currentUser.IsInRole(ApproveViaMcpRole)
            ? Allow(ApprovalChannel.Mcp)
            : Deny($"Approving ad proposals via Claude (MCP) requires {ApproveViaMcpRole}.");
    }

    private static ApprovalChannelResolution Allow(ApprovalChannel channel) => ApprovalChannelResolution.Allow(channel);

    private static ApprovalChannelResolution Deny(string reason) => ApprovalChannelResolution.Deny(reason);

    private static IReadOnlySet<string> ToClientIdSet(IEnumerable<string?> clientIds) =>
        clientIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
```

- [ ] **Step 7: Create `ApprovalChannelRegistration.cs`**

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;

public static class ApprovalChannelRegistration
{
    /// <summary>Binds <see cref="ApprovalChannelOptions"/> and replaces the deny-all placeholder
    /// resolver registered by the proposals slice with the real one.</summary>
    public static IServiceCollection AddApprovalChannel(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ApprovalChannelOptions>()
            .Bind(configuration.GetSection(ApprovalChannelOptions.SectionName))
            .Configure(options => options.ApiClientId = configuration[ApprovalChannelOptions.ApiClientIdConfigKey]);

        services.Replace(ServiceDescriptor.Scoped<IApprovalChannelResolver, ApprovalChannelResolver>());
        return services;
    }
}
```

- [ ] **Step 8: Wire it into `MarketingAdsModule.cs`.** As the **last** statement before `return services;` in `AddMarketingAdsModule`, add:

```csharp
        services.AddApprovalChannel(configuration);
```

If `AddMarketingAdsModule` has no `IConfiguration` parameter, add `IConfiguration configuration` to its signature and pass `configuration` at its call site in `ApplicationModule.cs` (all neighbouring modules already receive it). Then delete C3's `DenyAllApprovalChannelResolver` and its dedicated test if nothing else references it (`grep -rn DenyAll backend`); its registration line in the module goes too, because `Replace` is now the only registration.

- [ ] **Step 9: Add the configuration** to `backend/src/Anela.Heblo.API/appsettings.json` — if C2/C3 already created an `"Ads"` object, merge these keys into it; otherwise add it as a new top-level section:

```json
  "Ads": {
    "AgentRateLimitPerMinute": 60,
    "Approval": {
      "WebClientIds": [ "87193df0-3128-44d2-8673-580e97631a07" ],
      "McpClientIds": [ ]
    }
  },
```

- [ ] **Step 10: Build and run — expect pass**

Build, then `--filter "FullyQualifiedName~MarketingAds.Proposals.Approval"`. Expected: all pass (15 theory rows + 4 facts in the resolver class, 6 token tests, 1 registration test), plus C3's own approval tests still green: rerun with `--filter "FullyQualifiedName~MarketingAds"`. If a C3 handler test relied on the deny-all default, it must already mock `IApprovalChannelResolver`; if it builds the real DI graph, give it the options it needs instead of weakening the assertion.

- [ ] **Step 11: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingAds backend/src/Anela.Heblo.API/appsettings.json backend/src/Anela.Heblo.Application/ApplicationModule.cs backend/test/Anela.Heblo.Tests/Features/MarketingAds
git commit -m "feat: resolve ad approval channel from token client app

Service identities (app-only tokens) are refused before any permission is
consulted; web SPA and cookie sessions approve on the Web channel, Claude
connectors and HebloMCP on the MCP channel (spec 7.4).

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
## Task 4: `/mcp` rate limiter for app-only callers

**Files:**
- Create: `backend/src/Anela.Heblo.API/MCP/McpAgentRateLimiting.cs`
- Modify: `backend/src/Anela.Heblo.API/Program.cs` (next to `builder.Services.AddMcpServices();`)
- Modify: `backend/src/Anela.Heblo.API/Extensions/ApplicationBuilderExtensions.cs` (pipeline + `MapMcp`)
- Test: `backend/test/Anela.Heblo.Tests/MCP/McpAgentRateLimitingTests.cs`

**Interfaces:**
- Consumes: `TokenClientClaims.IsAppOnly(HttpContext)` (Task 2); config key `Ads:AgentRateLimitPerMinute` (Task 3 appsettings).
- Produces: `McpAgentRateLimiting.PolicyName = "mcp-agent"`, `AddMcpAgentRateLimiting(IServiceCollection, IConfiguration)`, `RateLimitPartition<string> GetPartition(HttpContext, int permitLimit)`.

- [ ] **Step 1: Write the failing tests** — `McpAgentRateLimitingTests.cs`:

```csharp
using System.Security.Claims;
using Anela.Heblo.API.MCP;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anela.Heblo.Tests.MCP;

public class McpAgentRateLimitingTests
{
    private const int PermitLimit = 60;
    private const string Bearer = "Bearer eyJ0eXAi.fake.token";

    private static HttpContext Context(string? authorizationHeader, params Claim[] claims)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth", "name", "roles")),
        };
        if (authorizationHeader is not null)
        {
            context.Request.Headers.Authorization = authorizationHeader;
        }

        return context;
    }

    private static HttpContext AgentContext(string oid) =>
        Context(Bearer, new Claim("oid", oid), new Claim("azp", "agent-client"), new Claim("roles", "Marketing.Agent"));

    private static int CountAcquired(HttpContext context, int attempts)
    {
        var partition = McpAgentRateLimiting.GetPartition(context, PermitLimit);
        using var limiter = partition.Factory(partition.PartitionKey);
        return Enumerable.Range(0, attempts).Count(_ => limiter.AttemptAcquire().IsAcquired);
    }

    [Fact]
    public void AppOnlyCaller_IsPartitionedByObjectId()
    {
        var partition = McpAgentRateLimiting.GetPartition(AgentContext("agent-1"), PermitLimit);

        partition.PartitionKey.Should().Be("app:agent-1");
    }

    [Fact]
    public void TwoAgents_GetSeparateBuckets()
    {
        var first = McpAgentRateLimiting.GetPartition(AgentContext("agent-1"), PermitLimit);
        var second = McpAgentRateLimiting.GetPartition(AgentContext("agent-2"), PermitLimit);

        first.PartitionKey.Should().NotBe(second.PartitionKey);
    }

    [Fact]
    public void AppOnlyCaller_IsRefusedAfterPermitLimitWithinTheWindow()
    {
        CountAcquired(AgentContext("agent-1"), PermitLimit + 1).Should().Be(PermitLimit);
    }

    [Fact]
    public void DelegatedUser_IsNeverLimited()
    {
        var user = Context(Bearer, new Claim("oid", "user-1"), new Claim("azp", "api-client"), new Claim("scp", "access_as_user"));

        McpAgentRateLimiting.GetPartition(user, PermitLimit).PartitionKey.Should().Be("interactive");
        CountAcquired(user, PermitLimit * 10).Should().Be(PermitLimit * 10);
    }

    [Fact]
    public void CookieSession_IsNeverLimited()
    {
        var cookieUser = Context(authorizationHeader: null, new Claim("oid", "e2e-test-object-id"));

        McpAgentRateLimiting.GetPartition(cookieUser, PermitLimit).PartitionKey.Should().Be("interactive");
    }

    [Fact]
    public void AppOnlyCallerWithoutOid_SharesTheUnknownBucket()
    {
        var anonymousApp = Context(Bearer, new Claim("azp", "agent-client"));

        McpAgentRateLimiting.GetPartition(anonymousApp, PermitLimit).PartitionKey.Should().Be("app:unknown");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void AddMcpAgentRateLimiting_RejectsNonPositiveLimit(string configured)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [McpAgentRateLimiting.PermitLimitConfigKey] = configured })
            .Build();

        var act = () => new ServiceCollection().AddMcpAgentRateLimiting(configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Ads:AgentRateLimitPerMinute*");
    }
}
```

- [ ] **Step 2: Build — expect failure**

Expected: `error CS0103: The name 'McpAgentRateLimiting' does not exist in the current context`.

- [ ] **Step 3: Create `McpAgentRateLimiting.cs`**

```csharp
using System.Threading.RateLimiting;
using Anela.Heblo.API.Infrastructure.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Identity.Web;

namespace Anela.Heblo.API.MCP;

/// <summary>Per-service-principal rate limit on /mcp (spec 7.3). App-only callers (agents) get a
/// fixed one-minute window keyed by their oid; people (delegated tokens, cookie sessions) are not limited.</summary>
public static class McpAgentRateLimiting
{
    public const string PolicyName = "mcp-agent";
    public const string PermitLimitConfigKey = "Ads:AgentRateLimitPerMinute";
    public const int DefaultPermitLimit = 60;

    private const string InteractivePartitionKey = "interactive";
    private const string AppPartitionPrefix = "app:";
    private const string UnknownObjectId = "unknown";
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static IServiceCollection AddMcpAgentRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var permitLimit = configuration.GetValue(PermitLimitConfigKey, DefaultPermitLimit);
        if (permitLimit <= 0)
        {
            throw new InvalidOperationException(
                $"{PermitLimitConfigKey} must be a positive number of requests per minute (got {permitLimit}).");
        }

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(McpAgentRateLimiting).FullName!);
                logger.LogWarning(
                    "MCP agent rate limit hit for service principal {ObjectId} ({PermitLimit}/min)",
                    context.HttpContext.User.GetObjectId() ?? UnknownObjectId,
                    permitLimit);
                return ValueTask.CompletedTask;
            };
            options.AddPolicy(PolicyName, httpContext => GetPartition(httpContext, permitLimit));
        });

        return services;
    }

    public static RateLimitPartition<string> GetPartition(HttpContext httpContext, int permitLimit)
    {
        if (!TokenClientClaims.IsAppOnly(httpContext))
        {
            return RateLimitPartition.GetNoLimiter(InteractivePartitionKey);
        }

        var objectId = httpContext.User.GetObjectId() ?? UnknownObjectId;
        return RateLimitPartition.GetFixedWindowLimiter(
            AppPartitionPrefix + objectId,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = Window,
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    }
}
```

- [ ] **Step 4: Register and apply.**

In `Program.cs`, directly after `builder.Services.AddMcpServices();`:

```csharp
        builder.Services.AddMcpAgentRateLimiting(builder.Configuration);
```

In `ApplicationBuilderExtensions.ConfigureApplicationPipeline`, directly after `app.UseAuthorization();` (the limiter needs the authenticated, claims-transformed user and runs after routing so endpoint policies apply):

```csharp
        // Per-service-principal rate limit for agents on /mcp (policy attached on MapMcp below).
        app.UseRateLimiter();
```

and change the `MapMcp` block to:

```csharp
        app.MapMcp("/mcp")
            .RequireAuthorization()
            .RequireRateLimiting(McpAgentRateLimiting.PolicyName)
            .WithRequestTimeout(TimeSpan.FromMinutes(5));
```

- [ ] **Step 5: Build and run — expect pass**

Build, then `--filter "FullyQualifiedName~McpAgentRateLimitingTests"`. Expected: 8 passed. Then run `--filter "FullyQualifiedName~MCP|FullyQualifiedName~SpaFallback"` to confirm existing MCP/pipeline tests still pass.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.API/MCP/McpAgentRateLimiting.cs backend/src/Anela.Heblo.API/Program.cs backend/src/Anela.Heblo.API/Extensions/ApplicationBuilderExtensions.cs backend/test/Anela.Heblo.Tests/MCP/McpAgentRateLimitingTests.cs
git commit -m "feat: rate limit app-only callers on /mcp per service principal

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
## Task 5: `MarketingAdsMcpTools` — skeleton, helpers and the six read tools

**Files:**
- Create: `backend/src/Anela.Heblo.API/MCP/Tools/MarketingAdsMcpTools.cs`
- Modify: `backend/src/Anela.Heblo.API/MCP/McpModule.cs`
- Test: `backend/test/Anela.Heblo.Tests/MCP/Tools/MarketingAdsMcpToolsTestBase.cs`, `MarketingAdsMcpToolsReadTests.cs`

**Interfaces:**
- Consumes: C2/C3 requests per the "Before you start" table; `EnsureFeatureAccess` (`MCP/McpAuthorizationExtensions.cs`); `McpJsonOptions.Default`.
- Produces: class `MarketingAdsMcpTools(IMediator, ICurrentUserService, ILogger<MarketingAdsMcpTools>)`; private helpers `SendAsync`, `Serialize`, `EnsureSuccess`, `ParseDate`, `ParseRange`, `ParseInstant`, `RequireText` used by Tasks 6–7; test base `MarketingAdsMcpToolsTestBase` with `Mediator`, `CurrentUser`, `Tools`, `DenyRole(string)`, role constants.

- [ ] **Step 1: Write the test base** — `MarketingAdsMcpToolsTestBase.cs`:

```csharp
using Anela.Heblo.API.MCP.Tools;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.MCP.Tools;

public abstract class MarketingAdsMcpToolsTestBase
{
    protected static readonly string AdsRead = AccessRoles.For(Feature.Marketing_Ads, AccessLevel.Read);
    protected static readonly string AdsWrite = AccessRoles.For(Feature.Marketing_Ads, AccessLevel.Write);
    protected static readonly string ApprovalsWrite = AccessRoles.For(Feature.Marketing_AdApprovals, AccessLevel.Write);

    protected readonly Mock<IMediator> Mediator = new();
    protected readonly Mock<ICurrentUserService> CurrentUser = new();
    protected readonly MarketingAdsMcpTools Tools;

    protected MarketingAdsMcpToolsTestBase()
    {
        // Default: caller holds every role. FORBIDDEN tests deny a specific role (later setup wins).
        CurrentUser.Setup(s => s.IsInRole(It.IsAny<string>())).Returns(true);
        Tools = new MarketingAdsMcpTools(Mediator.Object, CurrentUser.Object, Mock.Of<ILogger<MarketingAdsMcpTools>>());
    }

    protected void DenyRole(string role) => CurrentUser.Setup(s => s.IsInRole(role)).Returns(false);
}
```

- [ ] **Step 2: Write the failing read-tool tests** — `MarketingAdsMcpToolsReadTests.cs` (fix the `using` lines for C2/C3 namespaces found in the mapping step):

```csharp
using System.Reflection;
using System.Text.Json;
using Anela.Heblo.API.MCP.Tools;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdAccounts;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdChangeHistory;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdGuardrails;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdPerformance;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdSearchTerms;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetBlendedPerformance;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP.Tools;

public class MarketingAdsMcpToolsReadTests : MarketingAdsMcpToolsTestBase
{
    [Fact]
    public void ToolSurface_MatchesSpecSection75()
    {
        var names = typeof(MarketingAdsMcpTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .Select(m => m.Name);

        names.Should().BeEquivalentTo(new[]
        {
            "ListAdAccounts", "GetAdPerformance", "GetAdSearchTerms", "GetBlendedPerformance",
            "GetAdChangeHistory", "GetAdGuardrails", "SubmitAdProposal", "ReviseAdProposal",
            "ListAdProposals", "GetAdProposal", "StartAgentRun", "FinishAgentRun",
            "ListPendingAdProposals", "ApproveAdProposal", "RejectAdProposal",
        });
    }

    [Fact]
    public async Task ListAdAccounts_ForwardsPlatform_AndReturnsJson()
    {
        // Arrange
        Mediator.Setup(m => m.Send(It.IsAny<GetAdAccountsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetAdAccountsResponse());

        // Act
        var json = await Tools.ListAdAccounts(AdPlatform.Sklik);

        // Assert
        Mediator.Verify(m => m.Send(It.Is<GetAdAccountsRequest>(r => r.Platform == AdPlatform.Sklik), It.IsAny<CancellationToken>()), Times.Once);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("Success").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetAdPerformance_MapsEveryParameter()
    {
        // Arrange
        Mediator.Setup(m => m.Send(It.IsAny<GetAdPerformanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetAdPerformanceResponse());

        // Act
        await Tools.GetAdPerformance(AdPlatform.GoogleAds, "123-456-7890", AdEntityLevel.AdGroup,
            "2026-09-01", "2026-09-30", parentExternalId: "cmp-1", pageNumber: 2, pageSize: 25);

        // Assert
        Mediator.Verify(m => m.Send(It.Is<GetAdPerformanceRequest>(r =>
            r.Platform == AdPlatform.GoogleAds &&
            r.AccountExternalId == "123-456-7890" &&
            r.Level == AdEntityLevel.AdGroup &&
            r.From == new DateOnly(2026, 9, 1) &&
            r.To == new DateOnly(2026, 9, 30) &&
            r.ParentExternalId == "cmp-1" &&
            r.PageNumber == 2 &&
            r.PageSize == 25), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("2026-13-01", "2026-09-30")]
    [InlineData("1.9.2026", "2026-09-30")]
    [InlineData("", "2026-09-30")]
    [InlineData("2026-09-30", "2026-09-01")]
    public async Task GetAdPerformance_RejectsBadDates_WithoutCallingTheHandler(string from, string to)
    {
        // Act
        var act = () => Tools.GetAdPerformance(AdPlatform.GoogleAds, "acc", AdEntityLevel.Campaign, from, to);

        // Assert
        (await act.Should().ThrowAsync<McpException>()).Which.Message.Should().Contain(nameof(ErrorCodes.ValidationError));
        Mediator.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAdPerformance_RejectsBlankAccount()
    {
        var act = () => Tools.GetAdPerformance(AdPlatform.MetaAds, "  ", AdEntityLevel.Campaign, "2026-09-01", "2026-09-02");

        (await act.Should().ThrowAsync<McpException>()).Which.Message.Should().Contain("accountExternalId");
        Mediator.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAdSearchTerms_MapsParameters()
    {
        // Arrange
        Mediator.Setup(m => m.Send(It.IsAny<GetAdSearchTermsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetAdSearchTermsResponse());

        // Act
        await Tools.GetAdSearchTerms(AdPlatform.Sklik, "acc", AdEntityLevel.Campaign, "cmp-9", "2026-09-01", "2026-09-07");

        // Assert
        Mediator.Verify(m => m.Send(It.Is<GetAdSearchTermsRequest>(r =>
            r.Platform == AdPlatform.Sklik &&
            r.ParentLevel == AdEntityLevel.Campaign &&
            r.ParentExternalId == "cmp-9" &&
            r.From == new DateOnly(2026, 9, 1) &&
            r.To == new DateOnly(2026, 9, 7)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(AdEntityLevel.Keyword)]
    [InlineData(AdEntityLevel.Ad)]
    public async Task GetAdSearchTerms_RejectsParentLevelsOtherThanCampaignOrAdGroup(AdEntityLevel level)
    {
        var act = () => Tools.GetAdSearchTerms(AdPlatform.GoogleAds, "acc", level, "x", "2026-09-01", "2026-09-07");

        await act.Should().ThrowAsync<McpException>();
        Mediator.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task GetBlendedPerformance_ForwardsGranularity()
    {
        Mediator.Setup(m => m.Send(It.IsAny<GetBlendedPerformanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetBlendedPerformanceResponse());

        await Tools.GetBlendedPerformance("2026-01-01", "2026-09-30", BlendedGranularity.Daily);

        Mediator.Verify(m => m.Send(It.Is<GetBlendedPerformanceRequest>(r =>
            r.Granularity == BlendedGranularity.Daily &&
            r.From == new DateOnly(2026, 1, 1)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAdChangeHistory_MapsFiltersAndParsesInstants()
    {
        Mediator.Setup(m => m.Send(It.IsAny<GetAdChangeHistoryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetAdChangeHistoryResponse());

        await Tools.GetAdChangeHistory(AdPlatform.GoogleAds, "acc", since: "2026-09-01T00:00:00Z",
            origin: AdChangeOrigin.OutOfBand, actorKind: AdChangeActorKind.PlatformAutomation);

        Mediator.Verify(m => m.Send(It.Is<GetAdChangeHistoryRequest>(r =>
            r.Since == new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero) &&
            r.Until == null &&
            r.Origin == AdChangeOrigin.OutOfBand &&
            r.ActorKind == AdChangeActorKind.PlatformAutomation), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAdChangeHistory_RejectsMalformedInstant()
    {
        var act = () => Tools.GetAdChangeHistory(since: "yesterday");

        await act.Should().ThrowAsync<McpException>();
        Mediator.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAdGuardrails_SendsRequest()
    {
        Mediator.Setup(m => m.Send(It.IsAny<GetAdGuardrailsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetAdGuardrailsResponse());

        await Tools.GetAdGuardrails();

        Mediator.Verify(m => m.Send(It.IsAny<GetAdGuardrailsRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("ListAdAccounts")]
    [InlineData("GetAdPerformance")]
    [InlineData("GetAdSearchTerms")]
    [InlineData("GetBlendedPerformance")]
    [InlineData("GetAdChangeHistory")]
    [InlineData("GetAdGuardrails")]
    public async Task ReadTools_ThrowForbidden_AndSkipMediator_WithoutAdsRead(string tool)
    {
        // Arrange
        DenyRole(AdsRead);

        // Act
        var act = () => tool switch
        {
            "ListAdAccounts" => Tools.ListAdAccounts(),
            "GetAdPerformance" => Tools.GetAdPerformance(AdPlatform.GoogleAds, "acc", AdEntityLevel.Campaign, "2026-09-01", "2026-09-02"),
            "GetAdSearchTerms" => Tools.GetAdSearchTerms(AdPlatform.GoogleAds, "acc", AdEntityLevel.AdGroup, "g", "2026-09-01", "2026-09-02"),
            "GetBlendedPerformance" => Tools.GetBlendedPerformance("2026-09-01", "2026-09-02"),
            "GetAdChangeHistory" => Tools.GetAdChangeHistory(),
            _ => Tools.GetAdGuardrails(),
        };

        // Assert
        var ex = (await act.Should().ThrowAsync<McpException>()).Which;
        ex.Message.Should().Contain("FORBIDDEN").And.Contain(AdsRead);
        Mediator.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task HandlerErrorWithoutParams_BecomesMcpException_NotNullReference()
    {
        // Arrange
        Mediator.Setup(m => m.Send(It.IsAny<GetAdAccountsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetAdAccountsResponse { Success = false, ErrorCode = ErrorCodes.ResourceNotFound, Params = null });

        // Act
        var act = () => Tools.ListAdAccounts();

        // Assert
        (await act.Should().ThrowAsync<McpException>()).Which.Message.Should().StartWith("[ResourceNotFound]");
    }
}
```

- [ ] **Step 3: Build — expect failure**

Expected: `error CS0246: The type or namespace name 'MarketingAdsMcpTools' could not be found`.

- [ ] **Step 4: Create `MarketingAdsMcpTools.cs`** with the helpers and the six read tools. Tasks 6–7 add the remaining tools to this class (keep the `#region`-free layout: read tools, then proposal tools, then approval tools, then helpers at the bottom).

```csharp
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Anela.Heblo.API.Infrastructure.Json;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdAccounts;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdChangeHistory;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdGuardrails;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdPerformance;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdSearchTerms;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetBlendedPerformance;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Users;
using FluentValidation;
using MediatR;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Anela.Heblo.API.MCP.Tools;

/// <summary>
/// MCP surface of the marketing agents platform (spec 7.5): ad data for agents, proposals,
/// agent-run reporting and the chief's approvals. Thin wrappers over the MarketingAds MediatR
/// handlers. Approve/reject channel rules (web vs MCP vs service identity) are enforced inside
/// the handlers via IApprovalChannelResolver, so REST and MCP behave the same.
/// </summary>
[McpServerToolType]
public class MarketingAdsMcpTools
{
    private const string AdsResource = "Marketing Ads";
    private const string ApprovalsResource = "Marketing Ad Approvals";
    private const string DateFormat = "yyyy-MM-dd";
    private const int DefaultPageSize = 50;
    private const int MaxPendingItems = 50;

    private readonly IMediator _mediator;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<MarketingAdsMcpTools> _logger;

    public MarketingAdsMcpTools(IMediator mediator, ICurrentUserService currentUserService, ILogger<MarketingAdsMcpTools> logger)
    {
        _mediator = mediator;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    [McpServerTool]
    [Description("List the ad accounts Heblo manages (Google Ads, Meta Ads, Sklik): external id, name, currency, time zone. " +
                 "Use the external id as accountExternalId in the other ad tools.")]
    public async Task<string> ListAdAccounts(
        [Description("Optional platform filter: GoogleAds, MetaAds or Sklik")] AdPlatform? platform = null,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Marketing_Ads, AdsResource);

        var response = await SendAsync(new GetAdAccountsRequest { Platform = platform }, "list ad accounts", cancellationToken);
        return Serialize(response);
    }

    [McpServerTool]
    [Description("Daily platform-reported performance (impressions, clicks, cost net of VAT in account currency, conversions, " +
                 "conversion value) for ONE entity level of one account and date range. Never sum rows of different levels. " +
                 "Optionally restrict to children of one parent (e.g. ad groups of a campaign). Paged.")]
    public async Task<string> GetAdPerformance(
        [Description("GoogleAds, MetaAds or Sklik")] AdPlatform platform,
        [Description("Account external id from ListAdAccounts")] string accountExternalId,
        [Description("Campaign, AdGroup, Keyword or Ad")] AdEntityLevel level,
        [Description("First day, yyyy-MM-dd")] string from,
        [Description("Last day (inclusive), yyyy-MM-dd")] string to,
        [Description("Optional parent entity external id")] string? parentExternalId = null,
        [Description("Page number (default 1)")] int pageNumber = 1,
        [Description("Page size (default 50)")] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Marketing_Ads, AdsResource);
        var account = RequireText(accountExternalId, nameof(accountExternalId));
        var (fromDate, toDate) = ParseRange(from, to);

        var request = new GetAdPerformanceRequest
        {
            Platform = platform,
            AccountExternalId = account,
            Level = level,
            From = fromDate,
            To = toDate,
            ParentExternalId = string.IsNullOrWhiteSpace(parentExternalId) ? null : parentExternalId.Trim(),
            PageNumber = pageNumber,
            PageSize = pageSize,
        };
        return Serialize(await SendAsync(request, "get ad performance", cancellationToken));
    }

    [McpServerTool]
    [Description("Search terms (what people actually typed) with daily metrics for one campaign or ad group and date range. " +
                 "Google Ads and Sklik only; Meta has no search terms. Paged. Use it to find negative-keyword candidates.")]
    public async Task<string> GetAdSearchTerms(
        [Description("GoogleAds or Sklik")] AdPlatform platform,
        [Description("Account external id from ListAdAccounts")] string accountExternalId,
        [Description("Campaign or AdGroup")] AdEntityLevel parentLevel,
        [Description("External id of that campaign / ad group")] string parentExternalId,
        [Description("First day, yyyy-MM-dd")] string from,
        [Description("Last day (inclusive), yyyy-MM-dd")] string to,
        [Description("Page number (default 1)")] int pageNumber = 1,
        [Description("Page size (default 50)")] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Marketing_Ads, AdsResource);
        if (parentLevel is not (AdEntityLevel.Campaign or AdEntityLevel.AdGroup))
        {
            throw ValidationError($"parentLevel must be Campaign or AdGroup (got {parentLevel}).");
        }

        var (fromDate, toDate) = ParseRange(from, to);
        var request = new GetAdSearchTermsRequest
        {
            Platform = platform,
            AccountExternalId = RequireText(accountExternalId, nameof(accountExternalId)),
            ParentLevel = parentLevel,
            ParentExternalId = RequireText(parentExternalId, nameof(parentExternalId)),
            From = fromDate,
            To = toDate,
            PageNumber = pageNumber,
            PageSize = pageSize,
        };
        return Serialize(await SendAsync(request, "get ad search terms", cancellationToken));
    }

    [McpServerTool]
    [Description("Blended reality check: real e-shop revenue (net of VAT, cancelled orders excluded) divided by total ad cost " +
                 "of all platforms, daily or monthly. Platforms over-count conversions; use this to sanity-check them.")]
    public async Task<string> GetBlendedPerformance(
        [Description("First day, yyyy-MM-dd")] string from,
        [Description("Last day (inclusive), yyyy-MM-dd")] string to,
        [Description("Daily or Monthly (default Monthly)")] BlendedGranularity granularity = BlendedGranularity.Monthly,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Marketing_Ads, AdsResource);
        var (fromDate, toDate) = ParseRange(from, to);

        var request = new GetBlendedPerformanceRequest { From = fromDate, To = toDate, Granularity = granularity };
        return Serialize(await SendAsync(request, "get blended performance", cancellationToken));
    }

    [McpServerTool]
    [Description("Changes made to ad accounts — by Heblo, the agency, people in the platform UI or platform auto-applied " +
                 "recommendations — each flagged Heblo or OutOfBand. Filter by platform, account, time, origin or actor kind. Paged.")]
    public async Task<string> GetAdChangeHistory(
        [Description("Optional platform filter")] AdPlatform? platform = null,
        [Description("Optional account external id")] string? accountExternalId = null,
        [Description("Optional start, ISO-8601 (e.g. 2026-09-01T00:00:00Z)")] string? since = null,
        [Description("Optional end, ISO-8601")] string? until = null,
        [Description("Optional origin filter: Heblo or OutOfBand")] AdChangeOrigin? origin = null,
        [Description("Optional actor filter: Heblo, User, PlatformAutomation, Unknown")] AdChangeActorKind? actorKind = null,
        [Description("Page number (default 1)")] int pageNumber = 1,
        [Description("Page size (default 50)")] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Marketing_Ads, AdsResource);

        var request = new GetAdChangeHistoryRequest
        {
            Platform = platform,
            AccountExternalId = string.IsNullOrWhiteSpace(accountExternalId) ? null : accountExternalId.Trim(),
            Since = ParseInstant(since, nameof(since)),
            Until = ParseInstant(until, nameof(until)),
            Origin = origin,
            ActorKind = actorKind,
            PageNumber = pageNumber,
            PageSize = pageSize,
        };
        return Serialize(await SendAsync(request, "get ad change history", cancellationToken));
    }

    [McpServerTool]
    [Description("The rules a proposal must fit: allowed action types per platform, per-proposal and per-week limits, " +
                 "the stricter limits for approvals via Claude, autonomy modes and whether execution is switched on. " +
                 "Read this before proposing so proposals are not refused.")]
    public async Task<string> GetAdGuardrails(CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Marketing_Ads, AdsResource);

        return Serialize(await SendAsync(new GetAdGuardrailsRequest(), "get ad guardrails", cancellationToken));
    }

    private async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, string operation, CancellationToken cancellationToken)
        where TResponse : BaseResponse
    {
        TResponse response;
        try
        {
            response = await _mediator.Send(request, cancellationToken);
        }
        catch (ValidationException ex)
        {
            var details = string.Join(" | ", ex.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"));
            throw new McpException($"[{ErrorCodes.ValidationError}] {details}");
        }
        catch (McpException)
        {
            throw;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "MCP marketing ads call failed: {Operation}", operation);
            throw new McpException($"Failed to {operation}: {ex.Message}");
        }

        EnsureSuccess(response);
        return response;
    }

    private static string Serialize(object value) => JsonSerializer.Serialize(value, McpJsonOptions.Default);

    private static void EnsureSuccess(BaseResponse response)
    {
        if (response.Success)
        {
            return;
        }

        var code = response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR";
        // FullError() dereferences Params, which is null when a handler reports a bare error code.
        var detail = response.Params is { Count: > 0 } ? response.FullError() : code;
        throw new McpException($"[{code}] {detail}");
    }

    private static McpException ValidationError(string message) => new($"[{ErrorCodes.ValidationError}] {message}");

    private static string RequireText(string? value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw ValidationError($"{parameterName} must not be empty.")
            : value.Trim();

    private static DateOnly ParseDate(string? value, string parameterName) =>
        DateOnly.TryParseExact(value?.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw ValidationError($"{parameterName} must be a date in {DateFormat} format (got '{value}').");

    private static (DateOnly From, DateOnly To) ParseRange(string? from, string? to)
    {
        var fromDate = ParseDate(from, nameof(from));
        var toDate = ParseDate(to, nameof(to));
        return fromDate <= toDate
            ? (fromDate, toDate)
            : throw ValidationError($"from ({from}) must not be after to ({to}).");
    }

    private static DateTimeOffset? ParseInstant(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var instant)
            ? instant
            : throw ValidationError($"{parameterName} must be an ISO-8601 date-time (got '{value}').");
    }
}
```

The `ToolSurface_MatchesSpecSection75` test stays red until Task 7 adds the last tools — that is expected; every other test in this file must pass now.

- [ ] **Step 5: Register the tool class** — in `McpModule.cs`, after `.WithTools<ProcessDocsMcpTools>()` (move the `;`):

```csharp
            .WithTools<ProcessDocsMcpTools>()
            .WithTools<MarketingAdsMcpTools>();
```

- [ ] **Step 6: Build and run — expect pass except the surface test**

Build, then `--filter "FullyQualifiedName~MarketingAdsMcpToolsReadTests"`. Expected: all pass except `ToolSurface_MatchesSpecSection75` (missing 9 names).

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.API/MCP backend/test/Anela.Heblo.Tests/MCP/Tools/MarketingAdsMcpToolsTestBase.cs backend/test/Anela.Heblo.Tests/MCP/Tools/MarketingAdsMcpToolsReadTests.cs
git commit -m "feat: add marketing ads read tools to MCP

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
## Task 6: Proposal and agent-run tools

**Files:**
- Modify: `backend/src/Anela.Heblo.API/MCP/Tools/MarketingAdsMcpTools.cs` (insert after `GetAdGuardrails`, before the private helpers)
- Test: `backend/test/Anela.Heblo.Tests/MCP/Tools/MarketingAdsMcpToolsProposalTests.cs`

**Interfaces:**
- Consumes: Task 5 helpers (`SendAsync`, `Serialize`, `RequireText`, `ValidationError`); C3 requests `SubmitAdProposalRequest`, `ReviseAdProposalRequest`, `GetAdProposalsRequest`, `GetAdProposalRequest`, `StartAgentRunRequest`, `FinishAgentRunRequest`; C3 types `AdActionInputDto`, `AdProposalStatus`, `AgentRunStatus`.
- Produces: tools `SubmitAdProposal`, `ReviseAdProposal`, `ListAdProposals`, `GetAdProposal`, `StartAgentRun`, `FinishAgentRun`.

- [ ] **Step 1: Write the failing tests** — `MarketingAdsMcpToolsProposalTests.cs`. Construct `AdActionInputDto` with C3's real property names (the sample below assumes `Type`, `Platform`, `AccountExternalId`, `TargetLevel`, `TargetExternalId`, `Payload`). `StaleVersionCode` must be the error code C3's revise/approve handlers return for a non-current version (`grep -n "Conflict\|Stale\|Version" backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`).

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.FinishAgentRun;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdProposal;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdProposals;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.ReviseAdProposal;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.StartAgentRun;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.SubmitAdProposal;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using ModelContextProtocol;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP.Tools;

public class MarketingAdsMcpToolsProposalTests : MarketingAdsMcpToolsTestBase
{
    // Replace with C3's stale-version code if it differs.
    private const ErrorCodes StaleVersionCode = ErrorCodes.ValidationError;

    private static List<AdActionInputDto> OneNegativeKeyword() => new()
    {
        new AdActionInputDto
        {
            Type = AdActionType.AddNegativeKeyword,
            Platform = AdPlatform.GoogleAds,
            AccountExternalId = "123-456-7890",
            TargetLevel = AdEntityLevel.Campaign,
            TargetExternalId = "cmp-1",
            Payload = new Dictionary<string, string> { ["text"] = "zdarma", ["matchType"] = "Phrase" },
        },
    };

    [Fact]
    public async Task SubmitAdProposal_ForwardsActionsReasoningAndRun()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var actions = OneNegativeKeyword();
        Mediator.Setup(m => m.Send(It.IsAny<SubmitAdProposalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubmitAdProposalResponse());

        // Act
        await Tools.SubmitAdProposal(actions, "  Wasted spend on free-seekers.  ", runId);

        // Assert
        Mediator.Verify(m => m.Send(It.Is<SubmitAdProposalRequest>(r =>
            r.Actions.SequenceEqual(actions) &&
            r.Reasoning == "Wasted spend on free-seekers." &&
            r.AgentRunId == runId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitAdProposal_RejectsEmptyActionList()
    {
        var act = () => Tools.SubmitAdProposal(new List<AdActionInputDto>(), "reason");

        (await act.Should().ThrowAsync<McpException>()).Which.Message.Should().Contain(nameof(ErrorCodes.ValidationError));
        Mediator.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitAdProposal_RejectsBlankReasoning()
    {
        var act = () => Tools.SubmitAdProposal(OneNegativeKeyword(), "   ");

        await act.Should().ThrowAsync<McpException>();
        Mediator.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task ReviseAdProposal_ForwardsIdAndExpectedVersion()
    {
        var id = Guid.NewGuid();
        Mediator.Setup(m => m.Send(It.IsAny<ReviseAdProposalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReviseAdProposalResponse());

        await Tools.ReviseAdProposal(id, 2, OneNegativeKeyword(), "narrowed to phrase match");

        Mediator.Verify(m => m.Send(It.Is<ReviseAdProposalRequest>(r =>
            r.ProposalId == id && r.ExpectedVersion == 2 && r.Actions.Count == 1), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReviseAdProposal_SurfacesStaleVersionAsMcpException()
    {
        Mediator.Setup(m => m.Send(It.IsAny<ReviseAdProposalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReviseAdProposalResponse { Success = false, ErrorCode = StaleVersionCode });

        var act = () => Tools.ReviseAdProposal(Guid.NewGuid(), 1, OneNegativeKeyword(), "r");

        (await act.Should().ThrowAsync<McpException>()).Which.Message.Should().StartWith($"[{StaleVersionCode}]");
    }

    [Fact]
    public async Task ListAdProposals_MapsStatusAndMine()
    {
        Mediator.Setup(m => m.Send(It.IsAny<GetAdProposalsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetAdProposalsResponse());

        await Tools.ListAdProposals(AdProposalStatus.Rejected, onlyMine: true, pageNumber: 3, pageSize: 10);

        Mediator.Verify(m => m.Send(It.Is<GetAdProposalsRequest>(r =>
            r.Status == AdProposalStatus.Rejected && r.OnlyMine && r.PageNumber == 3 && r.PageSize == 10),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAdProposal_ForwardsId()
    {
        var id = Guid.NewGuid();
        Mediator.Setup(m => m.Send(It.IsAny<GetAdProposalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetAdProposalResponse());

        await Tools.GetAdProposal(id);

        Mediator.Verify(m => m.Send(It.Is<GetAdProposalRequest>(r => r.ProposalId == id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAgentRun_ForwardsNameAndPurpose()
    {
        Mediator.Setup(m => m.Send(It.IsAny<StartAgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StartAgentRunResponse());

        await Tools.StartAgentRun(" paid-search-agent ", "weekly negatives sweep");

        Mediator.Verify(m => m.Send(It.Is<StartAgentRunRequest>(r =>
            r.AgentName == "paid-search-agent" && r.Purpose == "weekly negatives sweep"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAgentRun_RejectsBlankName()
    {
        var act = () => Tools.StartAgentRun("");

        await act.Should().ThrowAsync<McpException>();
        Mediator.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task FinishAgentRun_ForwardsEveryField()
    {
        var runId = Guid.NewGuid();
        Mediator.Setup(m => m.Send(It.IsAny<FinishAgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FinishAgentRunResponse());

        await Tools.FinishAgentRun(runId, AgentRunStatus.Succeeded, "3 proposals", new List<string> { "search_terms" }, 120_000, 4.5m);

        Mediator.Verify(m => m.Send(It.Is<FinishAgentRunRequest>(r =>
            r.AgentRunId == runId &&
            r.Status == AgentRunStatus.Succeeded &&
            r.Summary == "3 proposals" &&
            r.DataScopesRead!.Single() == "search_terms" &&
            r.TokensUsed == 120_000 &&
            r.CostCzk == 4.5m), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("SubmitAdProposal")]
    [InlineData("ReviseAdProposal")]
    [InlineData("StartAgentRun")]
    [InlineData("FinishAgentRun")]
    public async Task WriteTools_ThrowForbidden_WithReadButNotWrite(string tool)
    {
        // Arrange — a read-only marketer
        DenyRole(AdsWrite);

        // Act
        var act = () => tool switch
        {
            "SubmitAdProposal" => Tools.SubmitAdProposal(OneNegativeKeyword(), "r"),
            "ReviseAdProposal" => Tools.ReviseAdProposal(Guid.NewGuid(), 1, OneNegativeKeyword(), "r"),
            "StartAgentRun" => Tools.StartAgentRun("agent"),
            _ => Tools.FinishAgentRun(Guid.NewGuid(), AgentRunStatus.Failed),
        };

        // Assert
        (await act.Should().ThrowAsync<McpException>()).Which.Message.Should().Contain("FORBIDDEN").And.Contain(AdsWrite);
        Mediator.Invocations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("ListAdProposals")]
    [InlineData("GetAdProposal")]
    public async Task ProposalReadTools_ThrowForbidden_WithoutAdsRead(string tool)
    {
        DenyRole(AdsRead);

        var act = () => tool == "ListAdProposals" ? Tools.ListAdProposals() : Tools.GetAdProposal(Guid.NewGuid());

        (await act.Should().ThrowAsync<McpException>()).Which.Message.Should().Contain(AdsRead);
        Mediator.Invocations.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Build — expect failure**

Expected: `error CS1061: 'MarketingAdsMcpTools' does not contain a definition for 'SubmitAdProposal'`.

- [ ] **Step 3: Add the tools** — add these `using` lines to `MarketingAdsMcpTools.cs` (fix namespaces per mapping):

```csharp
using Anela.Heblo.Application.Features.MarketingAds.UseCases.FinishAgentRun;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdProposal;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdProposals;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.ReviseAdProposal;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.StartAgentRun;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.SubmitAdProposal;
using Anela.Heblo.Domain.Features.MarketingAds;
```

and insert after `GetAdGuardrails`:

```csharp
    [McpServerTool]
    [Description("Submit a proposal: 1–50 actions of ONE action type on ONE account, plus your reasoning (shown to the " +
                 "approver separately; the human-readable diff is rendered by Heblo from the actions, never from your text). " +
                 "Returns the proposal id, version and the limits evaluation. Nothing is executed until a human approves. " +
                 "Call GetAdGuardrails first.")]
    public async Task<string> SubmitAdProposal(
        [Description("Actions: type (AddNegativeKeyword | PauseAd), platform, accountExternalId, targetLevel, targetExternalId, " +
                     "payload (AddNegativeKeyword: text + matchType Exact|Phrase|Broad; PauseAd: empty)")]
        List<AdActionInputDto> actions,
        [Description("Why — evidence and expected effect")] string reasoning,
        [Description("Optional id from StartAgentRun")] Guid? agentRunId = null,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Marketing_Ads, AdsResource, AccessLevel.Write);

        var request = new SubmitAdProposalRequest
        {
            Actions = RequireActions(actions),
            Reasoning = RequireText(reasoning, nameof(reasoning)),
            AgentRunId = agentRunId,
        };
        return Serialize(await SendAsync(request, "submit ad proposal", cancellationToken));
    }

    [McpServerTool]
    [Description("Revise a pending proposal: creates version+1 and voids earlier approvals. expectedVersion must be the " +
                 "current version, otherwise the call fails and you should re-read the proposal.")]
    public async Task<string> ReviseAdProposal(
        [Description("Proposal id")] Guid proposalId,
        [Description("The version you are revising (the current one)")] int expectedVersion,
        [Description("The complete new action list (same rules as SubmitAdProposal)")] List<AdActionInputDto> actions,
        [Description("Updated reasoning")] string reasoning,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Marketing_Ads, AdsResource, AccessLevel.Write);

        var request = new ReviseAdProposalRequest
        {
            ProposalId = proposalId,
            ExpectedVersion = expectedVersion,
            Actions = RequireActions(actions),
            Reasoning = RequireText(reasoning, nameof(reasoning)),
        };
        return Serialize(await SendAsync(request, "revise ad proposal", cancellationToken));
    }

    [McpServerTool]
    [Description("List proposals with their status and outcome, newest first. Filter by status " +
                 "(Pending, Approved, Rejected, Expired, Executing, Executed, PartiallyExecuted, Failed, Reverted) " +
                 "and onlyMine=true for proposals you created. Paged.")]
    public async Task<string> ListAdProposals(
        [Description("Optional status filter")] AdProposalStatus? status = null,
        [Description("Only proposals created by the caller (default false)")] bool onlyMine = false,
        [Description("Page number (default 1)")] int pageNumber = 1,
        [Description("Page size (default 50)")] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Marketing_Ads, AdsResource);

        var request = new GetAdProposalsRequest { Status = status, OnlyMine = onlyMine, PageNumber = pageNumber, PageSize = pageSize };
        return Serialize(await SendAsync(request, "list ad proposals", cancellationToken));
    }

    [McpServerTool]
    [Description("One proposal in full: every version with its actions and rendered diff, the reasoning, approvals, " +
                 "limits evaluation and per-action execution results.")]
    public async Task<string> GetAdProposal(
        [Description("Proposal id")] Guid proposalId,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Marketing_Ads, AdsResource);

        return Serialize(await SendAsync(new GetAdProposalRequest { ProposalId = proposalId }, "get ad proposal", cancellationToken));
    }

    [McpServerTool]
    [Description("Report the start of an agent run (observability of the agents themselves). Returns agentRunId; pass it " +
                 "to SubmitAdProposal and to FinishAgentRun.")]
    public async Task<string> StartAgentRun(
        [Description("Stable agent name, e.g. paid-search-agent")] string agentName,
        [Description("Optional purpose of this run")] string? purpose = null,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Marketing_Ads, AdsResource, AccessLevel.Write);

        var request = new StartAgentRunRequest
        {
            AgentName = RequireText(agentName, nameof(agentName)),
            Purpose = string.IsNullOrWhiteSpace(purpose) ? null : purpose.Trim(),
        };
        return Serialize(await SendAsync(request, "start agent run", cancellationToken));
    }

    [McpServerTool]
    [Description("Report the end of an agent run: status, short summary, which data scopes were read and the approximate " +
                 "cost (tokens, CZK).")]
    public async Task<string> FinishAgentRun(
        [Description("Id from StartAgentRun")] Guid agentRunId,
        [Description("Final status, e.g. Succeeded or Failed")] AgentRunStatus status,
        [Description("Optional short summary")] string? summary = null,
        [Description("Optional data scopes read, e.g. performance, search_terms, change_history")] List<string>? dataScopesRead = null,
        [Description("Optional total tokens used")] long? tokensUsed = null,
        [Description("Optional approximate cost in CZK")] decimal? costCzk = null,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Marketing_Ads, AdsResource, AccessLevel.Write);

        var request = new FinishAgentRunRequest
        {
            AgentRunId = agentRunId,
            Status = status,
            Summary = string.IsNullOrWhiteSpace(summary) ? null : summary.Trim(),
            DataScopesRead = dataScopesRead,
            TokensUsed = tokensUsed,
            CostCzk = costCzk,
        };
        return Serialize(await SendAsync(request, "finish agent run", cancellationToken));
    }
```

and add next to the other helpers:

```csharp
    private static List<AdActionInputDto> RequireActions(List<AdActionInputDto>? actions) =>
        actions is { Count: > 0 }
            ? actions
            : throw ValidationError("actions must contain at least one action.");
```

(Maximum counts per action type are the limits engine's job — C3 — not the tool's.)

- [ ] **Step 4: Build and run — expect pass**

Build, then `--filter "FullyQualifiedName~MarketingAdsMcpToolsProposalTests"`. Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/MCP/Tools/MarketingAdsMcpTools.cs backend/test/Anela.Heblo.Tests/MCP/Tools/MarketingAdsMcpToolsProposalTests.cs
git commit -m "feat: add ad proposal and agent run MCP tools

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
## Task 7: The chief's tools — pending list, approve, reject

**Files:**
- Modify: `backend/src/Anela.Heblo.API/MCP/Tools/MarketingAdsMcpTools.cs` (insert after `FinishAgentRun`)
- Test: `backend/test/Anela.Heblo.Tests/MCP/Tools/MarketingAdsMcpToolsApprovalTests.cs`

**Interfaces:**
- Consumes: C3 `GetAdProposalsRequest`/`GetAdProposalsResponse` (`Items` of `AdProposalSummaryDto`, `TotalCount`), `ApproveAdProposalRequest`, `RejectAdProposalRequest`; the handlers call `IApprovalChannelResolver` (Task 3).
- Produces: tools `ListPendingAdProposals`, `ApproveAdProposal`, `RejectAdProposal`. JSON shape of the pending list: `{ "Pending": [ { "Number", "Id", "Version", "Platform", "AccountExternalId", "ActionType", "ActionCount", "CreatedBy", "CreatedByKind", "CreatedAt", "ExpiresAt", "Diff": [ … ] } ], "Shown", "TotalPending", "HowToDecide" }`.

- [ ] **Step 1: Write the failing tests** — `MarketingAdsMcpToolsApprovalTests.cs` (adjust `AdProposalSummaryDto` initialiser to C3's real property names):

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.ApproveAdProposal;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdProposals;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.RejectAdProposal;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using ModelContextProtocol;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP.Tools;

public class MarketingAdsMcpToolsApprovalTests : MarketingAdsMcpToolsTestBase
{
    private static AdProposalSummaryDto Summary(Guid id, int version, DateTimeOffset createdAt, params string[] diff) => new()
    {
        Id = id,
        CurrentVersion = version,
        Status = AdProposalStatus.Pending,
        Platform = AdPlatform.GoogleAds,
        AccountExternalId = "123-456-7890",
        ActionType = AdActionType.AddNegativeKeyword,
        ActionCount = diff.Length,
        CreatedByPrincipal = "heblo-marketing-agent",
        CreatedAt = createdAt,
        ExpiresAt = createdAt.AddHours(72),
        DiffLines = diff.ToList(),
    };

    [Fact]
    public async Task ListPendingAdProposals_AsksForPending_AndNumbersOldestFirstWithDiffs()
    {
        // Arrange
        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();
        var t0 = new DateTimeOffset(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);
        Mediator.Setup(m => m.Send(It.IsAny<GetAdProposalsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetAdProposalsResponse
            {
                Items = new List<AdProposalSummaryDto>
                {
                    Summary(newer, 1, t0.AddHours(2), "Pause ad 'Vánoce 2025' in ad group 'Krémy'"),
                    Summary(older, 3, t0, "Add negative \"zdarma\" (Phrase) to campaign 'Brand'", "Add negative \"recept\" (Broad) to campaign 'Brand'"),
                },
                TotalCount = 2,
            });

        // Act
        var json = await Tools.ListPendingAdProposals();

        // Assert
        Mediator.Verify(m => m.Send(It.Is<GetAdProposalsRequest>(r =>
            r.Status == AdProposalStatus.Pending && !r.OnlyMine), It.IsAny<CancellationToken>()), Times.Once);
        using var doc = JsonDocument.Parse(json);
        var pending = doc.RootElement.GetProperty("Pending");
        pending.GetArrayLength().Should().Be(2);
        pending[0].GetProperty("Number").GetInt32().Should().Be(1);
        pending[0].GetProperty("Id").GetGuid().Should().Be(older);
        pending[0].GetProperty("Version").GetInt32().Should().Be(3);
        pending[0].GetProperty("Diff").GetArrayLength().Should().Be(2);
        pending[1].GetProperty("Number").GetInt32().Should().Be(2);
        pending[1].GetProperty("Platform").GetString().Should().Be("GoogleAds");
        doc.RootElement.GetProperty("TotalPending").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task ListPendingAdProposals_WithNothingPending_ReturnsEmptyList()
    {
        Mediator.Setup(m => m.Send(It.IsAny<GetAdProposalsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetAdProposalsResponse { Items = new List<AdProposalSummaryDto>(), TotalCount = 0 });

        var json = await Tools.ListPendingAdProposals();

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("Pending").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ListPendingAdProposals_ClampsMaxItems()
    {
        Mediator.Setup(m => m.Send(It.IsAny<GetAdProposalsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetAdProposalsResponse { Items = new List<AdProposalSummaryDto>(), TotalCount = 0 });

        await Tools.ListPendingAdProposals(maxItems: 10_000);

        Mediator.Verify(m => m.Send(It.Is<GetAdProposalsRequest>(r => r.PageSize == 50 && r.PageNumber == 1), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApproveAdProposal_ForwardsIdAndVersion()
    {
        var id = Guid.NewGuid();
        Mediator.Setup(m => m.Send(It.IsAny<ApproveAdProposalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApproveAdProposalResponse());

        await Tools.ApproveAdProposal(id, 4);

        Mediator.Verify(m => m.Send(It.Is<ApproveAdProposalRequest>(r => r.ProposalId == id && r.Version == 4), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApproveAdProposal_SurfacesHandlerChannelDenial()
    {
        // The handler's resolver refuses a service identity even when it holds every permission.
        Mediator.Setup(m => m.Send(It.IsAny<ApproveAdProposalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApproveAdProposalResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.Forbidden,
                Params = new Dictionary<string, string> { ["Reason"] = "Service identities (app-only tokens) can never approve or reject ad proposals." },
            });

        var act = () => Tools.ApproveAdProposal(Guid.NewGuid(), 1);

        var ex = (await act.Should().ThrowAsync<McpException>()).Which;
        ex.Message.Should().StartWith("[Forbidden]").And.Contain("Service identities");
    }

    [Fact]
    public async Task RejectAdProposal_ForwardsTrimmedReason()
    {
        var id = Guid.NewGuid();
        Mediator.Setup(m => m.Send(It.IsAny<RejectAdProposalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RejectAdProposalResponse());

        await Tools.RejectAdProposal(id, 2, "  'recept' converts well  ");

        Mediator.Verify(m => m.Send(It.Is<RejectAdProposalRequest>(r =>
            r.ProposalId == id && r.Version == 2 && r.Reason == "'recept' converts well"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RejectAdProposal_RequiresReason(string reason)
    {
        var act = () => Tools.RejectAdProposal(Guid.NewGuid(), 1, reason);

        (await act.Should().ThrowAsync<McpException>()).Which.Message.Should().Contain("reason");
        Mediator.Invocations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("ListPendingAdProposals")]
    [InlineData("ApproveAdProposal")]
    [InlineData("RejectAdProposal")]
    public async Task ApprovalTools_ThrowForbidden_ForAgentsAndMarketersWithoutApprovalWrite(string tool)
    {
        // Arrange — holds Ads read+write (agent / marketer) but not AdApprovals write
        DenyRole(ApprovalsWrite);

        // Act
        var act = () => tool switch
        {
            "ListPendingAdProposals" => Tools.ListPendingAdProposals(),
            "ApproveAdProposal" => Tools.ApproveAdProposal(Guid.NewGuid(), 1),
            _ => Tools.RejectAdProposal(Guid.NewGuid(), 1, "no"),
        };

        // Assert
        (await act.Should().ThrowAsync<McpException>()).Which.Message.Should().Contain("FORBIDDEN").And.Contain(ApprovalsWrite);
        Mediator.Invocations.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Build — expect failure**

Expected: `error CS1061: 'MarketingAdsMcpTools' does not contain a definition for 'ListPendingAdProposals'`.

- [ ] **Step 3: Add the tools** — add `using` lines for `UseCases.ApproveAdProposal` and `UseCases.RejectAdProposal`, then insert after `FinishAgentRun`:

```csharp
    [McpServerTool]
    [Description("The chief's inbox: pending ad proposals, oldest first, numbered 1..n, each with id, version and the diff " +
                 "Heblo rendered from the structured actions. To decide, call ApproveAdProposal / RejectAdProposal with the " +
                 "item's Id and Version (e.g. 'approve 1-4, reject 5' = four approvals and one rejection).")]
    public async Task<string> ListPendingAdProposals(
        [Description("Maximum items to show (1-50, default 50)")] int maxItems = MaxPendingItems,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Marketing_AdApprovals, ApprovalsResource, AccessLevel.Write);

        var request = new GetAdProposalsRequest
        {
            Status = AdProposalStatus.Pending,
            OnlyMine = false,
            PageNumber = 1,
            PageSize = Math.Clamp(maxItems, 1, MaxPendingItems),
        };
        var response = await SendAsync(request, "list pending ad proposals", cancellationToken);

        var pending = response.Items
            .OrderBy(p => p.CreatedAt)
            .Select((p, index) => new
            {
                Number = index + 1,
                p.Id,
                Version = p.CurrentVersion,
                p.Platform,
                p.AccountExternalId,
                p.ActionType,
                p.ActionCount,
                CreatedBy = p.CreatedByPrincipal,
                p.CreatedByKind,
                p.CreatedAt,
                p.ExpiresAt,
                Diff = p.DiffLines,
            })
            .ToList();

        return Serialize(new
        {
            Pending = pending,
            Shown = pending.Count,
            TotalPending = response.TotalCount,
            HowToDecide = "Approve or reject by Id and Version. A version that is no longer current is refused; list again.",
        });
    }

    [McpServerTool]
    [Description("Approve exactly one version of a pending proposal. Allowed only for a signed-in person holding the " +
                 "approve-via-Claude permission; service identities are always refused. Larger proposals may need the web UI " +
                 "or a second approver — the response says so. Execution is done by Heblo afterwards.")]
    public async Task<string> ApproveAdProposal(
        [Description("Proposal id")] Guid proposalId,
        [Description("The version being approved (as listed)")] int version,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Marketing_AdApprovals, ApprovalsResource, AccessLevel.Write);

        var request = new ApproveAdProposalRequest { ProposalId = proposalId, Version = version };
        return Serialize(await SendAsync(request, "approve ad proposal", cancellationToken));
    }

    [McpServerTool]
    [Description("Reject exactly one version of a pending proposal, with a reason the agent can learn from. Same caller " +
                 "rules as ApproveAdProposal.")]
    public async Task<string> RejectAdProposal(
        [Description("Proposal id")] Guid proposalId,
        [Description("The version being rejected (as listed)")] int version,
        [Description("Why it is rejected")] string reason,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Marketing_AdApprovals, ApprovalsResource, AccessLevel.Write);

        var request = new RejectAdProposalRequest
        {
            ProposalId = proposalId,
            Version = version,
            Reason = RequireText(reason, nameof(reason)),
        };
        return Serialize(await SendAsync(request, "reject ad proposal", cancellationToken));
    }
```

- [ ] **Step 4: Build and run all MCP tool tests — expect pass**

Build, then `--filter "FullyQualifiedName~MarketingAdsMcpTools"`. Expected: all pass, **including** `ToolSurface_MatchesSpecSection75`. Check file size: `wc -l backend/src/Anela.Heblo.API/MCP/Tools/MarketingAdsMcpTools.cs` should be under 800 lines.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/MCP/Tools/MarketingAdsMcpTools.cs backend/test/Anela.Heblo.Tests/MCP/Tools/MarketingAdsMcpToolsApprovalTests.cs
git commit -m "feat: add pending list, approve and reject MCP tools for the chief

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
## Task 8: Documentation — runbook, grant step, MCP tool list

**Files:**
- Create: `docs/integrations/marketing-agents-identity.md`
- Modify: `docs/integrations/mcp-server.md`, `CLAUDE.md` (tool count only), process docs flagged by the checker

**Interfaces:**
- Consumes: role strings (Task 1), config keys (Tasks 3–4), tool names (Tasks 5–7).
- Produces: the runbook Ondrej follows; nothing in code depends on it.

- [ ] **Step 1: Create `docs/integrations/marketing-agents-identity.md`** with exactly this content:

````markdown
# Marketing agents — identity, approval channel, rollout

Operational runbook for PR C4 of the marketing agents platform
(spec: `docs/superpowers/specs/2026-10-07-marketing-agents-platform-design.md`, section 7).
Every step here is performed by **Ondrej**, staging first, production after review.

## 1. Who may do what

| Caller | Token | Read ad data / propose | Approve / reject |
|---|---|---|---|
| Agent (`heblo-marketing-agent`) | client credentials, app-only (`roles: Marketing.Agent`, no `scp`) | yes, if its AppUser is in `Marketing_Agent` | **never** — refused even with `super_user` |
| Person in the web app | SPA token (`azp` ∈ `Ads:Approval:WebClientIds`) or cookie session | per groups | channel **Web**, needs `marketing.ad_approvals.write` |
| Person in Claude (connector to `/mcp`) | delegated token, `azp` = API client id | per groups | channel **MCP**, needs `marketing.ad_approvals.write` **and** `marketing.ad_approvals_via_mcp.read`; stricter limits |
| Person via HebloMCP | delegated token, `azp` = HebloMCP client id (must be in `McpClientIds`) | per groups | channel **MCP** as above |
| Any other bearer client | — | per groups | refused |

Enforcement: `ApprovalChannelResolver` (Application, `Features/MarketingAds/Approvals`), called by every
approve/reject handler, so REST and MCP behave the same.

## 2. Configuration

| Key | Default (repo) | Meaning |
|---|---|---|
| `Ads:Approval:WebClientIds` | `["87193df0-3128-44d2-8673-580e97631a07"]` | `azp`/`appid` of the web SPA |
| `Ads:Approval:McpClientIds` | `[]` (+ `AzureAd:ClientId`, always added in code) | extra MCP clients, e.g. HebloMCP |
| `Ads:AgentRateLimitPerMinute` | `60` | `/mcp` requests per minute per agent `oid`; HTTP 429 above it. People are not limited |

Client ids are not secrets, but all environment overrides go through Key Vault like every other
setting (`--` separator, arrays by index):

```bash
az keyvault secret set --vault-name kv-heblo-stg --name "Ads--Approval--McpClientIds--0" --value "<heblomcp-client-id>"
```

Restart the web app after a Key Vault change (`az webapp restart --name heblo-test --resource-group rgHeblo`;
production: `heblo`). The config binder merges arrays by index; the resolver de-duplicates and
compares case-insensitively, so an override with fewer entries is safe.

**Verify the SPA client id per environment** (a wrong value blocks every web approval): sign in to
the web app, copy the access token of any `/api` call from the browser dev tools (Network →
request → `Authorization` header) and decode it:

```bash
python3 -c "import base64,json,sys;p=sys.argv[1].split('.')[1];p+='='*(-len(p)%4);print(json.loads(base64.urlsafe_b64decode(p)).get('azp'))" "<token>"
```

The printed value must be in `Ads:Approval:WebClientIds`; otherwise set
`Ads--Approval--WebClientIds--0` in that environment's Key Vault.

## 3. Rollout grant step (reviewed, staging first)

`JsonGroupSeeder` only inserts **missing** groups, so a deployment alone grants nothing to
existing groups. Never use `--reset-group` here — it wipes hand-made grants.

1. Deploy to staging.
2. Create the three new groups (non-destructive, insert-if-missing):
   ```bash
   scripts/seed-authorization.sh staging
   ```
   Expected: `Marketing_Agent`, `Marketing_Specialist`, `Marketing_Chief` created; every other
   group reported as existing/untouched.
3. In the web app, `/admin/access` → Groups:
   - `Marketer`: add `marketing.ads.read`, `marketing.ads.write`.
   - `Spravce`: add `marketing.ads.read`, `marketing.ads.write`, `marketing.ad_approvals.read`,
     `marketing.ad_approvals.write`, `marketing.ad_approvals.admin`, `marketing.ad_approvals_via_mcp.read`.
4. Put people into `Marketing_Specialist` / `Marketing_Chief` as agreed with marketing (these
   groups hold only the ads permissions; people keep their `Marketer` membership for the rest).
5. Verify (read-only, against `Heblo_TST`; production DB is `Heblo_V3`):
   ```sql
   SELECT g."Name", p."PermissionValue"
   FROM public."PermissionGroups" g
   JOIN public."GroupPermissions" p ON p."GroupId" = g."Id"
   WHERE p."PermissionValue" LIKE 'marketing.ad%'
   ORDER BY 1, 2;
   ```
   Expected: `Marketing_Agent` 2 rows, `Marketer` 2, `Marketing_Specialist` 4, `Marketing_Chief` 5,
   `Spravce` 6 — and no other group.
6. After review, repeat 1–5 for production (`scripts/seed-authorization.sh production`).

SQL alternative to step 3 (idempotent; only if the admin UI is unavailable — paste it into the PR
for review before running):

```sql
INSERT INTO public."GroupPermissions" ("GroupId", "PermissionValue")
SELECT g."Id", v.p
FROM public."PermissionGroups" g
JOIN (VALUES
  ('Marketer', 'marketing.ads.read'), ('Marketer', 'marketing.ads.write'),
  ('Spravce', 'marketing.ads.read'), ('Spravce', 'marketing.ads.write'),
  ('Spravce', 'marketing.ad_approvals.read'), ('Spravce', 'marketing.ad_approvals.write'),
  ('Spravce', 'marketing.ad_approvals.admin'), ('Spravce', 'marketing.ad_approvals_via_mcp.read')
) AS v(g, p) ON v.g = g."Name"
ON CONFLICT DO NOTHING;
```

SQL grants bypass the permission cache invalidation: they take effect within 5 minutes.

## 4. Agent service identity (Entra)

Staging and production use the same tenant and the same Heblo API app registration
(`AzureAd:ClientId`). Confirm before you start:

```bash
TENANT_ID=31fd4df1-b9c0-4abd-a4b0-0e1aceaabe9a
az webapp config appsettings list --name heblo --resource-group rgHeblo \
  --query "[?name=='AzureAd__ClientId'].value" -o tsv
API_APP_ID=8b34be89-f86f-422f-af40-7dbcd30cb66a   # use the value printed above if it differs
```

### 4.1 App role `Marketing.Agent` on the Heblo API

`--app-roles` replaces the whole list, so append to the current one:

```bash
az ad app show --id "$API_APP_ID" --query appRoles > approles.current.json
jq -e '.[] | select(.value=="Marketing.Agent")' approles.current.json && echo "already exists — skip to 4.2"
ROLE_ID=$(uuidgen | tr '[:upper:]' '[:lower:]')
jq --arg id "$ROLE_ID" '. + [{
  "allowedMemberTypes": ["Application"],
  "description": "Unattended marketing agent: reads ad data and submits proposals. Never approves.",
  "displayName": "Marketing.Agent",
  "id": $id,
  "isEnabled": true,
  "value": "Marketing.Agent"
}]' approles.current.json > approles.new.json
az ad app update --id "$API_APP_ID" --app-roles @approles.new.json
ROLE_ID=$(az ad app show --id "$API_APP_ID" --query "appRoles[?value=='Marketing.Agent'].id" -o tsv)
```

### 4.2 The `heblo-marketing-agent` app and service principal

```bash
AGENT_APP_ID=$(az ad app create --display-name heblo-marketing-agent --sign-in-audience AzureADMyOrg --query appId -o tsv)
AGENT_SP_OID=$(az ad sp create --id "$AGENT_APP_ID" --query id -o tsv)
API_SP_OID=$(az ad sp show --id "$API_APP_ID" --query id -o tsv)

# Record the required permission on the agent app (visible in the portal)...
az ad app permission add --id "$AGENT_APP_ID" --api "$API_APP_ID" --api-permissions "$ROLE_ID=Role"
# ...and grant it (this assignment is what puts Marketing.Agent into the token).
az rest --method POST \
  --uri "https://graph.microsoft.com/v1.0/servicePrincipals/$AGENT_SP_OID/appRoleAssignments" \
  --headers "Content-Type=application/json" \
  --body "{\"principalId\":\"$AGENT_SP_OID\",\"resourceId\":\"$API_SP_OID\",\"appRoleId\":\"$ROLE_ID\"}"
```

Verify it holds **exactly one** role and never `super_user`:

```bash
az rest --method GET \
  --uri "https://graph.microsoft.com/v1.0/servicePrincipals/$AGENT_SP_OID/appRoleAssignments" \
  --query "value[].appRoleId" -o tsv
jq -r '.appRoles[] | select(.value=="super_user") | .id' access-matrix-entra.generated.json
```

The first command must print only `$ROLE_ID`; it must not print the `super_user` id from the second.

### 4.3 Credential (held by the agent runtime, never by Heblo)

Prefer a certificate (`az ad app credential reset --id "$AGENT_APP_ID" --cert @agent-cert.pem --append`).
With a secret:

```bash
az ad app credential reset --id "$AGENT_APP_ID" --display-name "agent-runtime-$(date +%Y%m)" \
  --years 1 --append --query password -o tsv
```

Paste the output straight into the agent runtime's secret store. Do not put it in Heblo's Key
Vault, App Settings, the repo or a chat.

## 5. Getting a token and connecting to `/mcp`

Client-credentials token (Entra tokens live 60–90 min; the runtime fetches a fresh one per run):

```bash
curl -s -X POST "https://login.microsoftonline.com/$TENANT_ID/oauth2/v2.0/token" \
  -d grant_type=client_credentials \
  -d client_id="$AGENT_APP_ID" \
  --data-urlencode client_secret="$AGENT_SECRET" \
  --data-urlencode scope="api://$API_APP_ID/.default" | jq -r .access_token > agent.token
python3 -c "import base64,json;p=open('agent.token').read().strip().split('.')[1];p+='='*(-len(p)%4);c=json.loads(base64.urlsafe_b64decode(p));print(json.dumps({k:c.get(k) for k in ['aud','azp','appid','oid','roles','scp','idtyp']},indent=2))"
```

Expected: `roles` = `["Marketing.Agent"]`, `scp` = `null`, `oid` = `$AGENT_SP_OID`.

Smoke test (staging):

```bash
curl -s -i -X POST https://heblo.stg.anela.cz/mcp \
  -H "Authorization: Bearer $(cat agent.token)" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"smoke","version":"0"}}}'
```

Expected: HTTP 200 with an `Mcp-Session-Id` header. A 401 with `IDW10201` means the token has no
role (step 4.2 missing); a 403 means the AppUser is inactive.

Agent runtime MCP config (streamable HTTP; the runtime substitutes the token):

```json
{
  "mcpServers": {
    "heblo": {
      "type": "http",
      "url": "https://heblo.anela.cz/mcp",
      "headers": { "Authorization": "Bearer ${HEBLO_AGENT_TOKEN}" }
    }
  }
}
```

## 6. Putting the agent into `Marketing_Agent`

The first authenticated call creates the agent's AppUser automatically. App tokens carry no name,
so its email and display name are its object id (`$AGENT_SP_OID`).

1. `/admin/access` → Users → find the user whose email is `$AGENT_SP_OID`.
2. Add it to **`Marketing_Agent` only**. No other group, never `super_user`.
3. Effective immediately (editing a user drops their permission cache).

Check: over MCP the agent can call `ListAdAccounts` and `SubmitAdProposal`; `ListPendingAdProposals`
and `ApproveAdProposal` answer `[FORBIDDEN]`; 61 calls within a minute answer HTTP 429.

## 7. Revocation and rotation

- Stop an agent now: `az ad sp update --id "$AGENT_SP_OID" --set accountEnabled=false` (new tokens
  refused; issued tokens expire within 90 min) **and** deactivate its AppUser in `/admin/access`
  (takes effect at once on that instance, ≤ 5 min elsewhere).
- Rotate: create a new credential with `--append`, switch the runtime, then delete the old one
  (`az ad app credential delete --id "$AGENT_APP_ID" --key-id <old-key-id>`).
````

- [ ] **Step 2: Update `docs/integrations/mcp-server.md`.**
  - First line: append "marketing ads (agents' data, proposals, agent runs and approvals)" to the list of what the tools do.
  - After the "Pricing Simulator (6)" block add:

```markdown
**Marketing Ads (15)** — data and proposals need `Marketing_Ads` (read; submit/revise/agent runs need write). The chief's tools need `Marketing_AdApprovals` write; approve/reject additionally pass the approval-channel rules in the handler (MCP channel: also `Marketing_AdApprovalsViaMcp` read; service identities are always refused). Dates are `yyyy-MM-dd`, instants ISO-8601.
- `ListAdAccounts` — managed ad accounts per platform
- `GetAdPerformance` — daily facts for one platform/account/level/date range, optional parent filter, paged
- `GetAdSearchTerms` — search terms of a campaign or ad group for a date range (Google Ads, Sklik)
- `GetBlendedPerformance` — real revenue ÷ total ad spend, daily or monthly
- `GetAdChangeHistory` — change events, filter by origin (Heblo / OutOfBand) and actor kind
- `GetAdGuardrails` — allowed actions, limits, autonomy modes, kill switch — propose within them
- `SubmitAdProposal` — returns id, version and the limits evaluation
- `ReviseAdProposal` — `id` + `expectedVersion` + the new actions/reasoning
- `ListAdProposals` / `GetAdProposal` — filter by status and "mine"; full detail with rendered diffs
- `StartAgentRun` / `FinishAgentRun` — observability of the agents themselves
- `ListPendingAdProposals` — the chief's "what's pending?", numbered, with rendered diffs
- `ApproveAdProposal` / `RejectAdProposal` — `id` + `version` (+ reason on reject)

**Service identities (agents):** agents connect to `/mcp` with their own client-credentials token. App-only callers are rate limited per `oid` (`Ads:AgentRateLimitPerMinute`, default 60 → HTTP 429). Setup, grants and token instructions: `docs/integrations/marketing-agents-identity.md`.
```

  - In "Tests": replace the coverage number with the output of
    `grep -rhoE "\[(Fact|Theory)\]" backend/test/Anela.Heblo.Tests/MCP/Tools | wc -l` ("N test methods").

- [ ] **Step 3: Update the tool count** — count and edit:

```bash
grep -rho "\[McpServerTool\]" backend/src/Anela.Heblo.API/MCP/Tools | wc -l
```

Expected: `46`. In `CLAUDE.md`, change `MCP tools, endpoints, client config (31 tools)` to `(46 tools)` (only that number; if the printed count differs, use the printed count).

- [ ] **Step 4: Process docs.** Commit first so the checker sees the branch, then ask it which docs the PR touches without updating:

```bash
git add docs/integrations/marketing-agents-identity.md docs/integrations/mcp-server.md CLAUDE.md
git commit -m "docs: marketing agents identity runbook and MCP tool list

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
python3 scripts/process-docs/check.py pr --base origin/main --comment-file /tmp/c4-process-docs.md
```

For every flagged doc: if it is the C3 proposals/approvals process doc (e.g. `docs/processes/flow-ad-proposal-approval.md` or `module-marketing-ads.md`), replace any "deny-all placeholder" wording with this paragraph in its rules/logic section:

```markdown
**Approval channel** (`ApprovalChannelResolver`, used by every approve/reject handler):
app-only tokens (no `scp` or `idtyp = app`) are refused before any permission is checked;
`azp`/`appid` in `Ads:Approval:WebClientIds` → Web (needs `marketing.ad_approvals.write`);
in `Ads:Approval:McpClientIds` or equal to `AzureAd:ClientId` → Mcp (needs that **and**
`marketing.ad_approvals_via_mcp.read`; stricter MCP limits apply); a cookie session → Web;
any other bearer client → refused. Setup: `docs/integrations/marketing-agents-identity.md`.
```

For any other flagged doc whose behaviour did not change, bump `verified_at` to `git rev-parse --short HEAD`. Then:

```bash
python3 scripts/process-docs/check.py index
python3 scripts/process-docs/check.py pr --base origin/main --comment-file /tmp/c4-process-docs.md
```

Expected: the second run prints `no process docs affected`.

- [ ] **Step 5: Commit**

```bash
git add docs/processes
git commit -m "docs: describe approval channel in marketing ads process docs

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

(Skip the commit if Step 4 changed nothing.)

---

## Task 9: Full validation and PR

**Files:** none new.

- [ ] **Step 1: Build the solution**

```bash
dotnet build-server shutdown
DOTNET_CLI_DISABLE_BUILD_SERVERS=1 MSBUILDDISABLENODEREUSE=1 dotnet build Anela.Heblo.sln -nodeReuse:false -p:UseSharedCompilation=false
```

Expected: `Build succeeded`, 0 errors (AccessMatrixGen MSB3073 warning is noise).

- [ ] **Step 2: Format**

```bash
dotnet format Anela.Heblo.sln --verify-no-changes
```

If it reports changes: `dotnet format Anela.Heblo.sln`, review `git diff`, commit as `chore: dotnet format` (with the Co-Authored-By trailer).

- [ ] **Step 3: Run the whole unit test suite**

```bash
DOTNET_CLI_DISABLE_BUILD_SERVERS=1 MSBUILDDISABLENODEREUSE=1 dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "Category!=Integration"
```

Expected: 0 failed. Pay attention to `ErrorHandlingTests` and any reflection contract tests (`*Response must inherit BaseResponse`) — C4 adds no responses, so failures there mean a C2/C3 regression to report, not to patch here.

- [ ] **Step 4: Frontend** — only if `frontend/` changed (it should not): `cd frontend && CI=false npm run build && npm run lint`.

- [ ] **Step 5: Push and open the PR**

```bash
git push -u origin feature/marketing-core-c4-identity-mcp
gh pr create --base main --title "feat: marketing ads identity, approval channel and MCP tools (C4)" --body-file /tmp/c4-pr-body.md
```

Write `/tmp/c4-pr-body.md` first with these sections:
- **Summary** — seed groups; `ICurrentUserService.ClientAppId/IsAppOnly/IsBearerToken`; `ApprovalChannelResolver` replaces the deny-all default; `/mcp` agent rate limiter; 15 `MarketingAdsMcpTools`; runbook.
- **C2/C3 handler mapping** — the table from "Before you start" with the real names.
- **Spec deviations** — copy the six items from this plan.
- **Rollout (performed by Ondrej, staging first)** — checklist: `[ ] verify SPA azp per environment`, `[ ] scripts/seed-authorization.sh staging`, `[ ] grant Marketer + Spravce at /admin/access`, `[ ] run the verification SQL`, `[ ] HebloMCP client id into Key Vault if used`, `[ ] create heblo-marketing-agent + Marketing.Agent role`, `[ ] add the agent AppUser to Marketing_Agent`, `[ ] smoke: agent reads + submits, cannot approve, 429 after 60/min`, `[ ] repeat for production after review`. Link `docs/integrations/marketing-agents-identity.md`.
- **Test plan** — commands from Steps 1–3 with results.
- End with the line `🤖 Generated with [Claude Code](https://claude.com/claude-code)`.

- [ ] **Step 6: Watch CI**

```bash
sleep 60; gh pr checks --watch
```

"No checks reported" right after creation means not yet registered — wait and re-run. Expected: BE, FE and Docker checks green.
