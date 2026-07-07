### task: remove-productmarginsegmentdto-aliases

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/ProductMarginSegmentDto.cs:20-26`
- Regenerate (do not hand-edit): `frontend/src/api/generated/api-client.ts`
- Modify: `frontend/src/components/pages/__tests__/ProductMarginSummary.test.tsx:32-57`

- [ ] **Step 1: Pre-flight repo-wide scan for hidden consumers**

  Run from the repository root (`/home/user/worktrees/feature-3466-Arch-Review-Analytics-Productmarginsegmentdto-Has`):

  ```bash
  grep -rn --include="*.cs" -e "\.ProductCode\b" -e "\.ProductName\b" -e "\.MarginPerPiece\b" -e "\.SellingPriceWithoutVat\b" -e "\.MaterialCosts\b" -e "\.LaborCosts\b" backend/src backend/test | grep -v "/obj/\|/bin/"
  ```

  For each hit, confirm the receiver expression is **not** a `ProductMarginSegmentDto` (hits on `AnalyticsProduct`, `MonthlyProductMarginDto`, `TopProductDto`, or other unrelated types are expected and out of scope — do not touch them). Then run:

  ```bash
  grep -rn -e "\.productCode\b" -e "\.productName\b" -e "\.marginPerPiece\b" -e "\.sellingPriceWithoutVat\b" -e "\.materialCosts\b" -e "\.laborCosts\b" frontend/src --include="*.ts" --include="*.tsx"
  ```

  Confirm the only in-scope hits are inside `frontend/src/components/pages/__tests__/ProductMarginSummary.test.tsx` (object-literal keys in the `productSegments` fixture, to be renamed in Step 8) — no dot-access (`segment.productCode` etc.) hits should appear anywhere, since `ProductMarginSummary.tsx` already reads only canonical fields. If any other in-scope hit appears, STOP — the plan needs to be extended before proceeding.

  This step makes no code changes. No commit.

- [ ] **Step 2: Confirm current build and tests pass before changing anything**

  ```bash
  dotnet build Anela.Heblo.sln
  ```

  Expect: `Build succeeded.` with 0 errors.

  ```bash
  dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~GetProductMarginSummaryHandlerTests"
  ```

  Expect: all tests in `GetProductMarginSummaryHandlerTests` pass. This establishes the pre-change baseline.

- [ ] **Step 3: Delete the six alias properties from `ProductMarginSegmentDto.cs`**

  Open `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/ProductMarginSegmentDto.cs`. The current full file content is:

  ```csharp
  namespace Anela.Heblo.Application.Features.Analytics.Contracts;

  public class ProductMarginSegmentDto
  {
      public string GroupKey { get; set; } = string.Empty; // Group identifier (product code, family, type)
      public string DisplayName { get; set; } = string.Empty; // Display name for the group
      public decimal MarginContribution { get; set; } // Total margin for this group in this month
      public decimal Percentage { get; set; } // Percentage of monthly total margin
      public string ColorCode { get; set; } = string.Empty; // Hex color for consistency
      public bool IsOther { get; set; } = false; // True for "Other" category

      // Tooltip detail information (aggregated for group)
      public decimal AverageMarginPerPiece { get; set; } // Average margin per piece in group
      public int UnitsSold { get; set; } // Total units sold in this month for group
      public decimal AverageSellingPriceWithoutVat { get; set; } // Average selling price in group
      public decimal AverageMaterialCosts { get; set; } // Average material costs in group
      public decimal AverageLaborCosts { get; set; } // Average labor costs in group
      public int ProductCount { get; set; } // Number of products in this group

      // Keep for backward compatibility
      public string ProductCode => GroupKey;
      public string ProductName => DisplayName;
      public decimal MarginPerPiece => AverageMarginPerPiece;
      public decimal SellingPriceWithoutVat => AverageSellingPriceWithoutVat;
      public decimal MaterialCosts => AverageMaterialCosts;
      public decimal LaborCosts => AverageLaborCosts;
  }
  ```

  Replace the whole file with:

  ```csharp
  namespace Anela.Heblo.Application.Features.Analytics.Contracts;

  public class ProductMarginSegmentDto
  {
      public string GroupKey { get; set; } = string.Empty; // Group identifier (product code, family, type)
      public string DisplayName { get; set; } = string.Empty; // Display name for the group
      public decimal MarginContribution { get; set; } // Total margin for this group in this month
      public decimal Percentage { get; set; } // Percentage of monthly total margin
      public string ColorCode { get; set; } = string.Empty; // Hex color for consistency
      public bool IsOther { get; set; } = false; // True for "Other" category

      // Tooltip detail information (aggregated for group)
      public decimal AverageMarginPerPiece { get; set; } // Average margin per piece in group
      public int UnitsSold { get; set; } // Total units sold in this month for group
      public decimal AverageSellingPriceWithoutVat { get; set; } // Average selling price in group
      public decimal AverageMaterialCosts { get; set; } // Average material costs in group
      public decimal AverageLaborCosts { get; set; } // Average labor costs in group
      public int ProductCount { get; set; } // Number of products in this group
  }
  ```

  In other words: delete the blank line before `// Keep for backward compatibility`, that comment, and the six one-line alias properties (the original lines 19-26). Nothing else in the file changes.

- [ ] **Step 4: Build and test the backend after deletion**

  ```bash
  dotnet build Anela.Heblo.sln
  ```

  Expect: `Build succeeded.` with 0 errors, 0 new warnings. If this fails with a compile error referencing `.ProductCode`, `.ProductName`, `.MarginPerPiece`, `.SellingPriceWithoutVat`, `.MaterialCosts`, or `.LaborCosts` on a `ProductMarginSegmentDto`, that is a consumer the Step 1 scan missed — stop and inspect it (do not silently re-add the alias).

  ```bash
  dotnet test Anela.Heblo.sln
  ```

  Expect: same pass count as the Step 2 baseline (no new failures, no failures removed).

