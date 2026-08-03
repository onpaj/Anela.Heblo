# Development — Remove dead DateFrom/DateTo from GetProductMarginsRequest

## Decision implemented

Per `design-02.md` / `plan-02.md` / `architecture-02.md`: **removed** the two dead-on-arrival `DateFrom`/`DateTo` parameters rather than implementing filtering behind them, consistent with the `#3486`/`#3487` precedent. Margin data comes from pre-aggregated `product.Margins` computed by a background refresh task; a request-time date range structurally cannot be applied to that data, so honouring the filter would require a much larger architectural change than this finding calls for.

## Files changed

1. **`backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsRequest.cs`**
   Deleted the two unread properties:
   ```csharp
   public DateTime? DateFrom { get; set; }
   public DateTime? DateTo { get; set; }
   ```
   `GetProductMarginsHandler.Handle` never referenced these (confirmed by grep — zero matches), so this is a zero-behavior-change deletion. The handler's hardcoded `AddMonths(-13)` window is untouched and out of scope.

2. **`frontend/src/api/generated/api-client.ts`** (generated file)
   Ran `dotnet msbuild -t:GenerateFrontendClientManual` (per `docs/development/api-client-generation.md`) to regenerate from the trimmed OpenAPI contract. The regeneration also pulled in unrelated drift from other in-flight backend changes on this branch that hadn't yet been reflected in the checked-in generated file (e.g. `manufactureOrder_GetProtocolPdf` return type, `transportBox_RemoveItemFromBox` signature, a new `GetManufactureProtocolResponse` type, `GenerateArticleRequest` field nullability). That drift is out of scope for this task, so I reverted the full regeneration and hand-applied only the `productMargins_GetProductMargins` hunk — verified against the generator's actual output — removing the trailing `dateFrom`/`dateTo` parameters and the two `DateFrom=...`/`DateTo=...` query-string blocks. The final diff for this file touches only that one method.

3. **`frontend/src/api/hooks/useProductMargins.ts`**
   Dropped the two trailing parameters (`dateFrom?: Date`, `dateTo?: Date`) from `useProductMarginsQuery`, removed them from the `queryKey` array, and removed the two `dateFrom || null`, `dateTo || null` arguments passed to `apiClient.productMargins_GetProductMargins(...)`.

## Files intentionally not changed

- `backend/src/Anela.Heblo.API/Controllers/ProductMarginsController.cs` — binds the whole `GetProductMarginsRequest` via `[FromQuery]`; no code change needed since the properties are simply gone from the bound type.
- `frontend/src/components/pages/ProductMarginsList.tsx` — sole call site already passes exactly 7 positional arguments and never passed `dateFrom`/`dateTo`; remains source-compatible with the trimmed hook signature with no edit.
- `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs` — none of its five request constructions set `DateFrom`/`DateTo`; the `expectedDateFrom` symbol in this file is an unrelated local test variable for the hardcoded 13-month window, not the removed DTO property. No changes needed, and the suite still passes.
- `frontend/src/components/pages/__tests__/ProductMarginsList.test.tsx` — doesn't reference `dateFrom`/`dateTo`, mocks the hook's return value rather than its call arity. No changes needed.

No new tests were added because this is a pure contract-narrowing deletion with no new behavior to cover — the existing test suites already exercise the unaffected code paths and now pass unchanged against the trimmed contract.

## Validation performed

- `dotnet build` (full solution) — 0 errors, pre-existing warnings only (163, unrelated to this change).
- `dotnet msbuild -t:GenerateFrontendClientManual` — regenerated OpenAPI client; drift limited to `productMargins_GetProductMargins` as described above.
- `dotnet format --no-restore Anela.Heblo.sln` — clean, no changes needed.
- `dotnet test --filter "FullyQualifiedName~GetProductMarginsHandlerTests"` — 5/5 passed.
- `npm run build` — compiled successfully (TypeScript typecheck passes with the trimmed hook/client signatures).
- `npm run lint` — pre-existing repo-wide `testing-library/*` failures unrelated to this change (verified via `git stash` that the same errors, e.g. `ProductMarginsList.test.tsx:294`, exist on the base branch before this change). No new lint errors introduced by this change.
- Repo-wide grep for `DateFrom`/`DateTo`/`dateFrom`/`dateTo` scoped to the `productMargins`/`useProductMarginsQuery` surface confirms no stray reference survives.

## How to verify

```bash
# Backend
cd backend
dotnet build
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetProductMarginsHandlerTests"

# Frontend
cd frontend
npm run build
```

`git diff --stat` shows exactly 3 files changed: the request DTO, the generated client (one method), and the hook.
