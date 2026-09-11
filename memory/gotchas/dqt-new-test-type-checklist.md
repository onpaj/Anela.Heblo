# Adding a DQT test type: the pipeline is NOT fully generic

The drift DQT pipeline (`DriftDqtJobRunner`/`RunDqtHandler`/`IDriftDqtComparer`) resolves
comparers by `DqtTestType` and *is* generic for **running** a check. But **reading the run
detail is not**: `GetDqtRunDetailHandler.Handle` shapes results with an explicit per-type
branch:

- `IssuedInvoiceComparison` → invoice results
- `ProductPairing`/`StockWriteBackReconciliation`/`LotSumVsErpStock` → drift results (`GetDriftResultsAsync`)
- anything else → `throw new NotSupportedException("No result-shaping logic registered ...")`

So a new **drift** test type that only adds enum + comparer + job + DI will run and persist
results fine, then blow up the moment someone opens its run detail:
`System.NotSupportedException: No result-shaping logic registered for DqtTestType X`.

## Checklist when adding a DQT drift test type
1. `DqtTestType` enum value
2. `IDriftDqtComparer` implementation + DI registration in `DataQualityModule`
3. auto-discovered `IRecurringJob`
4. **`GetDqtRunDetailHandler` — add the new value to the drift branch** ← the easy-to-miss one
5. Frontend: `RunDqtButton`, `DqtRunsTable`, `DqtRunDetail` (isDriftTestType + flag map + headers), `i18n.ts`, regenerate TS client

Grep `grep -rn --include="*.cs" "StockWriteBackReconciliation" backend/src` to find every
per-type branch before assuming "generic, no handler changes." (Quote the `--include` glob —
unquoted it gets eaten by zsh and the grep silently finds nothing, which is how this was
missed the first time.)

Seen 2026-07-08 on the LotSumVsErpStock (Šarže vs. ERP sklad) check, PR #3553.

**Missed again, item 5 only, 2026-09-10, on the PriceComparison (Kontrola cen) check.**
Items 1-4 (enum, comparer, job, `GetDqtRunDetailHandler` branch) were done in the same PR
that added the check; the frontend (`DqtRunDetail.tsx`'s `isDriftTestType`, `i18n.ts`,
`DqtRunsTable.tsx`, `RunDqtButton.tsx`) was not, and the gap shipped and sat live for a
week before being caught in a later documentation task. Symptom was worse than a crash: the
run-detail screen fell through to `results.length` (the invoice-result array, always empty
for a drift run) and confidently rendered "Žádné neshody nalezeny pro tento test" while the
backend held a full set of real price mismatches. This is now the checklist's own second
recorded miss of the exact same item — treat item 5 as the one this checklist is worst at
enforcing, not a one-off.