- [ ] **Step 5: Commit the backend change**

  ```bash
  git add backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/ProductMarginSegmentDto.cs
  git commit -m "Remove backward-compatibility alias properties from ProductMarginSegmentDto"
  ```

- [ ] **Step 6: Regenerate the frontend TypeScript client**

  From the repository root:

  ```bash
  dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
  ```

  Expect console output including `Generating TypeScript API client for frontend...` and `Frontend API client generation completed.` with no errors. This overwrites `frontend/src/api/generated/api-client.ts` in place via NSwag reading `backend/src/Anela.Heblo.API/nswag.frontend.json`.

- [ ] **Step 7: Verify the regenerated client diff is scoped to this DTO only**

  ```bash
  git diff --stat frontend/src/api/generated/api-client.ts
  ```

  Then inspect the actual diff:

  ```bash
  git diff frontend/src/api/generated/api-client.ts
  ```

  Confirm:
  - Only the `ProductMarginSegmentDto` class and `IProductMarginSegmentDto` interface are touched (their property declarations, plus their `init`/`toJSON` (de)serialization blocks lose the six fields `productCode`, `productName`, `marginPerPiece`, `sellingPriceWithoutVat`, `materialCosts`, `laborCosts`).
  - No other interface, class, or controller method signature in the file changes.

  If any unrelated change appears elsewhere in the file (e.g. unrelated endpoints, unrelated DTOs, or NSwag header/version comments only — header/timestamp-only diffs are expected and fine), confirm it is not a substantive shape change before proceeding. If a substantive unrelated change appears, stop and investigate before committing.

  Run the client regeneration a second time to confirm idempotency:

  ```bash
  dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
  git status --short frontend/src/api/generated/api-client.ts
  ```

  Expect: no further changes reported (empty output), confirming the regeneration is idempotent.

- [ ] **Step 8: Rename the stale alias fields in the `ProductMarginSummary.test.tsx` fixture**

  Open `frontend/src/components/pages/__tests__/ProductMarginSummary.test.tsx`. The current `productSegments` array (lines 31-58) reads:

  ```typescript
      productSegments: [
        {
          productCode: "PROD001",
          productName: "Product 1",
          marginContribution: 1500,
          percentage: 60,
          colorCode: "#2563EB",
          marginPerPiece: 100,
          unitsSold: 15,
          sellingPriceWithoutVat: 150,
          materialCosts: 30,
          laborCosts: 20,
          isOther: false,
        },
        {
          productCode: "OTHER",
          productName: "Ostatní produkty",
          marginContribution: 1000,
          percentage: 40,
          colorCode: "#9CA3AF",
          marginPerPiece: 0,
          unitsSold: 0,
          sellingPriceWithoutVat: 0,
          materialCosts: 0,
          laborCosts: 0,
          isOther: true,
        },
      ],
  ```

  Replace it with:

  ```typescript
      productSegments: [
        {
          groupKey: "PROD001",
          displayName: "Product 1",
          marginContribution: 1500,
          percentage: 60,
          colorCode: "#2563EB",
          averageMarginPerPiece: 100,
          unitsSold: 15,
          averageSellingPriceWithoutVat: 150,
          averageMaterialCosts: 30,
          averageLaborCosts: 20,
          isOther: false,
        },
        {
          groupKey: "OTHER",
          displayName: "Ostatní produkty",
          marginContribution: 1000,
          percentage: 40,
          colorCode: "#9CA3AF",
          averageMarginPerPiece: 0,
          unitsSold: 0,
          averageSellingPriceWithoutVat: 0,
          averageMaterialCosts: 0,
          averageLaborCosts: 0,
          isOther: true,
        },
      ],
  ```

  Do not touch the `topProducts` fixture block (lines 62-70) or any other part of the file — it already uses canonical names (`groupKey`, `displayName`).

- [ ] **Step 9: Run the frontend test suite for this file**

  From `frontend/`:

  ```bash
  cd frontend
  CI=true npx react-scripts test src/components/pages/__tests__/ProductMarginSummary.test.tsx --watchAll=false
  ```

  Expect: all tests in this file pass (same pass count as before the rename — the fixture rename does not change test behavior since the tooltip callback reading these fields is never invoked under the mocked `Chart` component).

- [ ] **Step 10: Run frontend lint and build**

  From `frontend/`:

  ```bash
  npm run lint
  ```

  Expect: no new lint errors.

  ```bash
  npm run build
  ```

  Expect: build succeeds with no TypeScript errors. (This does not re-trigger client generation since no `prebuild`/`generate-client` script exists in this repo's `package.json`; the client was already regenerated in Step 6.)

- [ ] **Step 11: Final FR-4 regression grep**

  From the repository root:

  ```bash
  grep -nE "\.productCode|\.productName|\.marginPerPiece|\.sellingPriceWithoutVat|\.materialCosts|\.laborCosts" frontend/src/components/pages/ProductMarginSummary.tsx frontend/src/components/pages/__tests__/ProductMarginSummary.test.tsx
  ```

  Expect: no matches (empty output).

- [ ] **Step 12: Commit the frontend changes**

  ```bash
  git add frontend/src/api/generated/api-client.ts frontend/src/components/pages/__tests__/ProductMarginSummary.test.tsx
  git commit -m "Regenerate API client and rename stale alias fixture fields for ProductMarginSegmentDto"
  ```
