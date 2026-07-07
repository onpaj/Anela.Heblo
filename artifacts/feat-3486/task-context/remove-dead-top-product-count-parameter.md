### task: remove-dead-top-product-count-parameter

**Goal**
Delete the unused `TopProductCount` property from `GetProductMarginSummaryRequest`, regenerate the frontend OpenAPI TypeScript client, and update the frontend hook call site to match the new (shorter) generated method signature — with no behavior change anywhere, since the property was never read by the handler.

**Context** (self-contained — read this fully; you will not see the spec/arch-review/design docs)

This is an internal analytics endpoint: `GET /api/analytics/GetProductMarginSummary` (via `AnalyticsController.GetProductMarginSummary`), backed by MediatR request `GetProductMarginSummaryRequest` → `GetProductMarginSummaryHandler` → `GetProductMarginSummaryResponse`.

`GetProductMarginSummaryRequest.TopProductCount` is a documented "top N" parameter (default `15`) left over from an older, pre-refactor version of the handler. The current handler (header comment: "🔒 PERFORMANCE FIX: Refactored handler using streaming architecture") streams **all** matching products, computes margin data for **every** group in `GenerateTopProducts`, and returns the complete list in `TopProducts`. `TopProductCount` is never referenced anywhere in `GetProductMarginSummaryHandler.Handle` or `GenerateTopProducts` — it has zero effect on the response. Verified by reading the full handler file: no `Take(...)`, no `request.TopProductCount` reference anywhere.

The only caller, `useProductMarginSummaryQuery` (`frontend/src/api/hooks/useProductMarginSummary.ts`), already works around this by hard-coding `0` for this argument with the comment `// topProductCount = 0 means no limit` — because `ProductMarginSummary.tsx` needs the complete, untruncated group list to do its own client-side "top 15 + Other" chart bucketing, its full results table, and its "Celkem skupin" (total groups) count. This is not a bug workaround; the frontend genuinely needs the full list, so **removing** the parameter (rather than implementing real server-side truncation) is the correct, final, binding decision — already made by the architect. Do not implement truncation.

Current state of the request DTO (confirmed by reading the file), property declaration order matters because NSwag generates **positional** TypeScript method arguments in this exact order:

```csharp
// backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryRequest.cs
using Anela.Heblo.Domain.Features.Analytics;
using MediatR;

namespace Anela.Heblo.Application.Features.Analytics.UseCases.GetProductMarginSummary;

public class GetProductMarginSummaryRequest : IRequest<GetProductMarginSummaryResponse>
{
    public string TimeWindow { get; set; } = "current-year"; // current-year, current-and-previous-year, last-6-months, last-12-months, last-24-months
    public int TopProductCount { get; set; } = 15; // Configurable, default 15
    public ProductGroupingMode GroupingMode { get; set; } = ProductGroupingMode.Products; // Products, ProductFamily, ProductType

    // Margin level for display (determines which margin values to show)
    public MarginLevel MarginLevel { get; set; } = MarginLevel.M2;

    // Sorting parameters
    public string? SortBy { get; set; } // Column to sort by (m0percentage, m1amount, totalmargin, etc.)
    public bool SortDescending { get; set; } = true; // Default descending for margin sorting
}
```

After deleting the `TopProductCount` line, the remaining property order is `TimeWindow, GroupingMode, MarginLevel, SortBy, SortDescending` — so the regenerated TypeScript method `analytics_GetProductMarginSummary` will take exactly these 5 parameters, in this order (confirmed: current generated signature is `analytics_GetProductMarginSummary(timeWindow, topProductCount, groupingMode, marginLevel, sortBy, sortDescending)` in `frontend/src/api/generated/api-client.ts:51` — removing the DTO property removes only the `topProductCount` argument, all other positions/types are unchanged).

The controller does **not** need any change:
```csharp
// backend/src/Anela.Heblo.API/Controllers/AnalyticsController.cs:32
public async Task<ActionResult<GetProductMarginSummaryResponse>> GetProductMarginSummary([FromQuery] GetProductMarginSummaryRequest request)
```
`[FromQuery]` binds the whole DTO — the query string simply loses one recognized parameter (`TopProductCount`).

`GetProductMarginSummaryHandler.Handle` and `GenerateTopProducts` require **zero** code changes — confirmed by reading the full handler file, neither method references `request.TopProductCount`.

Current state of the frontend hook (confirmed by reading the file):

