# Telemetry: nightly Hangfire job — ArgumentException DateTime Kind=Unspecified × 22/night, every night Jun 19–24 (agent fix #3243 did not hold)

telemetry-signal: exception:ArgumentException@DateTimeConverterResolver.Get

**Window:** P7D (2026-06-17 – 2026-06-24)
**Total count:** 116 exceptions (22/night Jun 19–22, 14/night Jun 23–24)
**Regression from:** #3243 (closed 2026-06-22 as `agent-completed`; fix did not stop the nightly failures)

## Signal

`System.ArgumentException` thrown at `Npgsql.Internal.Converters.DateTimeConverterResolver.Get` — every night at ~02:00 UTC from a background Hangfire job:

| Date | Exceptions |
|---|---|
| 2026-06-19 | 22 |
| 2026-06-20 | 22 |
| 2026-06-21 | 22 |
| 2026-06-22 | 22 |
| 2026-06-23 | 14 |
| 2026-06-24 | 14 |

**Hourly pattern (repeat each night, Jun 19–22):**

| Hour UTC | Count |
|---|---|
| 02:00 | 16 |
| 03:00 | 2 |
| 04:00 | 2 |
| 06:00 | 2 |
| **Total** | **22** |

The 02:00 burst of 16 followed by 2 at 03:00, 04:00, and 06:00 is consistent with a Hangfire job starting at 02:00 and being retried with exponential backoff (approx. 11 attempts × 2 log entries per attempt = 22), exactly as documented in #3243.

**Error message:**
```
Cannot write DateTime with Kind=Unspecified to PostgreSQL type 'timestamp with time zone',
only UTC is supported. (Parameter 'value')
```

**Key custom dimensions from 2026-06-24T02:00 sample:**
```
FormattedMessage: DbTransientRetryExhausted attempts=1 exception.type=System.ArgumentException
CategoryName:    Anela.Heblo.Persistence.Infrastructure.Resilience.PollyExecutionStrategy
operation_Name:  (empty — confirmed background/Hangfire, not a user request)
```

`attempts=1` in `DbTransientRetryExhausted` means Polly treats `ArgumentException` as non-transient and does not retry at the DB level; Hangfire itself retries the job with backoff.

## Relationship to #3243

Issue #3243 was filed on 2026-06-19 for the first occurrence of this nightly job failure and closed on 2026-06-22 as `agent-completed`. However, the job has continued failing every night since Jun 19 without interruption:

- 22 exceptions on **Jun 20, 21, 22** — all after the agent was working on the fix
- 14 exceptions on **Jun 23, 24** — after the issue was marked completed

The fix (whatever was applied) did not correct the underlying `DateTime.Kind = Unspecified` write to a `timestamptz` column, or was not deployed to production.

## Root cause hypothesis

As in #3243: a scheduled nightly Hangfire job constructs or receives a `DateTime` without `DateTimeKind.Utc` and passes it to a raw SQL command targeting a `timestamp with time zone` column. Npgsql ≥ 6 rejects `Kind=Unspecified` on `timestamptz` by design. The job has been running nightly since at least Jun 19 and failing on every run.

The slight reduction from 22 to 14 exceptions per night starting Jun 23 (starting 1 hour earlier at 01:00 UTC) may indicate a schedule or timezone shift, or a partial fix that reduced retry count without eliminating the root cause.

## Next step

1. Confirm which Hangfire job is failing: check `SELECT jobid, statename, expireat, data FROM hangfire.job WHERE statename = 'Failed' ORDER BY createdat DESC LIMIT 20;` on production — look for the job running at ~02:00 UTC with `System.ArgumentException` in the exception data.
2. In that job, locate every `DateTime` value written to a `timestamptz` column via raw SQL or Dapper and replace with `DateTime.SpecifyKind(value, DateTimeKind.Utc)` or `DateTime.UtcNow`.
3. Verify the fix deployed to production and check the following morning (Jun 25 ~02:00 UTC) that no new exceptions appear.
4. If the agent applied a fix in the application layer, confirm it covers the specific code path reached by this job (the handler may use a different write route than the previously-fixed path).

_Filed by the telemetry-anomaly routine — 2026-06-24._
