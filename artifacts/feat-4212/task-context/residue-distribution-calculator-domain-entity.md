### task: residue-distribution-calculator-domain-entity

`IResidueDistributionCalculator` operates on `ManufactureOrder`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/IResidueDistributionCalculator.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ResidueDistributionCalculator.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/ResidueDistributionCalculatorTests.cs`

- [ ] **Step 1: Update the interface signature**

In `IResidueDistributionCalculator.cs`, change:
```csharp
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;

public interface IResidueDistributionCalculator
{
    Task<ResidueDistribution> CalculateAsync(UpdateManufactureOrderDto order, CancellationToken cancellationToken = default);
}
```
to:
```csharp
public interface IResidueDistributionCalculator
{
    Task<ResidueDistribution> CalculateAsync(ManufactureOrder order, CancellationToken cancellationToken = default);
}
```
Remove the now-unused `using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;` line (the file already has `using Anela.Heblo.Domain.Features.Manufacture;` for `ManufactureOrder`, or add it if missing).

- [ ] **Step 2: Update the implementation**

In `ResidueDistributionCalculator.cs`, change every occurrence of the parameter type from `UpdateManufactureOrderDto` to `ManufactureOrder` on both methods that take `order`:
```csharp
public async Task<ResidueDistribution> CalculateAsync(ManufactureOrder order, CancellationToken cancellationToken = default)
```
and
```csharp
private async Task<List<ProductCalculationData>> BuildProductDataAsync(
    ManufactureOrder order,
    CancellationToken cancellationToken)
```
No other line in the method bodies changes — `order.ManufactureType`, `order.SemiProduct`, `order.SemiProduct.ProductCode`, `order.SemiProduct.ActualQuantity`, `order.SemiProduct.PlannedQuantity`, `order.Products`, `product.ProductCode`, `product.ActualQuantity`, `product.PlannedQuantity`, `product.ProductName` all exist with identical names/nullability on `ManufactureOrder`/`ManufactureOrderSemiProduct`/`ManufactureOrderProduct`. Remove the now-unused `using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;` line from the top of the file.

- [ ] **Step 3: Build to confirm the compile error surface is limited to callers**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: build fails only on the two workflow files (they still pass `UpdateManufactureOrderDto` at their `CalculateAsync` call sites) — no unexpected errors inside `ResidueDistributionCalculator.cs` itself. This is expected at this point in the plan; Task 3 fixes the workflow call site.

- [ ] **Step 4: Update the unit test file's order fixtures**

`ResidueDistributionCalculatorTests.cs` builds `UpdateManufactureOrderDto`/`UpdateManufactureOrderSemiProductDto`/`UpdateManufactureOrderProductDto` inline at every test's Arrange section (lines ~220, ~261, ~331, ~430) and inside the two private builder methods `BuildOrder` (~line 451) and `BuildOrderWithZeroProduct` (~line 479). Apply this exact rename throughout the file, in every one of those locations:
- `UpdateManufactureOrderDto` → `ManufactureOrder`
- `UpdateManufactureOrderSemiProductDto` → `ManufactureOrderSemiProduct`
- `UpdateManufactureOrderProductDto` → `ManufactureOrderProduct`

Concrete worked example — `BuildOrder` becomes:
```csharp
private ManufactureOrder BuildOrder(
    string semiCode, decimal semiActual, decimal semiPlanned,
    List<(string code, decimal? actual, decimal planned)> products,
    ManufactureType manufactureType = ManufactureType.MultiPhase)
{
    var productEntities = products.Select(p => new ManufactureOrderProduct
    {
        ProductCode = p.code,
        ProductName = p.code,
        ActualQuantity = p.actual,
        PlannedQuantity = p.planned,
    }).ToList();

    return new ManufactureOrder
    {
        ManufactureType = manufactureType,
        SemiProduct = new ManufactureOrderSemiProduct
        {
            ProductCode = semiCode,
            ProductName = semiCode,
            ActualQuantity = semiActual,
            PlannedQuantity = semiPlanned,
        },
        Products = productEntities,
    };
}
```
(Match `BuildOrder`'s actual current parameter list and body exactly as it exists in the file today — only the three type names change; do not alter any parameter names, default values, or logic.) Apply the identical three-way rename to `BuildOrderWithZeroProduct` and to every inline `new UpdateManufactureOrderDto { ... }` / `new UpdateManufactureOrderSemiProductDto { ... }` / `new UpdateManufactureOrderProductDto { ... }` block in the test methods at the four line ranges listed above. Remove the `using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;` line from the top of the test file once no reference to the `UpdateManufactureOrder` namespace remains in it.

- [ ] **Step 5: Run the calculator's tests**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ResidueDistributionCalculatorTests"`
Expected: all tests PASS with identical assertions to before — this is a type substitution, not a logic change, so every existing expected value (`ActualSemiProductQuantity`, `Difference`, `DifferencePercentage`, `IsWithinAllowedThreshold`, per-product `AdjustedGramsPerUnit`, etc.) must still match.

- [ ] **Step 6: Commit**

```bash
cd backend
git add src/Anela.Heblo.Application/Features/Manufacture/Services/IResidueDistributionCalculator.cs \
        src/Anela.Heblo.Application/Features/Manufacture/Services/ResidueDistributionCalculator.cs \
        test/Anela.Heblo.Tests/Features/Manufacture/Services/ResidueDistributionCalculatorTests.cs
git commit -m "refactor(manufacture): IResidueDistributionCalculator takes ManufactureOrder instead of UpdateManufactureOrderDto"
```

---

