# Manufacture Finalization Redesign: Proportional Residue Distribution

## Context

During cosmetics manufacturing, the semiproduct phase produces an actual quantity (e.g., 4000g cream base) that rarely matches the theoretical consumption when filling into final products (e.g., 30x100g + 80x10g = 3800g). Currently, this mismatch is handled by a separate "discard residue" step that reads remaining stock and creates a third ERP document. This is fragile, creates unnecessary complexity, and doesn't accurately represent what happened in production.

**Goal**: Replace the discard step with proportional distribution of the actual semiproduct across products during the product manufacture step, and update BoMs to reflect actual weights.

## New Product Manufacture Flow

**Semiproduct manufacture stays unchanged.**

**Product manufacture (multi-phase only, single-phase is skipped):**

1. User enters actual product quantities (pieces) and clicks "Confirm"
2. System calculates:
   - Theoretical semiproduct consumption per product = BoM grams-per-unit x actual pieces
   - Total theoretical consumption = sum across all products
   - Actual semiproduct made = `SemiProduct.ActualQuantity` (grams)
   - Difference = actual - theoretical (positive = residue, negative = deficit)
   - Difference% = |difference| / theoretical x 100
3. Safe zone check against semiproduct's `AllowedResiduePercentage`:
   - Within threshold → proceed automatically
   - Outside threshold → return distribution preview to frontend for user confirmation
