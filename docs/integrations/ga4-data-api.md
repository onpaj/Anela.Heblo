# GA4 Data API → `Heblo_V3.ga4_agg`

Aggregated Google Analytics 4 web traffic, pulled through the GA4 Data API into its own schema in
the main Heblo database, and exposed to Metabase through month-grain read views.

Feeds reporting backlog items **#7** (visits vs completed orders), **#8** (most-viewed landing
page), **#9** (total site traffic) and **#37** (most-read articles).

> This sits **alongside** the existing GA4→BigQuery Metabase connection, which stays as the raw
> event archive. It does not replace it.

---

## Why this exists when GA4 is already in Metabase

Two things the BigQuery connection cannot do.

**1. History.** The BigQuery export holds **2026-05-25 onward only** (confirmed: 120 daily tables,
`20260525`–`20260921`). GA4's BigQuery export never backfills — it writes forward from the day it
was switched on. The first genuine year-over-year pair from BigQuery would be June 2027.

The Data API is not limited that way. It reaches back to **2023-06-29**, the day the property was
created — **39 months**, so every backlog item's *"srovnání … i stejným obdobím vloni"* is
answerable today.

**2. Joins.** Metabase has no cross-database joins in any edition. GA4 in BigQuery can never be
combined with orders or spend. In `Heblo_V3` it sits next to `flexi_raw` and `shoptet_raw` and
joins with plain SQL.

---

## The property

| | |
|---|---|
| Property | `properties/392098710` — "Anela.cz - GA4 - new" |
| Account | `accounts/142686395` — "Anela-shoptet" |
| Created | **2023-06-29T13:31:44Z** (first data the same day) |
| Timezone | `Europe/Prague` |
| Currency | `CZK` |
| Service level | `GOOGLE_ANALYTICS_STANDARD` (free tier) |
| Data retention | `FOURTEEN_MONTHS` for both event and user data |

### Retention does not limit this pull

The property retains event-level data for 14 months, and it is easy to assume that caps how far
back anything can be read. It does not. Retention governs **event-level and exploration** queries.
The standard pre-aggregated dimensions and metrics used here — `date`, `yearMonth`,
`sessionDefaultChannelGroup`, `landingPage`, `pagePath`, `sessions`, `totalUsers`,
`screenPageViews`, `transactions`, `purchaseRevenue` — are served from GA4's own aggregate tables
and reach back to property creation regardless of the retention setting.

Verified empirically: a `yearMonth` report over 2018-01-01 → 2026-09-21 returned 40 months
beginning 2023-06, with the range silently truncated only at the far end (before the property
existed), never at the 14-month boundary.

---

## Credentials

The GA4 Data API uses the **same service account** as Metabase's BigQuery connection:

```
metabase-bq-reader@heblo-493908.iam.gserviceaccount.com
```

Reusing it was a deliberate choice: it already existed, it is already scoped to read-only
analytics data, and a second key would be a second thing to rotate. BigQuery access did **not**
imply Data API access — two separate things had to be done by hand in the Google console:

1. Enable **`analyticsdata.googleapis.com`** (and `analyticsadmin.googleapis.com`) on GCP project
   `heblo-493908` (project number `1007742163977`).
2. Add the service account as a **Viewer** on GA4 property `392098710`
   (GA4 Admin → Property Access Management). Before this the Admin API listed *zero* accounts for
   the service account and every `runReport` returned `PERMISSION_DENIED`.

If the account is ever recreated, both steps have to be repeated.

### Key Vault

Secrets live in Key Vault, never in App Service settings. `--` is the config separator.

| Secret | `kv-heblo-stg` | `kv-heblo-prod` |
|---|---|---|
| `GoogleAnalytics--PropertyId` | `392098710` | `392098710` |
| `GoogleAnalytics--CredentialsJson` | service account JSON | service account JSON |

```bash
az keyvault secret set --vault-name kv-heblo-prod \
  --name "GoogleAnalytics--PropertyId" --value "392098710"

az keyvault secret set --vault-name kv-heblo-prod \
  --name "GoogleAnalytics--CredentialsJson" --file ./service-account.json
```

Restart the Web App afterwards — Key Vault is read once at startup.

---

## Schema — `ga4_agg`

