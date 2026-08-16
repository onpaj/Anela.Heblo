# Telemetry Anomaly Rules

Operating rules for the `applicationinsightsscan` skill. It is a **signal**
tool, not an alerting tool — noise (bot scans, expected auth gating, aborted
client fetches) is filtered out deliberately so the issues it files are worth
a human's attention.

All GitHub examples below use `${REPO}` for the target repo — get it once
with `.claude/skills/applicationinsightsscan/gh-api.sh repo` (auto-detected
from this checkout's `origin` remote, or `GH_REPO` if set).

## What it flags (file an issue)

- **Server faults** — any sustained 5xx, especially 500s whose exception
  stack names a specific handler/repository in the app's own codebase.
- **New or rising exception types** — particularly namespaces owned by this
  app (its own root namespace, not framework/third-party code) and
  infrastructure faults that affect users (database client connection
  drops/timeouts, external API/dependency client failures).
- **Regression leads** — a failure-rate or latency step-change that lines up
  with a recent merge.
- **Latency risk** — a dependency whose p95/p99 is high or trending up
  against a meaningful call volume (e.g. a third-party API dependency with
  p95 in the seconds); outbound LLM calls are typically expected-slow, not a
  finding on their own.
- **Permission misconfiguration** — an authenticated endpoint returning 403 at
  high volume to real users (distinct from a user simply lacking a feature).
- **Real frontend errors** — browser exceptions with an app stack
  (`TypeError: ... is not a function`), not third-party/extension noise.
- **Failed background jobs** — every `HangfireJobFailed` custom event is
  issue-worthy by default. It is emitted only when a Hangfire job has
  exhausted all automatic retries, so it is never transient noise; these
  failures are otherwise invisible (nothing user-facing breaks). Include the
  `JobType`, `RecurringJobId`, `ExceptionType`, and `ExceptionMessage`
  dimensions in the issue so a code-fixing agent can act on it.

## What it skips (do not file)

- **Bot / scanner traffic** — `POST /wp-login.php`, probes for non-existent
  paths, 405s on unsupported verbs.
- **Expected auth gating** — 401 on polling endpoints (e.g. `/api/auth/me`,
  dashboard polling routes) from unauthenticated/expired sessions is normal
  SPA behaviour.
- **Aborted client fetches** — `Fetch` dependencies to the app's own frontend
  domain with `resultCode 0` are browser requests cancelled by tab-close or
  SPA navigation; the giant p99 outliers are hung-then-abandoned fetches, not
  server latency.
- **Expected-slow outbound calls** — e.g. `api.anthropic.com` /
  `api.openai.com` LLM dependencies; flag only failures, not duration.
- **Anything already addressed** — a signal whose fix merged inside the
  window (cross-check GitHub before filing).

## Deduplication & suppression

Never file a duplicate, and never re-file something the user already
rejected. Before opening an issue for a surviving signal, check it against
existing GitHub issues and drop it if either holds:

1. **A similar issue is open.** An open `telemetry` issue describing the same
   anomaly → skip (already tracked). Do not post a "still happening" comment
   unless the signal has materially worsened.
2. **A similar issue was closed by the user without a fix.** A closed issue
   for the same anomaly whose closure did **not** land a fix — i.e. closed as
   *not planned* (`state_reason: not_planned`), or closed manually with **no
   linked merged PR** — is an explicit "won't do". Skip it permanently;
   respect the rejection.

A closed issue that **was** resolved by a merged PR is *not* a suppression:
if the same anomaly reappears after its fix shipped, that is a regression
worth a **new** issue (reference the old one).

### Signal fingerprint (how "similar" is decided)

Prose matching is unreliable, so every filed issue carries a stable,
machine-matchable fingerprint as its first body line:

```
telemetry-signal: <category>:<subject>[:<detail>]
```

The fingerprint is derived only from *what* the anomaly is, never from the
counts/percentiles of a particular run, so the same anomaly always produces
the same key.

| Category | Subject : detail | Example |
|---|---|---|
| `req-5xx` | `<endpoint>` : `<resultCode>` | `req-5xx:Orders/Create:500` |
| `req-403` | `<endpoint>` | `req-403:GET /api/Reports/summary` |
| `exception` | `<type>@<innermost app frame>` | `exception:InvalidOperationException@OrderRepository.GetStatsAsync` |
| `dep-fail` | `<type>:<target>` | `dep-fail:HTTP:payments.example.com` |
| `dep-latency` | `<type>:<target>` | `dep-latency:HTTP:payments.example.com` |
| `frontend` | `<error>@<symbol>` | `frontend:TypeError-r.filter@Yq1` |
| `job-fail` | `<JobType or RecurringJobId>` : `<ExceptionType>` | `job-fail:CatalogSyncJob:HttpRequestException` |

To dedup, search issues (open **and** closed) for the exact
`telemetry-signal:` line:

```bash
.claude/skills/applicationinsightsscan/gh-api.sh find-signal 'req-5xx:Orders/Create:500'
```

This returns every matching issue with its `state` and `state_reason`. Match
on the fingerprint, then apply the two rules above — for the "closed without
a merged PR" check, inspect the issue's timeline
(`gh-api.sh GET /repos/${REPO}/issues/<n>/timeline`) for a `closed`
event with a `commit_id` / linked merged PR. If no fingerprint match exists,
fall back to a prose comparison of the endpoint/exception before filing.

## Output

Issues are labelled `telemetry` + a secondary label (`reliability`,
`performance`, `risk`, or `frontend`). Each issue includes the
`telemetry-signal:` fingerprint line, the observed signal with **numbers**
(counts, percentiles, window), the exception/endpoint it maps to, a
correlation hypothesis where one exists, and a concrete minimal next step.

Find all open telemetry issues:
```
https://github.com/${REPO}/issues?q=label%3Atelemetry+is%3Aopen
```

### Labels

The routine expects these labels to already exist on the target repo:
`telemetry`, `reliability`, `performance`, `risk`, `frontend`. To create them
if missing:

```bash
for l in "telemetry:0e8a16" "reliability:b60205" "performance:fbca04" \
         "risk:d93f0b" "frontend:1d76db"; do
  name="${l%%:*}"; color="${l##*:}"
  .claude/skills/applicationinsightsscan/gh-api.sh POST "/repos/${REPO}/labels" \
    "$(jq -n --arg n "$name" --arg c "$color" '{name:$n, color:$c}')"
done
```
