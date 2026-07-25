### task: add-module-boundary-rule-for-shoptetorders-shipmentlabels

Pin the `ShoptetOrders -> ShipmentLabels` boundary in `ModuleBoundariesTests.cs` so a future contributor cannot reintroduce a direct `IShipmentClient` (or any other `ShipmentLabels` type) reference into `ShoptetOrders`. This is the regression guard for the whole fix.

File: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`.

**Step 1 — add a new empty allowlist field.**

Insert a new `private static readonly HashSet<string>` field immediately after the existing `PackagingShoptetOrdersAllowlist` field (which currently ends at line 339 with `};`) and before the `public static TheoryData<ModuleBoundaryRule> Rules() => new()` line (line 341). Find this exact text:

```csharp
        // PackingStatsTile is a dashboard tile that mirrors GetPackingDashboardHandler's logic;
        // it consumes only IPackingOrderClient (returns int?) — no ShoptetOrders DTOs cross the boundary.
        "Anela.Heblo.Application.Features.Packaging.DashboardTiles.PackingStatsTile -> Anela.Heblo.Application.Features.ShoptetOrders.IPackingOrderClient",
    };

    public static TheoryData<ModuleBoundaryRule> Rules() => new()
```

Replace it with:

```csharp
        // PackingStatsTile is a dashboard tile that mirrors GetPackingDashboardHandler's logic;
        // it consumes only IPackingOrderClient (returns int?) — no ShoptetOrders DTOs cross the boundary.
        "Anela.Heblo.Application.Features.Packaging.DashboardTiles.PackingStatsTile -> Anela.Heblo.Application.Features.ShoptetOrders.IPackingOrderClient",
    };

    // Allowlist for ShoptetOrders -> ShipmentLabels. Empty — CompleteDeliveredOrdersJob now consumes
    // the ShoptetOrders-owned IShipmentDeliveryChecker contract; the ShipmentLabels adapter
    // (ShipmentLabelsShipmentDeliveryCheckerAdapter) lives in ShipmentLabels.Infrastructure and
    // implements it there, so no ShoptetOrders type needs to reference ShipmentLabels directly.
    private static readonly HashSet<string> ShoptetOrdersShipmentLabelsAllowlist = new(StringComparer.Ordinal);

    public static TheoryData<ModuleBoundaryRule> Rules() => new()
```

**Step 2 — add the rule entry.**

Insert a new `ModuleBoundaryRule` as the last entry in the `Rules()` `TheoryData`. Find this exact text (the current last entry, ending the `TheoryData` initializer):

```csharp
        new ModuleBoundaryRule(
            Name: "FinancialOverview -> Catalog",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.FinancialOverview",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Catalog",
                "Anela.Heblo.Application.Features.Catalog",
                "Anela.Heblo.Persistence.Catalog",
            },
            Allowlist: new HashSet<string>(StringComparer.Ordinal)),
    };
```

Replace it with:

```csharp
        new ModuleBoundaryRule(
            Name: "FinancialOverview -> Catalog",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.FinancialOverview",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Catalog",
                "Anela.Heblo.Application.Features.Catalog",
                "Anela.Heblo.Persistence.Catalog",
            },
            Allowlist: new HashSet<string>(StringComparer.Ordinal)),

        new ModuleBoundaryRule(
            Name: "ShoptetOrders -> ShipmentLabels",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.ShoptetOrders",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Application.Features.ShipmentLabels",
            },
            Allowlist: ShoptetOrdersShipmentLabelsAllowlist),
    };
```

**Step 3 — run the architecture test suite and confirm the new rule passes with zero violations.**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests"
```

This must pass for all rules in `Rules()`, including the new `"ShoptetOrders -> ShipmentLabels"` entry — confirming the three prior tasks fully removed `ShoptetOrders`' compile-time dependency on `ShipmentLabels`.

If it fails, check for a leftover reference: search for any remaining `Anela.Heblo.Application.Features.ShipmentLabels` usage under `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/` — everything under that path (including `IShipmentDeliveryChecker.cs`) must be free of it, since the adapter and its `IShipmentClient` dependency live in `ShipmentLabels.Infrastructure`, not `ShoptetOrders`.

**Step 4 — run the full backend test suite and build one last time.**

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
dotnet format Anela.Heblo.sln --verify-no-changes
```

**Step 5 — commit.**

```bash
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "Add ModuleBoundariesTests rule pinning ShoptetOrders -> ShipmentLabels"
```
