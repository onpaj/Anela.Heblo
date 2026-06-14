## Module
Manufacture

## Finding
`ManufactureStockTakingController.GetManufactureStockTakingHistory` dispatches `GetStockTakingHistoryRequest` from the **Catalog** module, not the Manufacture module:

```
backend/src/Anela.Heblo.API/Controllers/ManufactureStockTakingController.cs
  line 1:  using Anela.Heblo.Application.Features.Catalog.UseCases.GetStockTakingHistory;
  line 47: var response = await _mediator.Send(request!, cancellationToken);   // request is GetStockTakingHistoryRequest
```

The endpoint lives at `GET /api/manufacture-stock-taking/history`, but the handler that fulfils it is owned by the Catalog module (`Anela.Heblo.Application.Features.Catalog.UseCases.GetStockTakingHistory`). The `POST /submit` action on the same controller correctly uses a Manufacture-owned handler (`SubmitManufactureStockTakingRequest`).

## Why it matters
This creates a hidden compile-time dependency from the Manufacture API surface on Catalog's internal use-case namespace. If `GetStockTakingHistoryRequest` is renamed, repackaged, or its signature changed in the Catalog module, the Manufacture controller breaks — without any indication that there is a cross-module coupling.

The architecture targets future microservice extractability (development_guidelines.md: "Each module must be deployable as a separate microservice"). In that model, the Manufacture service would need a runtime call to the Catalog service just to serve its own stock-taking history endpoint, which is architecturally backward.

Concretely: a reader of `ManufactureStockTakingController` reasonably expects all dispatched commands to belong to the Manufacture slice. The silent dependency on Catalog violates that expectation and adds surprise to any future refactor of the stock-taking history query.

## Suggested fix
Two options — pick the one that matches domain ownership:

**Option A (preferred if the data belongs to Catalog):** Move the `GET history` endpoint into `CatalogController` (or a new `StockTakingController`) and remove the Catalog import from `ManufactureStockTakingController`. The Manufacture controller then only contains Manufacture-owned handlers.

**Option B (if Manufacture needs its own history view):** Add a dedicated `GetManufactureStockTakingHistoryHandler` inside the Manufacture module (`Application/Features/Manufacture/UseCases/GetManufactureStockTakingHistory/`). It can call the same repository or consume a cross-module contract, but it is owned by Manufacture and the Catalog namespace is no longer referenced from the Manufacture controller.

---
_Filed by daily arch-review routine on 2026-06-06._