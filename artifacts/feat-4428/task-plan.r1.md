# Task plan

### task: use-consumed-quantity-helper
In `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs`, replace the inline formula in `DisassembleGiftPackageAsync` with `ConsumedQuantity(ingredient, quantity)`.

Steps:
1. Edit the single line (~326).
2. `dotnet build` and `dotnet format` for the backend.
3. Run existing GiftPackageManufacture tests (filter by `GiftPackageManufacture`); they must pass. No new tests needed (no behaviour change).
4. Check whether a docs/processes doc owns this file; if CI process-docs check requires it, bump `verified_at`.
