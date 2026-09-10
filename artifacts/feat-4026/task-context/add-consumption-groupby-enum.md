### task: add-consumption-groupby-enum

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/ConsumptionGroupBy.cs`

- [ ] **Step 1: Create the enum file**

```csharp
namespace Anela.Heblo.Application.Features.PackingMaterials.Contracts;

public enum ConsumptionGroupBy
{
    Material,
    Product,
    Order
}
```

- [ ] **Step 2: Build to confirm the new file compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application`
Expected: `Build succeeded.` — the enum has no dependencies and nothing references it yet,
so this step only confirms the file itself is syntactically valid and in the right
namespace/assembly.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/ConsumptionGroupBy.cs
git commit -m "feat(packing-materials): add ConsumptionGroupBy enum"
```
