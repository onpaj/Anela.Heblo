### task: create-catalog-manufacture-record

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Catalog/ManufactureHistory/CatalogManufactureRecord.cs`

- [ ] Step 1: Create directory `backend/src/Anela.Heblo.Domain/Features/Catalog/ManufactureHistory/` (it does not exist yet — check `PurchaseHistory/` as the reference: that directory contains only `CatalogPurchaseRecord.cs`).

- [ ] Step 2: Create `CatalogManufactureRecord.cs` with the exact content below. It must be a `class` (not a `record`) — the project rule forbids records on types that cross API/client generation boundaries. Mirror the field set from `ManufactureHistoryRecord` (which has `Date`, `Amount`, `PricePerPiece`, `PriceTotal`, `ProductCode`, `DocumentNumber` — no `SupplierId`/`SupplierName`):

```csharp
namespace Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;

public class CatalogManufactureRecord
{
    public DateTime Date { get; set; }
    public double Amount { get; set; }
    public decimal PricePerPiece { get; set; }
    public decimal PriceTotal { get; set; }
    public string ProductCode { get; set; }
    public string DocumentNumber { get; set; }
}
```

- [ ] Step 3: Verify the build compiles cleanly for the Domain project only:
```
dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj -v quiet
```

- [ ] Step 4: Commit:
```
git add backend/src/Anela.Heblo.Domain/Features/Catalog/ManufactureHistory/CatalogManufactureRecord.cs
git commit -m "feat: add CatalogManufactureRecord domain type to Catalog.ManufactureHistory namespace"
```

---

