# Cluster B — data-access investigation

> Answers the four blocking questions in `CLUSTER-BRIEF.md`, evaluates MCP vs API access,
> and proposes a next step. Investigated 2026-09-22. No code written.

## 1. The platform is Ecomail

Not in the repo, not in Key Vault — found in the company mailbox.

| Evidence | Meaning |
|---|---|
| `support@ecomail.cz` → *"Připomenutí platnosti slevových kódů \| anela"*, recurring (2026-09-09 … 09-21) | Live account, name `anela`, automations running now |
| Shoptet invoice (2026-08-10) line: *"Ecomail (Vytvořil: ECOMAIL.CZ), 1 měsíc, 0,00 Kč"* | The **Shoptet↔Ecomail connector addon is installed** — subscribers/orders flow through it |
| `michaela@ecomail.cz` (2026-06-16): *"Překročili jste maximální počet kontaktů v účtu anela"* | Account was over its contact limit — plan/quota issue, flag to Bára |
| `support@ecomail.cz` (2026-08-20): *"Nepodařilo se načíst zbožový feed"* | The product feed broke. **Casts doubt on whether conversion tracking is intact** — see §4 |

Nothing about Ecomail exists in the codebase: no adapter, no `Ecomail--*` secret in `kv-heblo-stg`
or `kv-heblo-prod`, no config key. This is a greenfield integration.

## 2. The API covers every metric #4/#5/#6 asks for

`https://api2.ecomailapp.cz` — **1000 calls/min** per key, 429 + `Retry-After` on throttle.

**Newsletters (#6) → campaigns.** `GET /campaigns` lists them (id, title, subject, `sent_at`,
`recipients`, status). `GET /campaigns/{id}/stats` returns, in one call:

```
inject, delivery, delivery_rate, open, total_open, open_rate, click, total_click,
click_rate, bounce, bounce_rate, spam, spam_rate, unsub, unsub_rate,
conversions, conversions_value
```

