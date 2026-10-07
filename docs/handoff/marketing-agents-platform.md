# Handoff: Marketing agents platform — measurement backbone + approval layer

Brainstorm held 2026-10-07 between Ondrej and Claude (outside this repo). This file carries the
agreed understanding into the Heblo repo. **Nothing is designed in detail or built yet.** Your job
is to continue the `superpowers:brainstorming` flow on the **architectural path** from the
"Present design sections" step, then write the spec.

## 1. Why this exists

An internal analysis (Oct 7, 2026) of Anela's PPC management found the external agency
(9,000 CZK/month) barely touched the accounts in a year: Google Ads — 41 changes on 12 days since
Jan 2026, mostly clicks on Google's auto-recommendations; Sklik — zero changes since 29 Sep 2025;
no reports delivered. Ad spend is ~1.75M CZK/year (Google ~1.6M, Sklik ~0.15M). Nobody could see
what was (not) happening until someone reconstructed it from platform change history.

**Goal:** most marketing operations done by AI agents, with humans approving, and **full
observability over everything** — marketing results *and* what agents/people/third parties did.

All four motives apply (Ondrej answered "A, B, C, D"):
A) replace paid agencies/freelancers, B) grow revenue / return on ad spend from the same budget,
C) free internal marketing people's time, D) visibility and control.

## 2. Decisions already made (do not re-litigate)

1. **Decomposition.** The whole vision is six sub-projects; **this workspace covers only 1 and 6**:
   1. **Measurement backbone** ← this workspace
   2. Paid search agent (Google Ads + Sklik) — later
   3. Paid social agent (Meta) — later
   4. Email/retention agent (Ecomail) — later
   5. Content/organic agent (blog, Instagram, calendar) — later
   6. **Proposal & approval layer** ← this workspace
2. **Users:** Ondrej + marketing people, **in Heblo** (existing logins, marketing section).
3. **Data granularity: enough to *manage*, not just report.** Daily sync at
   campaign → ad group → keyword / search term → ad (Google, Sklik) and
   campaign → ad set → ad/creative (Meta), with platform-reported conversions. Next to it, a
   **blended reality check** from real Shoptet/invoice revenue ÷ total marketing spend, because
   platforms over-count. Order-level attribution (GA4/UTM) is explicitly later.
4. **Architecture = "Heblo is the eyes and the hands, agents are the brain."**
   - Agents run **outside** Heblo, runtime-agnostic (Claude routines/scheduled tasks today,
     NanoClaw or anything else tomorrow).
   - Heblo holds **all** platform credentials and connectors (Google Ads, Meta, Sklik, plus the
     existing GA4/Ecomail/Shoptet/Flexi).
   - Agents talk to Heblo **only via the Heblo MCP server**: read marketing data, submit
     **proposals**, read proposal outcomes. Agents never write to ad platforms directly.
   - Heblo executes approved proposals through its own adapters and logs everything.
   - Rejected alternatives: (1) agents built inside Heblo — every prompt change needs a deploy,
     runtime lock-in; (2) external agents with direct platform write connectors — approval and
     audit become optional, write creds spread to cloud runtimes.
5. **Autonomy model:** configurable per channel × action type: *propose only* / *auto within
   limits* / *auto*, with hard limits. **Everything starts at "propose only".** Raise per action
   type based on approval-without-edit rates.