```typescript
// frontend/src/api/hooks/useProductMarginSummary.ts
import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import {
  GetProductMarginSummaryResponse,
  ProductGroupingMode,
  MarginLevel,
} from "../generated/api-client";

// Re-export the generated types for convenience
export { GetProductMarginSummaryResponse, ProductGroupingMode, MarginLevel };

export const useProductMarginSummaryQuery = (
  timeWindow: string = "current-year",
  groupingMode: ProductGroupingMode = ProductGroupingMode.Products,
  marginLevel: MarginLevel = MarginLevel.M2,
) => {
  return useQuery<GetProductMarginSummaryResponse, Error>({
    queryKey: [...QUERY_KEYS.productMarginSummary, timeWindow, groupingMode, marginLevel],
    queryFn: async () => {
      const apiClient = await getAuthenticatedApiClient();
      // Use sortBy parameter to sort by the selected margin level percentage (descending)
      const sortBy = `totalmargin`;

      // Use generated API client method with proper parameters
      return apiClient.analytics_GetProductMarginSummary(
        timeWindow,
        0, // topProductCount = 0 means no limit
        groupingMode,
        marginLevel,
        sortBy,
        true // sortDescending = true
      );
    },
    staleTime: 5 * 60 * 1000, // Consider data stale after 5 minutes
    gcTime: 10 * 60 * 1000, // Keep cache for 10 minutes
  });
};
```

Only the `0, // topProductCount = 0 means no limit` line needs to be deleted here — nothing else in this file changes.

`ProductMarginSummary.tsx` (the component consuming this hook) needs **no changes at all** — it already consumes the full `data.topProducts` array for its client-side top-15/"Other" chart bucketing, full table, and total-group count. The response shape (`GetProductMarginSummaryResponse.TopProducts: List<TopProductDto>`) is completely unaffected by this change.

`frontend/src/components/pages/__tests__/ProductMarginSummary.test.tsx` mocks the hook module itself (`jest.mock("../../../api/hooks/useProductMarginSummary")`), not the underlying generated client call — confirmed by reading the test file. It does not reference `topProductCount` anywhere, so it needs no changes and should continue to pass unmodified.

`backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs` was checked in full: none of its test cases set or assert `TopProductCount` on any `GetProductMarginSummaryRequest` instance anywhere. No test changes are needed there.

A repo-wide grep for `TopProductCount`/`topProductCount` found exactly these matches, all of which are handled by this task:
1. `frontend/src/api/hooks/useProductMarginSummary.ts` — the `0, // topProductCount...` argument (removed in this task)
2. `frontend/src/api/generated/api-client.ts` — generated file (regenerated in this task, not hand-edited)
3. `docs/superpowers/plans/2026-06-10-analytics-margin-level-enum.md` — historical plan document, out of scope, leave as-is
4. `docs/features/product-margin-summary.md` — historical pre-refactor feature doc, out of scope per this feature's spec, leave as-is
5. `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryRequest.cs` — the property declaration (removed in this task)

The frontend client is regenerated via an MSBuild target defined in `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`:
```xml
<Target Name="GenerateFrontendClientManual">
    <Message Text="Generating TypeScript API client for frontend..." Importance="high" />
    <Exec Command="dotnet nswag run nswag.frontend.json" ContinueOnError="true" WorkingDirectory="$(MSBuildThisFileDirectory)" />
    <Message Text="Frontend API client generation completed." Importance="high" />
</Target>
```
Note: this repository's `frontend/package.json` does **not** currently have a `generate-client`/`prebuild` npm script (checked directly — only `start`, `start:automation`, `start:conductor`, `build`, `test`, `test:playwright`, `eject`, `lint`, `lint:fix`, `prepare` exist). The only reliable way to regenerate the client is the `dotnet msbuild` command below; do not rely on an npm script that doesn't exist in this checkout.

**Files to create/modify**
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryRequest.cs`
- Regenerate (do not hand-edit): `frontend/src/api/generated/api-client.ts`
- Modify: `frontend/src/api/hooks/useProductMarginSummary.ts`

**Implementation steps**

1. Open `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryRequest.cs` and delete the `TopProductCount` line, leaving:
```csharp
using Anela.Heblo.Domain.Features.Analytics;
using MediatR;

namespace Anela.Heblo.Application.Features.Analytics.UseCases.GetProductMarginSummary;

public class GetProductMarginSummaryRequest : IRequest<GetProductMarginSummaryResponse>
{
    public string TimeWindow { get; set; } = "current-year"; // current-year, current-and-previous-year, last-6-months, last-12-months, last-24-months
    public ProductGroupingMode GroupingMode { get; set; } = ProductGroupingMode.Products; // Products, ProductFamily, ProductType

    // Margin level for display (determines which margin values to show)
    public MarginLevel MarginLevel { get; set; } = MarginLevel.M2;

