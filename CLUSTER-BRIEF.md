# Cluster B — Mailing automations & newsletters

> Workspace brief. Seeded 2026-09-22 from an analysis session on `main`.
> **Analysis for this cluster has NOT been done yet** — only a first-pass reading.
> Clusters C and D received a full codebase fit-gap; A and B did not.

## Where this comes from

Source document: **`~/Downloads/IT podpora Loop odstavec.pdf`** (9 pages, one table, 43 rows,
created 2026-09-22). A reporting backlog for the Heblo app, in Czech, columns:
Položka / Priorita / Oblast / Stav / Spolupráce / Brzda?

All 43 rows are in state `Ke zpracování` — nothing started.

| Priority | Count | Items |
|---|---|---|
| Vysoké | 12 | 1–12 |
| Střední | 21 | 13–33 |
| Nízké | 10 | 34–43 |

Four workspaces for the 12 high-priority items:

- A — operational throughput & cost (#1–#3) → `loop-cluster-a-ops`
- **B — mailing automations (#4–#6)** ← *this workspace*
- C — web analytics (#7–#9) → `loop-cluster-c-webanalytics`
- D — sales top-lists & basket (#10–#12) → `loop-cluster-d-sales`

## The three items

Oblast: **Mailingy**. Owner: **Bára Kocmánková** (#4, #5, #6); **Eliška Stofferová** also listed on #4.

### #4 — Automation trend chart
> *Graf trendu — jak si vedou automatizace*

A trend chart showing how the automations are performing over time.

### #5 — Per-automation effectiveness
> *Automatizace — mailingy — pro každou automatizaci zvlášť a pak udělat vyhodnocení účinnosti —
> kolik rozesláno / OR / UR / konverze atd. … viz newslettery měsíc a srovnání s předchozím měsícem*

For each automation separately, an effectiveness evaluation: how many sent / open rate /
unsubscribe rate / conversions, per month, compared with the previous month.

> Note: **OR** = open rate, **UR** — most likely unsubscribe rate, possibly click-through
> ("prokliky"). **Confirm the intended meaning with Bára before building anything** — the whole
> metric set hinges on it.

### #6 — Newsletter effectiveness
> *Newslettery — na kolik lidí rozesláno / Konverze / OR / UR … absolutně i míra aktuálního
> newsletteru / měsíc a srovnání s předchozím měsícem*

Per newsletter: recipients, conversions, OR, UR — **both absolute numbers and rates** — for the
current newsletter and per month, compared with the previous month.

## The blocker — read this first

**This cluster has no data source in the codebase at all.**

Verified on `main`:

- No ESP (email service provider) adapter exists. `Adapters/` contains: Anthropic, Azure,
  Comgate, Cups, FileSystem, Flexi, GoogleAds, HomeAssistant, Logeto, MetaAds, Microsoft365,
  OpenAI, OpenMeteo, OrgChart, Plaud, SendGrid, Shoptet, ShoptetApi, Smartsupp, WebSearch.
- **SendGrid is transactional email**, not campaign/newsletter analytics — it is not a source
  for OR/UR/conversion per campaign.
- Grepping for `ecomail|mailchimp|mailerlite|klaviyo|newsletter` across
  `Application/`, `Adapters/` and `frontend/src` returns **only** UI hits: "newsletter" appears
  purely as a *marketing action type label* in the marketing calendar
  (`frontend/src/components/marketing/list/marketingActionTypeLabels.ts` and friends).
  It is a category on a calendar entry — not campaign data.

So Cluster B carries the highest priority label and the lowest readiness of the four.

## Questions that must be answered before this is estimable

1. **Which platform actually sends these mailings?** Ecomail? Mailchimp? Shoptet's built-in
   newsletter tool? Something else? Nothing in the codebase reveals it.

2. **Does that platform's API expose per-campaign opens / clicks / unsubscribes / conversions
   historically, or only from the moment we start collecting?**
   If only going forward, the "srovnání s předchozím měsícem" requirement is **unsatisfiable for
   the first month or two**, and Bára needs to be told that up front rather than discovering it
   at the first review.

3. **What counts as a "conversion"** for a mailing? An order attributed within N days? Revenue?
   Whoever answers this also decides whether the metric needs order data joined in — which pulls
   Cluster D's order-grain problem into this cluster.

4. **What distinguishes an "automation" from a "newsletter"** in the sending platform? #5 and #6
   ask for the same metric set over two different object types; if the platform does not separate
   them cleanly, the split has to be defined manually.

## Open architectural question that affects every cluster

During the session it emerged that **Heblo already acts as a producer into a Metabase-facing
analytics database**:

- `docs/superpowers/plans/2026-05-20-flexi-analytics-sync.md:5` — *"pulls raw Flexi accounting
  data into a dedicated `anela_analytics` PostgreSQL database **so Metabase can query it**"*
- Implemented and live: `Anela.Heblo.Persistence.Analytics` (`flexi_raw` schema),
  `Adapters/Anela.Heblo.Adapters.Flexi/Analytics/` (12 files), `FlexiAnalyticsSyncJob : IRecurringJob`
  (nightly cron), `AnalyticsDatabase` configured in `appsettings.json`, `.Staging.json`, `.Production.json`

Working rule proposed in session:

| Use | Home |
|---|---|
| Reviewed monthly, in a meeting, to understand the business | Metabase |
| Changes what someone does in the app in the next five minutes | Heblo |

**All three Cluster B items are monthly marketing-review numbers.** By that rule they belong in
Metabase, not in Heblo pages. The open question is whether the Metabase instance already ingests
mailing-platform data — if it does, this cluster may be answerable with **zero code**.

Unresolved about Metabase: where it runs, what it actually ingests, by what mechanism, refresh
cadence, and whether the Loop owners (Bára, Eliška, Andrea, Linda, Petra — none of them SQL
users) have logins. There is **no Metabase entry in CLAUDE.md's documentation map and no
`docs/architecture/` page for it**.

If mailing data is *not* in Metabase, the choice becomes: ingest it into `anela_analytics`
(follows the established pattern) vs. build a Heblo adapter + pages (contradicts it).

## Related items elsewhere in the document

Nothing else in the 43 rows is in the Mailingy area — #4, #5 and #6 are the whole of it.
The nearest neighbours are the Online reklamy items (#20–#33, all Střední, owner Eliška), which
share the "marketing effectiveness" framing and the same MoM/YoY comparison shape.

One relevant known issue from elsewhere in the project: `ImportedMarketingTransactions` is
**empty in production** — the Meta/Google API import never actually worked. Worth knowing before
assuming any marketing-side pipeline in this codebase is live.

## Suggested next step in this workspace

Do not start implementation. Start by answering question 1 above — ask Bára which platform sends
the mailings, then read that platform's API docs to answer question 2. Everything else is
blocked on those two facts.

---
*Untracked file. Delete it or commit it, as you prefer.*
