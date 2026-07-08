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
