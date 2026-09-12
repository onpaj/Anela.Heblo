# A refresh-only action that changes nothing reads as a dead button

**Symptom.** Operator clicks *Synchronizovat* on the price comparison screen and reports
"nothing happens". Every layer is in fact healthy: the POST fires, the backend force-reloads
Flexi and re-reads Shoptet, the rows merge into the cache.

**Cause.** The action is a *read*. When both live systems still hold what the report already
showed — the normal case — the merged rows equal the old rows and the table is byte-for-byte
identical. The only feedback was a spinner that flickers for the duration of the call. Success
with no change and a broken click handler look exactly the same.

**Fix.** Any action whose success is "nothing changed" needs to say so. `PriceDivergenceReport`
now renders `Synchronizováno v HH:MM — beze změn` (or the count of rows that moved) under the
filter bar, with `role="status"`, cleared when a later run fails. `countChangedRows` in
`frontend/src/api/hooks/priceDivergenceMerge.ts` does the comparison; the pre-sync rows must be
captured *before* the await, because `rows` re-reads the very cache the sync rewrites.

**Debugging lesson.** "Nothing happens" does not mean "not wired". Before tracing the wiring,
ask what the operator saw: a disabled button, a spinner that never stops, an error toast and an
unchanged table are four different bugs, and only one of them is in the code path. Asking cost
one question; tracing the whole stack first found no defect because there was none.
