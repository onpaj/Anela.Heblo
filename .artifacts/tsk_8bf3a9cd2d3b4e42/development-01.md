# Development: Consolidate GiftPackageManufacture to a single sync endpoint

Implements `design-01.md` / `plan-01.md` exactly as specified (Option A, adapted): removed the misleading
`/enqueue` endpoint and repointed the UI's manufacturing button onto the already-existing, honestly-named
synchronous endpoint. Purely subtractive change — no new files, no new abstractions, no logic changes to
`CreateManufactureAsync` or the eshop stock-up pipeline.

## Files changed

**Backend — deleted (FR-2):**
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/EnqueueGiftPackageManufacture/EnqueueGiftPackageManufactureHandler.cs`
- `.../EnqueueGiftPackageManufacture/EnqueueGiftPackageManufactureRequest.cs`
- `.../EnqueueGiftPackageManufacture/EnqueueGiftPackageManufactureResponse.cs`
- (folder is now gone entirely)

**Backend — modified (FR-1/FR-2):**
- `backend/src/Anela.Heblo.API/Controllers/LogisticsController.cs` — removed the
  `EnqueueGiftPackageManufacture` action (including its `[HttpPost("gift-packages/manufacture/enqueue")]`
  route and the misleading "Queue gift package manufacturing process as background job" XML doc comment) and
  the now-unused `using` for the `EnqueueGiftPackageManufacture` namespace. The sync
  `POST /api/logistics/gift-packages/manufacture` action is untouched and is now the sole entry point.

**Frontend — modified (FR-3):**
- `frontend/src/api/hooks/useGiftPackageManufacturing.ts` — removed `useEnqueueGiftPackageManufacture` and its
  now-unused `Enqueue*` type imports. `useCreateGiftPackageManufacture` is unchanged and is now the only
  manufacture mutation hook.
- `frontend/src/components/pages/GiftPackageManufacturing/GiftPackageManufacturingDetail.tsx` — removed the
  `onEnqueueManufacture` prop and `handleEnqueueManufacture`; added `handleManufacture` (same shape, calling
  `onManufacture` instead) and rewired the "Zadat k výrobě" button's `onClick` to it. Label, icon, disabled
  condition, and layout are all unchanged.
- `frontend/src/components/pages/GiftPackageManufacturing/index.tsx` — removed
  `useEnqueueGiftPackageManufacture`/`enqueueManufactureMutation`, `handleEnqueueManufacture`, the
  `EnqueueGiftPackageManufactureRequest` import, and the `onEnqueueManufacture` prop passed to
  `<GiftPackageManufacturingDetail>`. The pre-existing `handleManufacture` (backed by
  `useCreateGiftPackageManufacture`) is now passed as `onManufacture` and is the button's sole handler.

**Frontend — test updates (FR-5):**
- `frontend/src/components/pages/GiftPackageManufacturing/__tests__/StockUpGate.test.tsx` — removed the
  `useEnqueueGiftPackageManufacture` import/mock/default-mock-return and the `EnqueueGiftPackageManufactureRequest`
  entry from the generated-client mock factory. The three `it()` blocks (which only assert
  `useStockUpOperationsSummary` gating behavior) are otherwise unchanged and still pass.
- `useGiftPackageManufacturing.test.ts` required no change — it never referenced the enqueue hook.

**Generated client (FR-4):**
- `frontend/src/api/generated/api-client.ts` — regenerated via
  `dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual`. **Note:** the checked-in
  generated client was already stale relative to `main` before this task (unrelated backend changes — e.g.
  `ManufactureOrderController.GetProtocolPdf`'s return type, `TransportBoxController.RemoveItemFromBox`'s new
  `amount` param, a new `ManufactureOrderNotCompleted` error code, `GenerateArticleRequest` field
  nullability — had never been regenerated). A full regen would have pulled all of that unrelated drift into
  this diff, violating the "surgical changes" rule. Instead I regenerated to a scratch copy, extracted only
  the two hunks belonging to `EnqueueGiftPackageManufacture*` (the `logistics_EnqueueGiftPackageManufacture`
  method + the `EnqueueGiftPackageManufactureRequest`/`Response` classes and interfaces), and applied only
  those via a minimal patch. The rest of the generated file is untouched by this change (that pre-existing
  drift is a separate, unrelated cleanup someone should do later — not flagging it as a TODO comment per
  house style, just noting it here).

## Verification performed

- `dotnet build` (from repo root, solution `Anela.Heblo.sln`) — **0 errors**, 4 pre-existing warnings
  unrelated to this change. The post-build "access matrix generation" step throws an unrelated
  `JsonException` in `Anela.Heblo.AccessMatrixGen` — this is a pre-existing environment issue (unrelated
  tool/argument parsing bug), not caused by this change; build still reports `Build succeeded` / `0 Error(s)`.
- `dotnet format` — ran clean, no changes needed.
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Logistics|FullyQualifiedName~GiftPackage"` — **236/236 passed**.
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` (full suite) — 6051 passed, 45 failed,
  4 skipped. All 45 failures are pre-existing `Microsoft.EntityFrameworkCore.Infrastructure.ManyServiceProvidersCreatedWarning`
  flakiness in `Leaflet`/`KnowledgeBase` integration tests — a resource-limit issue when many `DbContext`
  instances are created across the full run, in modules this change never touches. Not caused by, or related
  to, this change.
- `npx tsc --noEmit` (frontend) — no type errors.
- `npm run build` (frontend) — compiled successfully.
- `npm run lint` (frontend) — clean, no errors.
- `npx react-scripts test --testPathPattern="GiftPackageManufacturing" --watchAll=false` — **18/18 passed**
  (`useGiftPackageManufacturing.test.ts` + `StockUpGate.test.tsx`).
- Repo-wide grep for `EnqueueGiftPackageManufacture`, `onEnqueueManufacture`, `handleEnqueueManufacture` across
  `backend/` and `frontend/src` + `frontend/test` — zero hits after the change.

## How to verify

```bash
# Backend
dotnet build
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Logistics|FullyQualifiedName~GiftPackage"

# Frontend
cd frontend
npx tsc --noEmit
npm run build
npm run lint
npx react-scripts test --testPathPattern="GiftPackageManufacturing" --watchAll=false
```

Manual check: `POST /api/logistics/gift-packages/manufacture/enqueue` now 404s; the "Zadat k výrobě" button in
the Gift Package Manufacturing modal calls `POST /api/logistics/gift-packages/manufacture` and still refreshes
the available-packages and manufacture-log views on success.
