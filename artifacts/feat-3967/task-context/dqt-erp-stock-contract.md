### task: dqt-erp-stock-contract

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/DqtErpStockItem.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IDqtErpStockSource.cs`

- [ ] **Step 1: Write the DataQuality-owned ERP snapshot DTO**

```csharp
namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public class DqtErpStockItem
{
    public string ProductCode { get; set; }
    public string ProductName { get; set; }
    public bool IsSellable { get; set; }
}
```

Note: `IsSellable` replaces the raw `ProductTypeId` — the `ProductType` enum comparison is Catalog domain knowledge and lives only in `DataQualityErpStockSourceAdapter` (see task `catalog-erp-stock-source-adapter`).

- [ ] **Step 2: Write the DataQuality-owned ERP contract**

```csharp
namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public interface IDqtErpStockSource
{
    Task<IReadOnlyList<DqtErpStockItem>> ListAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/DqtErpStockItem.cs backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IDqtErpStockSource.cs
git commit -m "Add DataQuality-owned IDqtErpStockSource contract and snapshot DTO"
```

---

