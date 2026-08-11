telemetry-signal: exception:System.ArgumentException@PhotobankRepository.SaveChangesAsync

## Signal

`System.ArgumentException` ("Cannot write DateTime with Kind=Unspecified to PostgreSQL type 'timestamp with time zone', only UTC is supported") still fires from `PhotobankRepository.SaveChangesAsync` (`backend/src/Anela.Heblo.Persistence/Photobank/PhotobankRepository.cs`), called from the nightly `PhotobankIndexJob.IndexRootAsync` — at the exact same rate as before the "fix".

## Data — P7D window (2026-07-21 → 2026-07-27)

| Date | Occurrences |
|---|---|
| 07-21 | 3 |
| 07-22 | 3 |
| 07-23 | 3 |
| 07-24 | 3 |
| 07-25 | 3 |
| 07-26 | 3 |
| 07-27 | 3 |

Total: 21. Fires once nightly in a burst of exactly 3, ~01:28–01:40 UTC every day (the `PhotobankIndexJob` run). Latest: 2026-07-27T01:40:13Z.

## Prior issue — this is a regression, not a fix

#3444 ("PhotobankRepository.SaveChangesAsync still hits DateTime Kind=Unspecified after partial fix in #3330") was closed `completed` on **2026-07-25T18:34:33Z**, closed by PR #3743 (commit `bac8f42`, "Telemetry: PhotobankRepository.SaveChangesAsync still hits DateTime Kind=Unspecified after partial fix in #3330 (#3743)").

The very next nightly run after that merge (2026-07-26T01:39:39Z) failed identically — 3 occurrences. The run today (2026-07-27T01:40:13Z) also failed identically — 3 occurrences. The daily rate is **unchanged**: 3/day for every day before the merge and every day since. If PR #3743 changed the failing code path at all, it had zero measurable effect on this signal.

## Correlation hypothesis

This is the third fix attempt at this same job/repository (#3330 dropped it from 14/day to 3/day; #3444/PR #3743 aimed at the residual 3/day and didn't move it at all). Either PR #3743 fixed a column that isn't the one actually failing in production, or the fix path isn't being exercised by the entities in this nightly batch. There is likely a third (or fourth) `DateTime`-typed field on the `Photo`/related Photobank entity still carrying `Kind=Unspecified` into the same `SaveChangesAsync` batch.

## Next step

Pull the actual failing parameter from a live App Insights trace for the 2026-07-27T01:40:13Z occurrence (operation_Name is empty — Hangfire job context, no HTTP request) and diff against exactly what PR #3743 changed in `PhotobankRepository.cs`. If the column PR #3743 touched isn't the one in the current exception's parameter set, find the remaining field and apply the same UTC-normalization pattern used in #3330/#3743.

## Reproduce

```bash
./docs/routines/telemetry-anomaly/appinsights-query.sh --timespan P7D \
  'exceptions | where problemId has "DateTimeConverterResolver" | where operation_Name == "" | summarize count() by bin(timestamp,1d) | order by timestamp asc'
```

_Filed by the telemetry-anomaly routine — 2026-07-27. Related: #3444 (closed `completed` by PR #3743 — this is that exact signal recurring unchanged), #3330 (first partial fix), #3592 (same `DateTimeConverterResolver.Get` exception family, different call site — `SmartsuppRepository.UpsertContactAsync`, still open)._
