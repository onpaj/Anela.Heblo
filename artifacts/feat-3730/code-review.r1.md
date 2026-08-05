## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `frontend/src/components/pages/ManufacturingStockAnalysis.tsx:269` — `handleRowExpand(item.productFamily!, item.code!)` uses a non-null assertion on `item.code`, but the generated `ManufacturingStockItemDto.code` is now typed `string | undefined` (NSwag widens all response DTO fields to optional). The button is already guarded by `shouldShowExpandButton(item)` (checks `productFamily`, not `code`), so if the backend ever returns an item with a falsy `code`, this assertion silently passes `undefined` through as a `string`. In practice `code` is always populated, so this is low-risk, but it's inconsistent with the optional-field discipline applied everywhere else in this refactor (e.g. `formatNumber`/`formatPercentage` widened to accept `undefined`).
- `frontend/src/api/hooks/useManufacturingStockAnalysis.ts:74-79` — `toGeneratedTimePeriod` is exported alongside the re-exported generated types/enums; consider co-locating its JSDoc reference to FR-3 with the actual re-export block above so a future reader scanning the "why do we re-export things" comment doesn't miss the conversion helper — minor readability nit only, not required.

### Notes (verified, no issue)
- The 15-arg positional call in both `useManufacturingStockAnalysisQuery`'s `queryFn` and `handleExport` matches `manufacturingStockAnalysis_GetStockAnalysis`'s generated signature exactly, argument-for-argument (verified against `frontend/src/api/generated/api-client.ts:7622`).
- `toGeneratedTimePeriod` correctly reproduces the pre-refactor Q9M-omission behavior (returns `undefined` when the app-level period is `Q9M`), and is now shared by both call sites (previously each duplicated the same conditional).
- `handleExport` correctly passes `undefined` for `pageNumber`/`pageSize`, matching the pre-refactor query-string builder which never included these params in the export path.
- The `ManufacturingStockSeverity` enum's representation change (numeric `0-4` → generated string enum `"Critical"..."Unconfigured"`) is safe: all consumers (`getRowColorClass`, `getSeverityStripColor`, `getStockValueColorClass`, `getManufacturingSeverityColorClass`, `getManufacturingSeverityDisplayText`) either compare against the enum members directly (unaffected by the underlying value) or already defensively handle both string and legacy numeric string forms (`String(severity)` switches with `case "Critical": case "0":` pairs) — this dual-handling predates the diff and isn't broken by it.
- Sending explicit `false`/`1` values for booleans and `salesMultiplier` (previously omitted when falsy/default) is a no-op behavior change: backend defaults (`SalesMultiplier = 1.0`, bool fields default `false`) match the explicitly-sent values, confirmed against `GetManufacturingStockAnalysisRequest.cs`.
- Sending `SearchTerm=""` explicitly (previously omitted when empty) is a no-op: the backend validator/handler treats `SearchTerm` via `string.IsNullOrWhiteSpace`, so empty and absent are equivalent.
- No `(apiClient as any)`, `.http.fetch(`, or manual `URLSearchParams` remains in any of the four reviewed files.
- Test files correctly updated to mock `apiClient.manufacturingStockAnalysis_GetStockAnalysis` (via existing `testUtils.ts` / `api/client` mocks) instead of `http.fetch`, and assert the full positional argument list — good regression protection against future argument-order mistakes.
