### task: manufacture-name-builder-domain-entity

`IManufactureNameBuilder` operates on `ManufactureOrder`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ManufactureNameBuilder.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/Workflows/ManufactureNameBuilderTests.cs`

- [ ] **Step 1: Update the interface and implementation**

In `ManufactureNameBuilder.cs`, replace:
```csharp
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;
using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Manufacture.Services.Workflows;

public interface IManufactureNameBuilder
{
    string Build(UpdateManufactureOrderDto order, ErpManufactureType type);
}
```
with:
```csharp
using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Manufacture.Services.Workflows;

public interface IManufactureNameBuilder
{
    string Build(ManufactureOrder order, ErpManufactureType type);
}
```
and change the implementation's method signature the same way:
```csharp
public string Build(ManufactureOrder order, ErpManufactureType type)
```
No other line in the method body changes — `order.SemiProduct`, `order.SemiProduct.ProductCode`, `order.SemiProduct.ProductName`, `order.Products.All(p => p.ProductCode == semiCode)` all resolve identically on `ManufactureOrder`.

- [ ] **Step 2: Update the unit test file**

In `ManufactureNameBuilderTests.cs`, replace the `CreateOrder` helper (currently building `UpdateManufactureOrderDto`/`UpdateManufactureOrderSemiProductDto`/`UpdateManufactureOrderProductDto`) with:
```csharp
private static ManufactureOrder CreateOrder(
    string semiCode,
    string semiName,
    (string code, string name)[] products)
{
    return new ManufactureOrder
    {
        OrderNumber = "MO-TEST-001",
        SemiProduct = new ManufactureOrderSemiProduct
        {
            ProductCode = semiCode,
            ProductName = semiName,
            PlannedQuantity = 10m,
            ActualQuantity = 10m,
        },
        Products = products.Select(p => new ManufactureOrderProduct
        {
            ProductCode = p.code,
            ProductName = p.name,
            SemiProductCode = semiCode,
            PlannedQuantity = 10m,
            ActualQuantity = 10m,
        }).ToList(),
    };
}
```
Remove the `using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;` line from the top of the file (no longer referenced). No other line in the file changes — all five `[Fact]` test bodies call `CreateOrder(...)` and `_builder.Build(order, ...)` exactly as before; only the helper's return/construction type changed.

- [ ] **Step 3: Run the name builder's tests**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ManufactureNameBuilderTests"`
Expected: all 5 tests PASS with identical results (`Build_WhenProductCodeShorterThan6Chars_DoesNotThrow`, `Build_WhenSemiProduct_PrependsMSuffix`, `Build_WhenSinglephaseProduct_ReturnsSemiCodeOnly`, `Build_WhenResultExceeds40Chars_TruncatesSafely`, `Build_WhenCodeShorterThanPrefix_UsesFullCode`).

- [ ] **Step 4: Commit**

```bash
cd backend
git add src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ManufactureNameBuilder.cs \
        test/Anela.Heblo.Tests/Features/Manufacture/Services/Workflows/ManufactureNameBuilderTests.cs
git commit -m "refactor(manufacture): IManufactureNameBuilder takes ManufactureOrder instead of UpdateManufactureOrderDto"
```

---

