telemetry-signal: dep-latency:HTTP:petra-tesarikova.flexibee.eu

## Signal

The ABRA FlexiBee ERP dependency (`petra-tesarikova.flexibee.eu`) remains slow at essentially unchanged tail latency, measured **after** the #3759 fix (a circuit breaker on the confirm path, merged 2026-07-30T20:30:04Z via commit `52fdb79`). This is the third filing against this dependency (#2987 → #3759 → this one); the author of #3759's fix explicitly flagged in the merge notes that "the tracked p95/p99 latency signal is not actually resolved by a circuit breaker."

**Window:** P7D (2026-07-28 → 2026-08-04)

| Metric | #2987 (2026-06-05→12) | #3759 (2026-07-20→27) | Now, full P7D | Now, **post-fix only** (2026-07-30T20:30Z → 08-04) |
|---|---|---|---|---|
| Calls | 21,527 | 18,791 | 18,203 | 12,102 |
| Failed | — | — | 16 (Canceled) | 11 |
| p50 | 531 ms | ~similar | 536 ms | 500 ms |
| p95 | 5.8 s | ~similar | 6.63 s | 6.74 s |
| p99 | 21 s | ~similar | 23.8 s | 22.7 s (max 69.9 s) |

Post-fix numbers are statistically indistinguishable from pre-fix — the circuit breaker addresses failure handling, not the underlying request latency.

## New this time: per-endpoint breakdown pinpoints the tail

Prior two issues treated `petra-tesarikova.flexibee.eu` as one aggregate target. Breaking down by endpoint (P7D) shows the tail is concentrated in two specific calls, not spread evenly across all FlexiBee traffic:

| Endpoint | Calls (P7D) | p50 | p95 | p99 |
|---|---|---|---|---|
| `POST .../ucetni-denik/query` (accounting journal) | 2,954 | 2,587 ms | 24,047 ms | 28,739 ms |
| `GET .../sarze-expirace.json` (batch/expiration) | 171 | 5,954 ms | 19,213 ms | 31,477 ms |
| `POST .../stav-skladu-k-datu/query` (stock-as-of-date, highest volume) | 10,409 | 524 ms | 3,044 ms | 6,269 ms |
| `GET .../uzivatelsky-dotaz/21/call.json` | 171 | 4,058 ms | 10,321 ms | 18,661 ms |

Post-fix isolation of `ucetni-denik/query` alone: 1,953 calls, 0 failed, p50 2,557 ms, **p95 22,906 ms, p99 28,126 ms** — this single endpoint is the dominant driver of the overall p95/p99, running ~10x slower than the highest-volume endpoint (`stav-skladu-k-datu/query`, p95 3,044 ms).

## Correlation hypothesis

`ucetni-denik/query` (accounting-journal query, likely paginated/unbounded server-side on FlexiBee's end) and `sarze-expirace.json` (batch/expiration listing) are the two endpoints actually responsible for the tail latency that's been tracked and "fixed" twice without effect. The prior fixes targeted the dependency as a whole (timeout tuning, then a circuit breaker) rather than these two specific slow queries, which is consistent with why neither fix moved the aggregate p95/p99.

## Next step

Given two prior dependency-wide interventions didn't move the number, a different approach is needed: profile what `ucetni-denik/query` and `sarze-expirace.json` are actually querying (date range / pagination size) and whether they can be narrowed, cached, or moved off the synchronous request path — rather than a third attempt at timeout/circuit-breaker tuning on the dependency as a whole. If neither is feasible (FlexiBee-side limitation), consider documenting this as an accepted third-party latency ceiling rather than re-filing a fourth time.

Refs: #2987, #3759