    // Sorting parameters
    public string? SortBy { get; set; } // Column to sort by (m0percentage, m1amount, totalmargin, etc.)
    public bool SortDescending { get; set; } = true; // Default descending for margin sorting
}
```

2. Build the backend to confirm the DTO change compiles cleanly (no other code references `TopProductCount`):
```bash
dotnet build backend/src/Anela.Heblo.Application
```
Expected: `Build succeeded.` with 0 errors.

3. Build the API project in Debug configuration so the OpenAPI spec used for client generation reflects the change, then regenerate the frontend TypeScript client:
```bash
dotnet build backend/src/Anela.Heblo.API
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
```
Expected: both commands complete without errors; the second prints `Generating TypeScript API client for frontend...` followed by `Frontend API client generation completed.`

4. Open `frontend/src/api/generated/api-client.ts` and confirm the `analytics_GetProductMarginSummary` method signature no longer has a `topProductCount` parameter — it should now read (order per remaining DTO property order `TimeWindow, GroupingMode, MarginLevel, SortBy, SortDescending`):
```typescript
analytics_GetProductMarginSummary(timeWindow: string | undefined, groupingMode: ProductGroupingMode | undefined, marginLevel: MarginLevel | undefined, sortBy: string | null | undefined, sortDescending: boolean | undefined): Promise<GetProductMarginSummaryResponse> {
```
Do not hand-edit this file — it must come from the regeneration step above. If the signature still contains `topProductCount`, re-run step 3; do not proceed until the generated file reflects the DTO change.

5. Open `frontend/src/api/hooks/useProductMarginSummary.ts` and remove the `0, // topProductCount = 0 means no limit` line from the `analytics_GetProductMarginSummary` call, so the `queryFn` body becomes:
```typescript
    queryFn: async () => {
      const apiClient = await getAuthenticatedApiClient();
      // Use sortBy parameter to sort by the selected margin level percentage (descending)
      const sortBy = `totalmargin`;

      // Use generated API client method with proper parameters
      return apiClient.analytics_GetProductMarginSummary(
        timeWindow,
        groupingMode,
        marginLevel,
        sortBy,
        true // sortDescending = true
      );
    },
```
Leave every other line in the file (imports, exports, function signature, `queryKey`, `staleTime`, `gcTime`) exactly as-is.

6. Run the backend test suite for this feature to confirm no regressions:
```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~GetProductMarginSummaryHandlerTests"
```
Expected: all existing tests pass (`Handle_ValidRequest_ReturnsCorrectResponse`, `Handle_DifferentTimeWindows_ParsesCorrectly`, `Handle_EmptyProductList_ReturnsZeroMargin`, `Handle_WithMockedDependencies_InvokesCalculatorAndBreakdownGenerator`, `GetMarginAmountForLevel_WithUndefinedEnumValue_ThrowsArgumentOutOfRangeException`, `ParseTimeWindow_UnknownValue_ThrowsArgumentException`) — none of them reference `TopProductCount`, so no test code changes are required; this step is verification only.

7. Run the full backend build and formatting check per the repo's standard validation gate:
```bash
dotnet build backend/Anela.Heblo.sln
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```
Expected: build succeeds with 0 errors; `dotnet format` reports no changes needed (if it reports changes, run `dotnet format backend/Anela.Heblo.sln` to apply them and re-verify).

8. Run the frontend test suite for the affected component to confirm no regressions:
```bash
cd frontend
CI=true npm test -- --testPathPattern=ProductMarginSummary
```
Expected: `ProductMarginSummary.test.tsx` passes unchanged — it mocks the hook module directly and never asserted on `topProductCount`.

9. Run the full frontend build and lint per the repo's standard validation gate:
```bash
cd frontend
npm run build
npm run lint
```
Expected: `npm run build` completes with no TypeScript errors (this is the step that would catch any remaining `topProductCount` argument-count mismatch between the hook and the regenerated client); `npm run lint` reports no new errors.

**Tests to write**
No new tests are required. This is a subtractive, behavior-preserving change to a parameter that was never read by any code path, so there is no new behavior to cover. The existing backend test suite (`GetProductMarginSummaryHandlerTests.cs`) and frontend test suite (`ProductMarginSummary.test.tsx`) already provide regression coverage for the unchanged behavior and are re-run as verification in steps 6 and 8 above — do not add tests asserting the absence of a property or a parameter, as that would be testing the compiler/type system rather than behavior.

**Acceptance criteria**
- `grep -n "TopProductCount" backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryRequest.cs` returns no matches.
- `grep -n "topProductCount" frontend/src/api/generated/api-client.ts` returns no matches (confirms regeneration succeeded).
- `grep -n "topProductCount" frontend/src/api/hooks/useProductMarginSummary.ts` returns no matches.
- `dotnet build backend/Anela.Heblo.sln` succeeds with 0 errors.
- `dotnet format backend/Anela.Heblo.sln --verify-no-changes` reports no formatting diffs.
- `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~GetProductMarginSummaryHandlerTests"` — all tests pass.
- `cd frontend && npm run build` succeeds with no TypeScript errors.
- `cd frontend && npm run lint` reports no new errors.
- `cd frontend && CI=true npm test -- --testPathPattern=ProductMarginSummary` passes.
- Manual smoke check: start the app, navigate to the Analytics → Product Margin Summary page, and confirm the chart (top 15 + "Ostatní produkty"), the full results table, and the "Celkem skupin" total-group count all render identically to before the change (same data, since the handler's behavior is unchanged).
