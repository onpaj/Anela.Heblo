# FlexiBee `importNotAllowed` on manufacture consumption = SDK posted to wrong evidence

## Symptom
Moving a manufacture order to SemiProduct fails with the generic fallback:
*"Při zpracování výroby došlo k neočekávané chybě. Technické detaily: Failed to create consumption
stock movement for warehouse 5"*. "Warehouse 5" is incidental — it's the MATERIAL warehouse
(FlexiBee `sklad` id 5 = "Sklad Materialu"), the first document written in the flow.
Raw ERP response (App Insights `aiHeblo`, `traces | where message contains "winstrom"`):
```json
{"winstrom":{"success":"false","message@messageCode":"importNotAllowed","message":"Import není povolen."}}
```

## Root cause (verified)
NOT an ERP license/permission problem (that was an early wrong theory). It was a **bug in
Rem.FlexiBeeSDK.Client ≤ 0.1.138**: `StockItemsMovementClient.SaveAsync` POSTed the full stock
document (header + `polozkyDokladu`) to the **line-items** evidence `skladovy-pohyb-polozka`
instead of the **document** evidence `skladovy-pohyb`. FlexiBee refuses a document import into the
line-items evidence with `importNotAllowed`.

SDK diff 0.1.138 → 0.1.139 (only change, decompiled):
```
0.1.138: PostAsync(envelope, null, null,             null, ct)  // URL .../skladovy-pohyb-polozka
0.1.139: PostAsync(envelope, null, "skladovy-pohyb", null, ct)  // URL .../skladovy-pohyb
```
`ResourceClient.GetUri` uses `customResourceIdentifier ?? ResourceIdentifier`; 0.1.139 passes the
correct evidence. Fix = bump `Rem.FlexiBeeSDK.Client` to **0.1.139** in
`backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj`.

## How it was proven
Dry-run against the live ERP (`petra-tesarikova.flexibee.eu`, creds in `kv-heblo-prod`
`FlexiBeeSettings--*`, login `heblo`), same body `{"winstrom":{"skladovy-pohyb":[{…,"polozkyDokladu":[…]}]}}`:
- `POST /c/{company}/skladovy-pohyb-polozka?dry-run=true` → `importNotAllowed` (reproduces prod error)
- `POST /c/{company}/skladovy-pohyb?dry-run=true` → `success:true` (document validates, e.g. M-00393/2026)

`?dry-run=true` validates without committing — use it to test FlexiBee imports safely.

## Note on the error filter
An `ImportNotAllowedFilter` (`.../Manufacture/ErrorFilters/Filters/`) was added as a defensive net
so any future `importNotAllowed` shows a clean Czech message instead of the raw winstrom fallback.
Its message is deliberately cause-neutral (retry / contact admin) — do NOT reintroduce a
"check license/permissions" message; that was the wrong diagnosis. Filters auto-register via the
assembly scan in `ManufactureModule.cs`.