**Automations (#5) → pipelines.** `GET /pipelines` lists them; `GET /pipelines/{id}/stats` returns
the same set plus `triggered`, `ended`, `not_opened`, `send`, `ctr`, `sms_sent`.

**Trend chart (#4)** needs no new source — it is #5/#6 plotted over time.

Three brief questions fall out for free:

- **Q3 "what is a conversion"** — Ecomail already computes `conversions` / `conversions_value`
  from e-commerce transactions fed by the Shoptet connector. We do not have to define it or join
  Shoptet orders ourselves — *provided the tracking is actually live* (§4).
- **Q4 "automation vs newsletter"** — the platform separates them natively: campaigns vs pipelines.
  No manual split needed.
- **The OR/UR ambiguity** — moot. `open_rate`, `click_rate` and `unsub_rate` all arrive in the same
  response. Ship all three; Bára's intent stops being a blocker. Still worth confirming, but it no
  longer gates the build.

## 3. History: the documented parameter does not work — SUPERSEDED BY §8

> **Verified 2026-09-22 against the live account: `from_date`/`to_date` on the stats endpoints is a
> no-op.** The desk research below was right about what the changelog claims and wrong about what
> the API does. Read §8 for the measured behaviour and the workaround that does work.

### What the docs claim

Brief Q2 asked whether history is retroactive or only forward from first collection. Ecomail's
changelog, **2026-09-14**: `from_date` / `to_date` (`YYYY-MM-DD`) added to
`GET /campaigns/{id}/stats` and `GET /pipelines/{id}/stats`, to *"retrieve statistics day by day
without loading the full history"*.

So "srovnání s předchozím měsícem" looks answerable from day one. **This shipped eight days ago
and is unverified against the `anela` account** — it is the single highest-value thing to test, and
the whole month-over-month requirement rests on it.

Second-order issue either way: campaign stats are **cumulative to date**. Opens and conversions keep
arriving for weeks after a send, so a number snapshotted on the 1st will not match what Ecomail
shows later. Any ingest must re-pull a trailing window (≈90 days), not write once and freeze.

## 4. Conversions are the real risk

`conversions_value` only works if Ecomail's e-commerce tracking is receiving transactions from
Shoptet. Two reasons for doubt: the product feed failed on 2026-08-20, and this project already has
a precedent — `ImportedMarketingTransactions` is empty in production because the Meta/Google import
never worked. **Check before promising conversions.** If the numbers are zero, #5/#6's "Konverze"
must come from joining Shoptet orders, which pulls Cluster D's order-grain problem into this cluster.

## 5. MCP vs API — they are not competing options

There **is** an official Ecomail MCP server (`npx -y ecomail-mcp`, or a hosted remote; auth via
`ECOMAIL_API_KEY`). It wraps the same REST endpoints, campaign and pipeline stats included.

| | MCP server | REST API |
|---|---|---|
| Time to first number | minutes | days |
| Repeatable monthly figure | no — an agent retypes numbers each time | yes |
| Usable by Bára | no (needs an MCP client + an agent) | yes, via whatever UI we build |
| History / snapshots | none | ours to keep |
| Write safety | **exposes every endpoint — send campaign, trigger automation, delete subscribers — with no read-only mode**, against a live list | we call only what we call |

They answer different questions. **MCP is the research instrument; REST is the production pipe.**
Using MCP as the delivery mechanism for a recurring monthly report would mean a person prompting an
LLM to read numbers off a live marketing account and retype them — no audit trail, no reproducibility.
Using REST to answer "is conversion tracking even on?" would mean writing an adapter to find out.

## 6. The Metabase pattern is not live in production

The brief proposed following the Flexi→`anela_analytics`→Metabase precedent. That precedent is
**code-complete but dark in prod**:

- `FlexiAdapterServiceCollectionExtensions.cs:112-129` registers the analytics DbContext, the four
  sync services and `FlexiAnalyticsSyncJob` **only if `AnalyticsDatabase:ConnectionString` is non-empty**.
- `appsettings.Production.json` sets only `AnalyticsDatabase:MaxPoolSize` — no connection string.
- No `AnalyticsDatabase--ConnectionString` secret in `kv-heblo-prod` or `kv-heblo-stg`.
- No `AnalyticsDatabase__ConnectionString` app setting on `heblo` or `heblo-test`.
- No Application Insights trace mentioning Analytics in the last 30 days.

So the nightly job has never registered in production. "Follow the established pattern" currently
means "stand the pattern up in prod for the first time" — a real cost that belongs in the estimate,
not an assumed freebie. Still unknown, and still undocumented anywhere in `docs/`: where Metabase
runs, what it ingests, and whether Bára has a login.

## 7. Proposal

**The ingest is identical whichever home we choose.** A job that pulls campaigns + pipelines daily
and stores dated snapshots is the same work whether the numbers surface in Metabase or a Heblo page.
That decision does not have to block starting — but it does need the Metabase facts before we commit.

**Phase 0 — spike, hours, no code.** Get a read-only-intent Ecomail API key. Point the MCP server
(or plain `curl`) at the live account and answer, with real numbers:

1. Does `from_date`/`to_date` actually backfill August and July, or only work forward?
2. Are `conversions` / `conversions_value` non-zero — is the Shoptet tracking alive?
3. How many campaigns and pipelines exist, and are the pipelines named well enough to report per-automation?
4. Does what Ecomail calls a campaign match what Bára calls a newsletter?

Every one of those is a yes/no that changes the design. None can be answered from the repo.

**Phase 1 — decide the home,** on the Phase 0 answers plus two questions only people can answer:
does Bára have (or want) a Metabase login, and where does that instance actually run?

**Phase 2 — build the ingest** (nightly job, dated snapshots, 90-day re-pull window, `Ecomail--ApiKey`
in Key Vault), then the presentation layer Phase 1 chose.

**Do not** start with a Heblo adapter + three React pages. It is the most expensive option, it
contradicts the monthly-review rule, and 40 more rows of that backlog are queued behind these three.

### Ask Bára / Eliška
1. Is Ecomail the only sending platform? (Any Shoptet-native newsletters outside it?)
2. UR = unsubscribes or clicks? *(we can ship both — this is confirmation, not a blocker)*
3. Which pipelines count as "automations" for reporting — all of them, or a named few?
4. Was the contact-limit overage (June) resolved, and is the broken product feed (August) known?


---

# 8. Phase 0 results — measured against the live `anela` account

Read-only run, 2026-09-22. All GETs. Raw responses kept in the session scratchpad.

## 8.1 The account

| | |
|---|---|
| Automations (`/pipelines`) | **4**, of which **2 carry real traffic**: `Opuštěný košík_2025` (14720), `První nákup` (31762). The others are `Opuštěný košík_vánoce koncept` (all zeros) and `test`. |
| Campaigns (`/campaigns`) | **200+** (4+ pages × 50; `per_page` caps at 50, not 100 — the doc's example 422s) |
| Newsletter reach | ~1,270 recipients per recent send — a *segment*, not the base. Older campaigns show 12,000–19,000. Bára should confirm this is intentional. |
| Product feed | **healthy now** — `/feeds` shows one Google feed, 307 products, `error: null`, updated 2026-09-22 05:21. The August failure self-resolved. |

## 8.2 Conversion tracking is live, and the numbers are material

```
Opuštěný košík_2025   triggered 4749  send 1139  OR 43.55%  CR 6.15%  conv 150  →  314 708,90 Kč
První nákup           triggered 5537  send 5624  OR 22.32%  CR  1.49%  conv  25  →   28 453,00 Kč
```

Brief Q3 is answered: **we do not need to define "conversion" or join Shoptet orders.** Ecomail
already attributes revenue, and the abandoned-cart automation alone is carrying ~315k Kč.

## 8.3 The blocking defect: `from_date`/`to_date` on `/stats` is ignored

Campaign 266 (`Pleť v létě…_0726`, a **July** send) returns **byte-identical lifetime numbers** for
every window asked:

```
lifetime        inject 1271  open 321  click 2  unsub 4  conv 7 / 11 446 Kč
window 2026-07  inject 1271  open 321  click 2  unsub 4  conv 7 / 11 446 Kč
window 2026-09  inject 1271  open 321  click 2  unsub 4  conv 7 / 11 446 Kč   ← impossible
window 2026-01  inject 1271  open 321  click 2  unsub 4  conv 7 / 11 446 Kč   ← impossible
```

Same for `/pipelines/{id}/stats`: July and August windows return the lifetime figure unchanged.
The parameter shipped 2026-09-14 and **does not function on this account**. Anything built on the
changelog's promise would have silently reported lifetime totals as monthly ones — the worst kind of
reporting bug, because every number looks plausible.

## 8.4 The workaround that does work: `stats-detail`

`GET /{campaigns|pipelines}/{id}/stats-detail?event=E&from_date=A&to_date=B&per_page=1` →
read `.total`. It genuinely filters, and `total` is the count of **unique subscribers** with that
event in the window — exactly the grain OR/UR need. `per_page=1` keeps the payload tiny; we never
page through subscribers.

Measured on `První nákup` (cumulative since date — the filter is real):

```
since 2026-07-01   send 1875   open 623   click 63   unsub 20
since 2026-08-01   send 1093   open 403   click 34   unsub 11
since 2026-09-01   send  609   open 221   click 13   unsub  3
```

Events available: `send, open, click, unsub, soft_bounce, hard_bounce, spam, out_of_band`.
Cost: one call per (object × event × month) — trivial against a 1000 req/min budget.

> **Correction, 2026-09-24: this endpoint only reaches back 365 days.** Every window probed above
> was inside that, so the limit never showed. See §8.9 — automation months are *not* backfillable
> to the start of the account.

## 8.9 CORRECTION — `stats-detail` only answers for the last 365 days

> Added 2026-09-24, after the first production run. Supersedes every claim above that automation
> months are retroactively backfillable to the start of the account.

The first real run backfilled `BackfillFrom = 2024-11-01` and got a clean split: for **all four**
pipelines, 2025-10 … 2026-09 succeeded and 2024-11 … 2025-09 returned `422`. The pipelines' own
creation dates (2024-07 through 2026-05) make no difference — the boundary is the same for all of
them, and it is the calendar, not the account:

```
GET /pipelines/31762/stats-detail?event=send&from_date=2025-09-01&to_date=2025-09-30
422 {"errors":{"from_date":["The from date field must be a date after or equal to 2025-09-24."]}}

GET /pipelines/31762/stats-detail?event=send&from_date=2025-10-01&to_date=2025-10-31
200 {"next_page_url":null,"total":0,"per_page":1,"subscribers":[]}
```

A rolling 365-day window, measured on 2026-09-24. Consequences:

- **Automation history before 2025-10 is unreachable and always will be.** #5's monthly
  sends/OR/UR/clicks go back one year, not to 2024-11. Bára needs telling.
- The window **rolls**, so each passing month drops one off the far end. Months already computed
  stay (they lock and are never recomputed), but a month never captured can never be captured.
  This is a second, weaker version of the §8.5 deadline: it applies to all four event counts, not
  just conversions, and it gives about a year of slack instead of none.
- A `BackfillFrom` below the floor is not merely useless: failed months are deliberately never
  locked, so the job re-requests four doomed calls for each of them on **every** run. The fix is to
  clamp the loop to the first calendar month that starts on or after the floor.

## 8.5 The one metric that is genuinely unavailable

**There is no conversion event on `stats-detail`.** `event=conversion|conversions|transaction|order|purchase`
all return `total: null`, and `GET /transactions` is 403 on this key. So:

- **Newsletters (#6): solved.** A one-off campaign's conversions all land within days of its send,
  so lifetime `/stats` *is* that month's number — just bucket the campaign by send date.
- **Automations (#5): conversions per month cannot be read retroactively.** An automation runs
  continuously, so its lifetime 150 conversions span the whole of 2025–2026.

The fix is a nightly snapshot of the cumulative counter; month-over-month deltas then give monthly
conversions — **but only from the day we start collecting.** The brief's original fear was right,
and it is now pinned to exactly one metric instead of the whole cluster. Bára needs telling that
*automation revenue MoM* starts empty and fills in from month two. Everything else is retroactive.

## 8.6 CORRECTED — the campaign model is explicit, not guesswork

> Supersedes an earlier version of this section, which read the campaign list off page 1 only and
> got three things wrong. The full 250-campaign pull shows Ecomail models all of this properly.

`campaign_type` is a real field on every campaign. Across all 250:

```
email      141    a plain newsletter
variation   60    one arm of an A/B test
ab          28    the A/B parent — carries the aggregate
sms         21    an SMS campaign, not email at all
```

**Variations are samples, not halves.** Campaign 264 (`ab`) vs its two variations:

```
264  ab         inject 12 715   open 3 248 (25.56%)   unsub 33   conv 42 → 72 302 Kč
265  variation  inject  1 271   open   320 (25.18%)   unsub  0   conv  3 →  2 053 Kč
266  variation  inject  1 271   open   321 (25.28%)   unsub  4   conv  7 → 11 446 Kč
```

The parent is the whole send — two 10% test arms plus the winner to the remaining ~80%. So the
earlier reading was wrong three ways: the pairs must **not** be merged (that would still miss 80% of
the send), they must be **excluded** in favour of the parent, and real newsletter reach is
**~12 700, not ~1 270**. All 60 variations carry `parent_id`, and all 60 parents are type `ab` — the
link is total, so the exclusion is safe.

**`sent_at` is reliable after all.** By (type, status) across all 250:

```
ab         status=3   26 HAS sent_at      status=0    2 NULL   (drafts)
email      status=3  103 HAS sent_at      status=0   38 NULL   (drafts)
sms        status=3   11 HAS sent_at      status=0   10 NULL   (drafts)
variation  status=3   60 NULL
```

**Zero exceptions:** every sent campaign that is not a variation has `sent_at`. The earlier "null on
every recent send" was an artifact of page 1 being mostly variations and drafts.

### The selection rule, settled

```sql
status = 3  AND  campaign_type IN ('email','ab')      -- bucket by sent_at
```

That drops drafts, drops A/B arms already counted in their parent, and drops SMS — each on a real
field rather than a title-prefix hack. §9.3's open questions 1, 2 and 3 are answered by it.

### What that yields

129 reportable newsletters, **2024-11-29 → 2026-09-13** — nearly two years of history, all of it
retroactively available.

```
2026-09   1 campaign    12 642 recipients
2026-08   2 campaigns   25 450
2026-07   1 campaign    12 715
2026-06   6 campaigns   68 199
2026-03   9 campaigns   92 952
2025-11  11 campaigns   44 331
```

Worth Bára's attention independently of this build: send volume has fallen off a cliff since June —
from 6–11 campaigns a month to 1–2. That is the kind of thing item #4's trend chart exists to show.

## 8.7 A caveat on OR itself

Campaign 275's opens: 118 of them are `GmailImageProxy`, and 45 more come from
`Mountain View, California`. Those are machine prefetches, not humans. A third or more of every
open rate here is proxy noise. It is stable enough that MoM comparison still means something, but
"OR 24%" should not be presented as "24% of women read it". Worth one sentence when this ships.

## 8.8 Revised recommendation

Nothing here changes the §7 architecture — REST for the pipeline, MCP for exploration, the ingest
identical whichever home wins. It changes the **build order**, because the cheap parts are now known:

1. **Start the nightly cumulative snapshot immediately**, before anything else is designed. It is
   perhaps a day of work and it is the *only* thing with a deadline: every day without it is a
   permanently missing data point for automation conversions. Snapshot `/pipelines/{id}/stats` and
   `/campaigns/{id}/stats` verbatim, dated.
2. **#6 (newsletters) is deliverable now** and retroactively — campaigns bucketed by send date,
   lifetime stats, pairs merged, drafts and SMS excluded.
3. **#5 (automations) is deliverable now for OR/UR/clicks/sends** via `stats-detail` windows,
   but only **12 rolling months** back, not to the start of the account (§8.9). Conversions MoM fills in from the snapshot's start date.
4. **#4 (trend chart)** falls out of 2 and 3 — no new source.

Scope reality check: **two** live automations and ~2 newsletters a month. This is a much smaller
reporting surface than the brief implies — which argues for putting it in Metabase over three
bespoke Heblo pages more strongly than before.

---

# 9. Storage decision — settled 2026-09-22

**Where:** the primary Heblo database. `anela_analytics` is declared dead (§6 already showed it has
never been wired in production). Two consequences worth stating plainly:

- The brief's working rule — *"reviewed monthly in a meeting → Metabase"* — is moot. These numbers
  surface in Heblo pages, because there is no live Metabase pipeline to surface them in.
- `Anela.Heblo.Persistence.Analytics` + the Flexi sync (12 files, a DbContext, a migration and an
  `IRecurringJob`) are now confirmed dead code shipping in every image. Not this cluster's business
  to remove — flagging it, not touching it.

**How:** nightly-style ingest → dated rows in Postgres → every read is SQL. Not live API reads, not
an in-memory cache. The reasoning is in §8.5: automation conversions per month exist nowhere except
in snapshots we take ourselves, and a cache answers *"now"* when the requirement is *"August"*.
Measured cost of a full pull: ~400 calls ≈ 2 minutes at ~0.3 s/call, against a 1000/min budget —
cheap enough to re-pull everything on a schedule and skip incremental logic entirely.

## 9.1 Follow `MarketingPerformance`, not `flexi_raw`

The main DB uses `ToTable("MarketingActions", "public")` — PascalCase, explicit `public` schema. No
`*_raw` schemas here; that convention belongs to the analytics DB we just abandoned.

More importantly, `Application/Features/MarketingPerformance` **already solves this exact problem
shape**, and independently arrived at the same rules:

| Its pattern | Why it matters here |
|---|---|
| `MarketingPerformanceMonth` — *"Sums only — ratios are derived at read time"* | Exactly §8.8's rule. Store counts, compute OR/UR at read time. Already house policy. |
| `RecomputeWindowMonths = 2` (current + previous) | The late-arriving-opens problem, already solved. Re-pull recent months, leave older alone. |
| `IsLocked` — *"true once the month left the recompute window"* | Months freeze instead of drifting forever. |
| `RefreshJob` (scheduled) + `RecomputeJob` (manual, ignores locks) | A "recompute" button for when a number looks wrong. |
| `ChannelCode` as string — *"adding a channel is a config change"* | Resist enums for anything Bára might rename. |
| `LastError`, `RevenueComputedAt`, `CostsComputedAt` | Per-stage observability on a job nobody watches. |

Mirror this module. Do not invent a second idiom.

**Do not** fold Ecomail into `MarketingPerformanceChannelCost` — that entity is cost + invoice count
per channel. Mailing metrics are a different fact shape (sent/open/click/unsub/conversions). Same
idiom, sibling tables.

## 9.2 Tables

Schema `public`, PascalCase, configs in `Persistence/<Area>/<Entity>Configuration.cs`,
**every `DateTime` via `.AsUtcTimestamp()`** — the global converter forces `Kind=Unspecified`, so a
`timestamp with time zone` column fails every write.

**`EcomailCampaigns`** — one row per campaign (newsletters, #6). Lifetime stats are final for a
one-off send, so the stats live as columns here rather than in a separate fact table.

```
Id (Ecomail campaign id, natural key)   Title, Subject, Status, FromEmail
SentOn (date, derived — see below)      Recipients
Inject, Delivery, Open, TotalOpen, Click, TotalClick, Unsub, Bounce, Spam
Conversions, ConversionsValue
IsSms (bool)                            SyncedAt, IsLocked, LastError
```

**`EcomailAutomationMonths`** — pipeline × year/month (automations, #5). Sums only, from the
`stats-detail` windows of §8.4. Backfillable 12 rolling months only (§8.9).

```
PipelineId, Year, Month   Send, Open, Click, Unsub, SoftBounce, HardBounce, Spam
ComputedAt, IsLocked, LastError
```

**`EcomailAutomationSnapshots`** — pipeline × captured-on date. **The irreplaceable table.**

```
PipelineId, CapturedOn (date)   Triggered, Ended, Send, Open, Click, Unsub,
                                Bounce, Conversions, ConversionsValue
```

This one deliberately breaks the `MarketingPerformance` idiom: it is never recomputed and never
locked, because it is not a computed aggregate — it is an **observation of what the counter said on
a given day**. Monthly automation conversions are the delta between two snapshots. Append-only,
correct by construction, and the only defence against §8.5.

**`EcomailPipelines`** — dimension (Id, Name, ListId, CreatedAt, UpdatedAt, SyncedAt).

## 9.3 Decisions to make before writing the migration

1. ~~`SentOn` derivation~~ — **resolved by §8.6.** Use `sent_at`; it is populated on every sent
   non-variation campaign, without exception.
2. ~~A/B pair merging~~ — **resolved.** Do not merge. Store every campaign including variations for
   traceability, but report only `campaign_type IN ('email','ab')`; the `ab` parent already contains
   the full send.
3. ~~SMS exclusion~~ — **resolved.** `campaign_type == 'sms'`, a real field, not a title prefix.
4. **Refresh cadence.** Every 6 h rather than nightly, so #6's *"míra aktuálního newsletteru"* isn't
   up to a day stale. The job costs two minutes.

Migrations in this project are **manual** — the migration has to be applied by hand after deploy.
