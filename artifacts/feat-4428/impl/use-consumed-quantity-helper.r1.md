# Impl: use-consumed-quantity-helper r1
- GiftPackageManufactureService.DisassembleGiftPackageAsync: replaced inline `(int)(ingredient.RequiredQuantity * quantity)` with `ConsumedQuantity(ingredient, quantity)` (identical formula, no behaviour change).
- dotnet build (Application): 0 errors. dotnet format on the file: no changes.
- GiftPackageManufacture tests: 41 passed; 5 failed only because Docker/Testcontainers is unavailable in this sandbox (AtomicityIntegrationTests), unrelated to the change.
- docs/processes/flow-gift-package-manufacture.md: verified_at bumped; INDEX unchanged.