Aggregates only, never events. Nothing in the backlog needs event grain, and `heblosql` is a
1-vCore burstable server shared with production Heblo.

| Table | Grain | Rows (2023-06-29 → 2026-09-21) | Purpose |
|---|---|---|---|
| `traffic_monthly` | month | 40 | **#9** — headline figures that match the GA4 UI |
| `traffic_total_daily` | day | 1,181 | daily totals; the denominator for **#7** |
| `traffic_daily` | day × channel group | 10,885 | acquisition mix (**#20** and follow-ups) |
| `conversions_daily` | day × channel group | 8,317 | **#7** — GA4 purchase events and revenue |
| `landing_page_daily` | day × landing page, top 100/day | 112,221 | **#8** |
| `page_daily` | day × page path, top 100/day | 118,100 | **#37** |
| `sync_state` | one row per table | 6 | watermark and last-run bookkeeping |

Total ≈ 250,700 rows, ~84 MB including indexes.

### Three traffic tables is not redundancy

**GA4 figures do not decompose**, and each table answers a question the others cannot. Measured
against GA4's own month-grain answer for June–August 2026:

| Roll-up | vs GA4's own figure |
|---|---|
| sessions summed from daily rows | **+0.0% to +3.8%** |
| sessions summed across channel rows | **+0.3% to +3.8%** |
| users summed across channels *within one day* | −0.2% to −0.9% (near enough) |
| **users summed across days** | **+34%** — August 2026: 29,194 against GA4's 21,746 |

Sessions are nearly additive; users are emphatically not. GA4 de-duplicates users over whatever
period it is asked about, so a visitor returning on three days becomes three users the moment you
sum daily rows. No amount of arithmetic recovers the monthly figure — it has to be asked for.

So: **`traffic_monthly`** for monthly headlines, **`traffic_total_daily`** for daily totals and
custom ranges (sessions and page views only — not users across days), **`traffic_daily`** for the
*shape* of acquisition and never for a total.

### Choices that differ from the original spec

- **No `entrances` column.** The GA4 Data API has no `entrances` metric — it was a Universal
  Analytics measure. `sessions` is the same quantity under a different name, since a session has
  exactly one landing page.
- **`landingPage`, not `landingPagePlusQueryString`.** The query-string variant splits one page
  across every campaign parameter it was ever reached with, which pushes genuinely busy pages out
  of the ranking (with it, August 2026's top landing page was `/` with 211 sessions; without it,
  the real leader has 515). It also cuts monthly cardinality from 23,114 rows to 5,196.
- **No stored `bounce_rate`.** It is a ratio, and a stored ratio invites an `AVG()` that weights a
  3-session day like a 3000-session day. Every stored metric is additive; bounce rate is derived
  in the views as `1 - engaged_sessions / sessions`.
- **`user_engagement_seconds` stored instead of an average.** Additive, so any roll-up works.

### Top-N caps

`landing_page_daily` and `page_daily` keep the **top 100 per day** (configurable; the cap actually
in force is recorded on `ga4_agg.sync_state.top_n_per_day`). Typical daily cardinality is ~168
landing pages and ~234 page paths, so the head of every ranking is exact. The consequence: the
session counts in those two tables **do not sum to a site total** — that is the cap, not a bug,
and totals belong to `traffic_monthly` / `traffic_total_daily`.

---

## Read views and grants

Five month-grain views in `ga4_agg`, all prefixed `v_`:

| View | Backlog | Notes |
|---|---|---|
| `v_monthly_traffic` | #9 | sessions, users, page views, bounce rate; MoM and YoY; `is_partial_month` flag |
| `v_monthly_traffic_by_channel` | #20 | share of month per channel; YoY per channel |
| `v_monthly_landing_pages` | #8 | ranked, with previous-month and year-ago rank |
| `v_monthly_articles` | #37 | ranked by views, with previous-month and year-ago rank |
| `v_monthly_conversion` | #7 | sessions vs GA4 purchase events — **read the caveat below** |

`metabase_ro` is granted `USAGE` on the schema and `SELECT` on **these five views only** — never
on the raw tables. Ad-hoc `GROUP BY` over 250k raw rows would hit the same single vCore that
serves production, and OSS Metabase has collection-level permissions only (no row-level security,
no data sandboxing), so the boundary has to be enforced in Postgres grants.

Apply (idempotent, safe to re-run):

```bash
psql "$CONNECTION_STRING" -v ON_ERROR_STOP=1 \
  -f backend/src/Anela.Heblo.Persistence.Ga4/Sql/ga4_agg_views.sql
```

---

## #7 will not match the ERP — by design

`v_monthly_conversion` counts GA4 `purchase` events, which fire when the browser reaches the
thank-you page. Against the ERP's order book that is wrong in four directions at once:

| | |
|---|---|
| **Too high** | counts orders later cancelled, returned or never paid — GA4 never hears what happens after checkout |
| **Too high** | a reloaded or bookmarked thank-you page can fire the event twice |
| **Too low** | orders taken by phone, e-mail or in person never touch the site |
| **Too low** | shoppers who block tracking or decline consent buy without producing an event |

It is a reliable **trend** for the website and a reliable month-against-month comparison. It is
**not the company's conversion rate**, and it must never be reconciled against an invoice count.
The caveat is repeated as a block comment in `ga4_agg_views.sql` and as a `COMMENT ON VIEW`, so it
surfaces in Metabase's own field documentation.

---

## Data API vs BigQuery reconciliation

For 2026-05-25 onward the same numbers exist in both places. Measured over three full months:

| Month | Metric | `ga4_agg` (Data API) | BigQuery (raw events) | Gap |
|---|---|---|---|---|
| 2026-06 | page views | 152,591 | 152,594 | **−0.002%** |
| | transactions | 2,432 | 2,432 | **0.000%** |
| | revenue | 2,262,071.68 | 2,262,071.68 | **0.000%** |
| | sessions | 41,253 | 26,225 | **+57.3%** |
| 2026-07 | page views | 104,792 | 104,796 | **−0.004%** |
| | transactions | 1,536 | 1,536 | **0.000%** |
| | revenue | 1,478,437.07 | 1,478,437.07 | **0.000%** |
| | sessions | 32,264 | 19,708 | **+63.7%** |
| 2026-08 | page views | 105,473 | 105,485 | **−0.011%** |
| | transactions | 1,591 | 1,591 | **0.000%** |
| | revenue | 1,565,365.28 | 1,565,365.28 | **0.000%** |
| | sessions | 34,022 | 20,567 | **+65.4%** |

(Data API figures are `traffic_monthly`; BigQuery sessions are
`COUNT(DISTINCT user_pseudo_id || ga_session_id)` over `events_*`.)

**E-commerce reconciles exactly** — to the unit and to the haléř, in all three months. **Page
views reconcile to within 12 events in 105,000.** Those are the numbers to trust.

**Sessions do not reconcile, and the gap is large but stable** (+57% to +65%). The cause is
measurable in the export itself: **26–28% of exported events carry no `user_pseudo_id` and no
`ga_session_id`** (August 2026: 139,600 of 512,050). Those are consent-mode-denied hits. Any
`COUNT(DISTINCT user_pseudo_id || ga_session_id)` silently drops all of them and under-counts,
while `COUNTIF(event_name = 'session_start')` over-counts in the other direction (45,470 for
August, 34% *above* the Data API). The Data API's figure includes GA4's behavioural modelling for
that traffic, which is also what the GA4 UI shows.

**Which number is right?** For "what does GA4 say" — the Data API, i.e. `ga4_agg`. It is what the
GA4 UI shows and what anyone comparing a Metabase chart to Google will see. BigQuery is the right
source for event-level forensics, not for session counts.

BigQuery session-count queries are therefore best avoided entirely; if one is unavoidable, count
`session_start` events and state the bias.

---

## Sync job

`ga4-aggregates-sync`, an `IRecurringJob` auto-discovered by Hangfire.

| | |
|---|---|
| Cron | `10 4 * * *` Europe/Prague (clear of `flexi-analytics-sync` at `0 3 * * *`) |
| Registration | conditional on `GoogleAnalytics:PropertyId` **and** `GoogleAnalytics:CredentialsJson` both being non-empty — an unconfigured environment is completely inert |
| Database | the main Heblo connection string (`ConnectionStrings:{Environment}`); `ga4_agg` lives in `Heblo_V3` / `Heblo_TST` |

### The trailing re-pull window

**GA4 keeps reprocessing a day's data for roughly 48 hours after it is collected.** A job that
only ever asked for "yesterday" would bake in whatever partial figure was visible at 04:10 and
never correct it — permanently under-reporting, invisibly.

So every run rewinds `Ga4Sync:TrailingReprocessDays` (default **7**) behind the watermark and
**upserts**, replacing the rows for that window rather than appending. A row that the fresh report
no longer returns is deleted, so a landing page that drops out of the day's top 100, or a channel
GA4 later reattributes, cannot linger and inflate a total.

Covered by `TrailingReprocessWindowTests`.

### Chunking, quota and resumability

Requests cover a whole chunk (default 31 days) with `date` as a dimension, rather than one request
per day. The watermark advances **after each chunk**, so an interrupted backfill resumes at the
chunk it reached rather than at the beginning.

Quota is not a constraint. The full 39-month backfill of all six tables cost **691 core tokens of
the 200,000 per-property daily allowance (0.35%)** across 156 requests, and ran in under seven
minutes. Live quota is logged after every request (`Ga4.QuotaAfterReport`).

### Configuration

All optional; defaults shown.

| Key | Default | |
|---|---|---|
| `Ga4Sync:Enabled` | `true` | |
| `Ga4Sync:CronExpression` | `10 4 * * *` | |
| `Ga4Sync:TimeZone` | `Europe/Prague` | |
| `Ga4Sync:BackfillFrom` | `2024-01-01` | set to `2023-06-29` for the full property history |
| `Ga4Sync:TrailingReprocessDays` | `7` | must stay above ~2 days |
| `Ga4Sync:ChunkDays` | `31` | days per Data API request |
| `Ga4Sync:TopLandingPagesPerDay` | `100` | |
| `Ga4Sync:TopPagesPerDay` | `100` | |
| `Ga4Sync:PagePathPrefixes` | *(empty)* | restrict `page_daily` to e.g. `/blog/`, filtered inside GA4 |
| `Ga4Sync:BatchSize` | `500` | rows per `SaveChanges` |
| `Ga4Sync:ThrottleMilliseconds` | `250` | pause between requests |
| `Ga4Sync:RequestTimeoutSeconds` | `1800` | whole-job timeout |

> `BackfillFrom` defaults to `2024-01-01` rather than the property's first day so that a fresh
> environment does not silently pull four years on its first run. Set it to `2023-06-29`
> deliberately when a full history is wanted.

---

## Deploying to a new environment

Migrations are **manual** in this project.

```bash
# 1. Schema
dotnet ef migrations script \
  --project backend/src/Anela.Heblo.Persistence.Ga4/Anela.Heblo.Persistence.Ga4.csproj \
  --context Ga4DbContext --idempotent --output ga4_schema.sql
psql "$CONNECTION_STRING" -v ON_ERROR_STOP=1 -f ga4_schema.sql

# 2. Views and grants
psql "$CONNECTION_STRING" -v ON_ERROR_STOP=1 \
  -f backend/src/Anela.Heblo.Persistence.Ga4/Sql/ga4_agg_views.sql

# 3. Secrets, then restart the Web App
az keyvault secret set --vault-name kv-heblo-prod --name "GoogleAnalytics--PropertyId"      --value "392098710"
az keyvault secret set --vault-name kv-heblo-prod --name "GoogleAnalytics--CredentialsJson" --file ./service-account.json
az webapp restart --name heblo --resource-group rgHeblo
```

The first job run backfills from `Ga4Sync:BackfillFrom`; no separate backfill command exists, and
none is needed.

`ga4_agg` uses its own `__EFMigrationsHistory` **inside the `ga4_agg` schema**, so it never
collides with the main application's migration history in `public`.

### Checking on it

```sql
SELECT entity_name, watermark_date, last_run_status, last_run_finished_at,
       last_run_rows_fetched, last_run_rows_upserted, top_n_per_day, last_error_message
FROM ga4_agg.sync_state ORDER BY entity_name;
```

---

## Out of scope

Cross-schema join views — GA4 sessions against `shoptet_raw` orders, ROAS against `flexi_raw`
spend — are deliberately **not** built here. They come after all three ingestion directions have
landed. See `_specs/00-CONTEXT.md`.