4. After auto-approve or user confirmation:
   a. **Consumption doc** (semiproduct outbound, grams): each product's share = `actualSemiProduct x (productTheoretical / totalTheoretical)`, total = actual semiproduct made
   b. **Production doc** (products inbound, pieces): same as before — actual piece counts, unit price = (product's semiproduct cost share) / pieces
   c. **BoM update**: for each product, update the semiproduct ingredient amount to `adjustedConsumption / actualPieces` grams per unit
5. Update order status to Completed

**No separate discard step. No discard document.**

### Example: Residue (excess semiproduct)

- Made: 4000g semiproduct
- Products: product1 = 30x100g (3000g), product2 = 80x10g (800g) → theoretical = 3800g
- Residue: +200g (5.26%)
- Distribution ratio: product1 = 3000/3800 = 78.95%, product2 = 800/3800 = 21.05%
- Consumption doc: product1 → 3157.9g, product2 → 842.1g (total = 4000g)
- Production doc: product1 → 30 pieces, product2 → 80 pieces (unchanged)
- BoM update: product1 = 105.26 g/unit, product2 = 10.53 g/unit

### Example: Deficit (not enough semiproduct)

- Made: 4000g semiproduct
- Products: product1 = 30x100g (3000g), product2 = 120x10g (1200g) → theoretical = 4200g
- Deficit: -200g (4.76%)
- Distribution ratio: product1 = 3000/4200 = 71.43%, product2 = 1200/4200 = 28.57%
- Consumption doc: product1 → 2857.1g, product2 → 1142.9g (total = 4000g)
- Production doc: product1 → 30 pieces, product2 → 120 pieces (unchanged)
- BoM update: product1 = 95.24 g/unit, product2 = 9.52 g/unit

## Confirmation Mechanism

Single endpoint `POST /{id}/confirm-products` with `OverrideConfirmed` flag:

- **First call** (`overrideConfirmed: false`, default):
  - If within `AllowedResiduePercentage` → completes automatically (single round-trip)
  - If outside → returns `200 OK` with `requiresConfirmation: true` + `ResidueDistribution` preview
- **Second call** (`overrideConfirmed: true`):
  - Proceeds with distribution regardless of threshold

Frontend shows a confirmation modal with a table of adjusted amounts when `requiresConfirmation` is returned.

## New Domain Types

### `ResidueDistribution` (value object)

Location: `Domain/Features/Manufacture/ResidueDistribution.cs`

```
ResidueDistribution
├── ActualSemiProductQuantity: decimal     (grams made)
├── TheoreticalConsumption: decimal        (grams needed by BoM)
├── Difference: decimal                    (positive=residue, negative=deficit)
├── DifferencePercentage: double           (|diff| / theoretical * 100)
├── IsWithinAllowedThreshold: bool
├── AllowedResiduePercentage: double
└── Products: List<ProductConsumptionDistribution>
    ├── ProductCode: string
    ├── ProductName: string
    ├── ActualPieces: decimal              (pieces confirmed by user)
    ├── TheoreticalGramsPerUnit: decimal   (from BoM)
    ├── TheoreticalConsumption: decimal    (BoM * pieces)
    ├── AdjustedConsumption: decimal       (proportional share of actual semiproduct)
    ├── AdjustedGramsPerUnit: decimal      (for BoM update)
    └── ProportionRatio: double            (this product's share)
```

## New Service: `ResidueDistributionCalculator`

Location: `Application/Features/Manufacture/Services/ResidueDistributionCalculator.cs`

Interface: `IResidueDistributionCalculator`

```csharp
Task<ResidueDistribution> CalculateAsync(
    UpdateManufactureOrderDto order,
    CancellationToken cancellationToken = default);
```

**Logic:**
1. Get `SemiProduct.ActualQuantity` as actual grams
2. For each product, call `IManufactureClient.GetManufactureTemplateAsync(productCode)` to find the semiproduct ingredient's BoM amount per unit
3. Calculate `theoreticalConsumption = bomGramsPerUnit * actualPieces` per product
4. Sum all theoretical consumptions
5. Get `AllowedResiduePercentage` from semiproduct's `CatalogProperties` via `ICatalogRepository`
6. Calculate difference and percentage
7. Distribute proportionally: `adjustedConsumption = actualSemiProduct * (productTheoretical / totalTheoretical)`
8. Handle rounding: assign remainder to the largest product to ensure exact sum

**Dependencies**: `IManufactureClient`, `ICatalogRepository`

## Changes to Existing Code

### `IManufactureClient` (Domain)

```diff
+ Task UpdateBoMIngredientAmountAsync(string productCode, string ingredientCode, double newAmount, CancellationToken ct = default);
- Task<DiscardResidualSemiProductResponse> DiscardResidualSemiProductAsync(DiscardResidualSemiProductRequest request, CancellationToken ct = default);
```

### `FlexiManufactureClient` (Adapter)

**Modify `SubmitManufacturePerProductAsync`:**
- Accept `ResidueDistribution` via a new property on `SubmitManufactureClientRequest`: `ResidueDistribution? Distribution`
- When `Distribution` is set (multi-phase product manufacture): use `AdjustedConsumption` per product for the semiproduct ingredient in the consumption doc instead of BoM-calculated amounts
- Non-semiproduct ingredients (raw materials that are direct product ingredients) continue using BoM-based calculation
- Production doc stays the same (piece-based amounts)

**Add `UpdateBoMIngredientAmountAsync`:**
- Fetch BoM via `_bomClient.GetAsync(productCode)`
- Find the semiproduct ingredient item
- Update its amount to the new grams-per-unit value
- Save via FlexiBee SDK or raw REST API

**Remove:**
- `DiscardResidualSemiProductAsync` method
- `CreateDescription(DiscardResidualSemiProductRequest)` overload

### `ManufactureOrderApplicationService`

**Modify `ConfirmProductCompletionAsync`:**

```
1. Update actual product quantities in DB (unchanged)
2. Calculate distribution via IResidueDistributionCalculator
3. IF !distribution.IsWithinAllowedThreshold AND !overrideConfirmed:
   → return ConfirmProductCompletionResult { RequiresConfirmation = true, Distribution = ... }
4. Submit manufacture to ERP with adjusted amounts (consumption + production docs)
5. Update BoM for each product via IManufactureClient.UpdateBoMIngredientAmountAsync
6. Update order status to Completed (no discard document reference)
```

**Remove:**
- `DiscardResidueMaterial` private method
- All `discardResiduesResult` references

### API Contracts

**`ConfirmProductCompletionRequest`** — add `OverrideConfirmed: bool = false`

**`ConfirmProductCompletionResult`** — add:
- `RequiresConfirmation: bool`
- `Distribution: ResidueDistribution?`

**`ConfirmProductCompletionResponse`** — add:
- `RequiresConfirmation: bool`
- `Distribution: ResidueDistributionDto?` (serializable DTO for the API response)

**Controller** — pass `request.OverrideConfirmed` to service, handle `RequiresConfirmation` response as `200 OK` (not error)

### `UpdateManufactureOrderStatusRequest`

Stop passing `DiscardRedisueDocumentCode` (always null). Leave the DB column for now — remove in a future migration.

## Files to Remove

- `Application/Features/Manufacture/UseCases/DiscardResidualSemiProduct/DiscardResidualSemiProductRequest.cs`
- `Application/Features/Manufacture/UseCases/DiscardResidualSemiProduct/DiscardResidualSemiProductResponse.cs`
- `Application/Features/Manufacture/UseCases/DiscardResidualSemiProduct/DiscardResidualSemiProductHandler.cs`
- `Domain/Features/Manufacture/DiscardResidualSemiProductRequest.cs`
- `Domain/Features/Manufacture/DiscardResidualSemiProductResponse.cs`

## Frontend Changes (High Level)

1. **Regenerate API client** from updated Swagger spec
2. **Confirmation modal**: when `requiresConfirmation` returned, show a table:
   | Product | Pieces | Theoretical (g) | Adjusted (g) | g/unit (new) |
   With difference percentage prominently displayed and warning styling
3. **Confirm button** re-calls endpoint with `overrideConfirmed: true`
4. **Remove** discard-related UI (`ErpDiscardResidueDocumentNumber` display)

## Edge Cases

- **Single-phase manufacture**: Skip residue distribution entirely (product code == semiproduct code). No BoM update needed.
- **Rounding**: Distribute remainder to the largest product to ensure adjusted amounts sum exactly to actual semiproduct quantity.
- **Zero actual quantity on a product**: Exclude from distribution (0 pieces = 0 grams consumption). Only distribute across products with `ActualQuantity > 0`.
- **AllowedResiduePercentage = 0**: Every non-zero difference requires user confirmation.
- **Exact match** (difference = 0): Auto-approve, no adjustment needed. BoM update still runs (confirms current weights).

## Testing Strategy

### `ResidueDistributionCalculator` unit tests:
- Residue case within threshold → auto-approve + correct proportions
- Residue case exceeds threshold → requires confirmation
- Deficit case within threshold → auto-approve + correct proportions
- Deficit case exceeds threshold → requires confirmation
- Exact match → auto-approve
- Single product → 100% allocation
- Product with 0 actual quantity → excluded
- Rounding: verify adjusted amounts sum exactly to actual semiproduct

### `ManufactureOrderApplicationService` unit tests:
- Within threshold: auto-proceeds, ERP docs created, BoM updated, status Completed
- Outside threshold, not confirmed: returns RequiresConfirmation with distribution
- Outside threshold, confirmed: proceeds normally
- ERP failure: sets ManualActionRequired
- BoM update failure: sets ManualActionRequired (ERP docs already created)

### `FlexiManufactureClient` adapter tests:
- `SubmitManufacturePerProductAsync` with adjusted semiproduct consumption amounts
- `UpdateBoMIngredientAmountAsync` — verify correct Flexi API call

### Frontend tests:
- Confirmation modal renders distribution table correctly
- Auto-proceed when within safe zone (no modal shown)
- Override flag sent on user confirmation

## Risks

1. **BoM update via FlexiBeeSDK**: `IBoMClient` may not expose `SaveAsync`. If `SaveAsync` is not available, implement via raw Flexi REST PUT to `/kusovnik/{id}`. Verify during implementation.
2. **Concurrent manufacture**: If two manufacture orders for the same product are finalized close together, BoM updates could conflict. Low risk for this codebase (solo operator, sequential workflow).
