---
process: calc-batch-calculation
kind: calculation
module: manufacture
summary: Scales a product's Flexi recipe (BoM) to a chosen batch size or to the amount of one ingredient on hand, and lists every ingredient's scaled amount next to its current stock and last stock taking.
owns:
  - backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CalculatedBatchSize/**
  - backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CalculateBatchByIngredient/**
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Validators/CalculateBatchBySizeRequestValidator.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Validators/CalculateBatchByIngredientRequestValidator.cs
  - backend/src/Anela.Heblo.API/Controllers/ManufactureBatchController.cs
  - backend/src/Anela.Heblo.API/MCP/Tools/ManufactureBatchMcpTools.cs
verified_at: "5e993f9e2"
related:
  - calc-batch-planning
  - flow-manufacture-order
  - feed-manufacture-to-flexi
---

# Batch calculator

## Purpose
Answers "how much of each ingredient do I weigh for a batch of X grams?" and "I have only Y
grams of this oil — how big a batch can I make, and how much of everything else?". Production
uses it on page `/manufacturing/batch-calculator` (Kalkulačka dávek) before mixing a bulk
semi-product (meziprodukt); from the result it can create a manufacture order
(`flow-manufacture-order`). MCP tools `GetBatchTemplate`, `CalculateBatchBySize`,
`CalculateBatchByIngredient` expose the same numbers to Claude.

## Trigger
On demand, permission `Manufacture_BatchPlanning` (read for the template, write for the calculations):
- `GET /api/manufacture-batch/template/{productCode}` — recipe at its minimum manufacture quantity (MMQ).
- `POST /api/manufacture-batch/calculate-by-size` `{productCode, desiredBatchSize?}`.
- `POST /api/manufacture-batch/calculate-by-ingredient` `{productCode, ingredientCode, desiredIngredientAmount}`.

## Data flow
1. Recipe: Flexi BoM (kusovník) of the product via `IManufactureClient.GetManufactureTemplateAsync`
   — header line (level 1) = reference batch amount, other lines = ingredients with amount,
   Flexi product type, display order (`poradi`) and phase label (`nazevC`, one letter A–Z).
   Cached in memory 5 min per product code (`manufacture-template:{code}`).
2. Catalog (in-memory catalog cache): product MMQ (`MinimalManufactureQuantity`, Flexi product
   attribute) and, per ingredient, `Stock.Total` and the latest `StockTakingHistory` date.
3. Scale → response (no persistence).

## Logic & formulas
- **By size**: target = `desiredBatchSize`, or the product's MMQ when omitted (the template
  endpoint). `scaleFactor = target ÷ BoM header amount`. Each ingredient
  `CalculatedAmount = round(BoM amount × scaleFactor, 2)`. Ingredients sorted by Flexi order
  (0 = unordered goes last), then name; the phase label is passed through.
- **By ingredient**: `scaleFactor = desiredIngredientAmount ÷ that ingredient's BoM amount`;
  `NewBatchSize = round(BoM header amount × scaleFactor, 2)`; every ingredient scaled as above.
  Ingredients keep BoM order (no sorting) and carry no phase label.
- Units are the BoM's units — grams for bulk recipes. Prices in the response are the BoM
  ingredient `Price` (not populated by the Flexi template loader, so 0).
- `StockTotal` = catalog `Stock.Total` (warehouse + transport + sklad výroby + reserve), i.e.
  not only what is physically in the material warehouse.
- Errors: no BoM → `ManufactureTemplateNotFound`; product not in catalog → `ProductNotFound`;
  header amount ≤ 0 → `InvalidBatchSize`; ingredient not in BoM → `IngredientNotFoundInTemplate`.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| Template cache TTL | 5 min (constant) | How long a BoM change in Flexi takes to show up |
| Flexi product attribute MMQ | per product | Default batch size |

## Runtime facts
None.

## Known quirks
- **The request validators are dead code.** `CalculateBatchBySizeRequestValidator` /
  `CalculateBatchByIngredientRequestValidator` (e.g. amount > 0, ≤ 999,999.99) are never
  registered in DI, so a 0 or negative amount is not rejected — by-ingredient with 0 returns an
  all-zero recipe.
- **BoM amounts change after every multi-phase completion**: the semi-product grams per piece in
  product BoMs are rewritten from the actual yield (`feed-manufacture-to-flexi`). Semi-product
  recipes themselves are not rewritten.
- By-ingredient loads catalog entries one by one (N calls to the cache) while by-size batches them; no functional difference.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CalculatedBatchSize/CalculatedBatchSizeHandler.cs` — by size / template
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CalculateBatchByIngredient/CalculateBatchByIngredientHandler.cs` — by ingredient
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureTemplateService.cs` — how the BoM is read
- `backend/src/Anela.Heblo.API/MCP/Tools/ManufactureBatchMcpTools.cs` — MCP wrappers