6. **Role model** (built on Heblo's existing `Feature` × `AccessLevel` + `[FeatureAuthorize]`):

   | Role | Read campaign data | Propose | Approve in web UI | Approve via MCP (Claude) | Limits / kill switch |
   |---|---|---|---|---|---|
   | Agent (unattended, own service identity) | ✅ | ✅ | ❌ | ❌ | ❌ |
   | Junior marketing | ✅ | ✅ | ❌ | ❌ | ❌ |
   | Marketing specialist (optional) | ✅ | ✅ | ✅ | ❌ | ❌ |
   | Chief of marketing | ✅ | ✅ | ✅ | ✅ | ❌ |
   | Admin (Ondrej) | ✅ | ✅ | ✅ | ✅ | ✅ |

   Intended UX for the chief: ask Claude "what's pending?", then "approve 1–4, reject 5".

## 3. Security requirements (agreed)

- **Agents vs. humans.** Heblo MCP today forwards the signed-in user's delegated token
  (`access_as_user`, see `HebloMCP/README.md`), so an agent inherits that user's rights.
  Therefore:
  - Unattended agents get their **own Entra ID app registration / service identity** with only
    read + propose rights. A service identity can **never** approve, even if assigned an
    approver role.
  - Approval via MCP is allowed only for users holding the explicit "approve via MCP" permission,
    and only with an **interactive delegated user token**.
  - The approval API checks the token's client app (`azp`/`appid` claim) to tell web-UI
    approvals from MCP approvals; enforcement lives in the API, not just in tool exposure.
- **Proposals are structured and immutable.** Typed action (type, platform, target id, old value,
  new value) + agent's free-text reasoning shown separately. Heblo renders the human-readable
  diff from the payload, never from the agent's text. Any change = new version. Approval names
  proposal id **and version**.
- **Stale protection.** Proposals expire (e.g. 72 h). At execution Heblo verifies current value ==
  proposal's old value (optimistic concurrency); mismatch → reject, ask for a fresh proposal.
- **Server-side hard limits regardless of who approved** (prompt injection is real: agents read
  search terms, chats, reviews, competitor pages): allowlisted action types, max budget change per
  action and per week, own accounts only. **Lower limits for MCP approvals than web approvals**
  (e.g. budget change >20 % or new campaigns require the web UI even for the chief). Over-threshold
  → second approver.
- **Kill switch** stopping all agent-originated execution; store before-state for revert.
- **Out-of-band change detection:** sync each platform's change history and flag changes that
  did not come through Heblo (agency, manual UI edits, Google auto-applied recommendations).
- **Least privilege:** Google user with standard (not admin) access; Meta system user scoped to
  the ad account only; secrets in Key Vault; agent MCP tokens short-lived, rate-limited,
  revocable; agent role sees aggregates only — no customer PII.
- **Audit log** append-only: proposed by / approved by / channel (web vs MCP) / executed /
  platform response / outcome. Agent runs also report to Heblo (what they looked at, proposals
  made, approximate cost) — observability covers agents themselves.

## 4. What already exists in Heblo (verified 2026-10-07 at `2c643a6bc`; re-verify)

| Area | Where | State |
|---|---|---|
| Per-feature authz | `Domain/Features/Authorization` (`Feature` enum, `AccessLevel` Read/Write/Admin, `FeatureAuthorizeAttribute`, `AccessRoles.generated.cs`) | Mature; MCP tools gate on it (e.g. `MeetingTasksMcpTools`) |
| MCP server | `Anela.Heblo.API/MCP` (in-API tools) + separate `~/Work/GitHub/HebloMCP` (Python, Entra auth) | Exists; user-delegated only |
| Google Ads adapter | `Adapters/Anela.Heblo.Adapters.GoogleAds` (SDK, developer token, OAuth refresh token) | Billing only (`GoogleAdsTransactionSource`, account budgets) |
| Meta Ads adapter | `Adapters/Anela.Heblo.Adapters.MetaAds` (system user token, Graph v21) | Billing only (`/transactions`) |
| Sklik | — | **No adapter** |
| GA4 | `Adapters/Anela.Heblo.Adapters.GoogleAnalytics` (traffic, conversions, landing pages) | Syncing |
| Ecomail | `Features/Ecomail` | Syncing; nothing reads it |
| Marketing performance | `Features/MarketingPerformance` — monthly revenue vs ad cost from Flexi received invoices (by supplier VAT id) | Used; Sklik cost shows negative (known quirk) |
| Marketing calendar | `Features/Marketing` ↔ Outlook group calendar | Used |
| AI content | Article, Leaflet, Photobank, Smartsupp drafts | Used |

**Known warning (memory note, 2026-09-16):** `ImportedMarketingTransactions` is **empty in prod**;
the user said the direct Meta/Google API importers "don't work", jobs show `Succeeded`. Cause
unknown. Hypothesis (unverified): billing endpoints need extra billing permissions / Google
monthly invoicing; reporting & management endpoints are a different, standard surface. **Do not
assume credentials work — verify first.**

## 5. Open items for this workspace

1. **Access spike first** (throwaway, read-only, nothing written to platforms): one call per
   platform for yesterday's campaign spend — Google Ads, Meta, Sklik. Needs from Ondrej: Google
   customer id + whether a developer token was issued (and its access level), Meta ad account id +
   system user token with `ads_read`/`ads_management`, Sklik API token. Also confirm **Anela holds
   admin access** to Google Ads and Sklik itself, not only via the agency's manager account.
2. Continue brainstorming → design sections (get approval per section):
   - Data model for campaign-level daily facts across platforms (+ blended reality check), and
     platform change-history sync.
   - Sklik adapter + campaign-level sync for Google/Meta (read side).
   - Proposal entity & lifecycle (draft → pending → approved/rejected/expired → executing →
     executed/failed → reverted), versioning, limits engine, autonomy config.
   - Write adapters — only for the first enabled action types (suggest: add negative keywords,
     pause ad).
   - MCP tool surface for agents (read data, submit proposal, list my proposals/outcomes, report
     agent run) and for the chief (list pending, approve/reject by id+version).
   - New `Feature` values and roles (e.g. `Marketing_Campaigns`, `Marketing_CampaignApprovals`,
     an approve-via-MCP permission); Entra service identity for agents; `azp` check.
   - Approval inbox UI in the marketing section; observability dashboard (results + audit +
     out-of-band changes + agent runs).
   - Testing strategy.
3. Write the spec to `docs/superpowers/specs/YYYY-MM-DD-marketing-agents-platform-design.md`,
   self-review, have Ondrej review it, then `superpowers:writing-plans`. Likely needs splitting
   into several implementation plans (sync backbone / proposal+approval / MCP+identity / UI).

Out of scope here: the agents themselves (sub-projects 2–5), order-level attribution, GA4/UTM
work, any change to live ad accounts.
