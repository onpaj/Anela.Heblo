### task: dqt-eshop-stock-contract

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/DqtEshopStockItem.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IDqtEshopStockSource.cs`

- [ ] **Step 1: Write the DataQuality-owned eshop snapshot DTO**

```csharp
namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public class DqtEshopStockItem
{
    public string Code { get; set; }
    public string PairCode { get; set; }
    public string Name { get; set; }
}
```

- [ ] **Step 2: Write the DataQuality-owned eshop contract**

```csharp
namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public interface IDqtEshopStockSource
{
    Task<IReadOnlyList<DqtEshopStockItem>> ListAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: `Build succeeded.` — these two files add no forbidden-namespace references (no `using Anela.Heblo.Domain.Features.Catalog*`).

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/DqtEshopStockItem.cs backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IDqtEshopStockSource.cs
git commit -m "Add DataQuality-owned IDqtEshopStockSource contract and snapshot DTO"
```

---

