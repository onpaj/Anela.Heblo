# Spec: reuse ConsumedQuantity in DisassembleGiftPackageAsync (#4428)

## Requirement
In `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs`, `DisassembleGiftPackageAsync` (line ~326) must compute the per-ingredient quantity via the private helper `ConsumedQuantity(ingredient, quantity)` instead of the inline `(int)(ingredient.RequiredQuantity * quantity)`.

## Acceptance criteria
- No inline duplicate of the formula remains outside `ConsumedQuantity`.
- Behaviour unchanged: identical results for all inputs.
- Build and existing GiftPackageManufacture tests pass.

## Out of scope
Rounding semantic changes; renaming the helper.
