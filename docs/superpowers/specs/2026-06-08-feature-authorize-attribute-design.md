# Collapse `[GateOn]` + `[Authorize]` into `[FeatureAuthorize]`

**Status:** Design approved 2026-06-08
**Owner:** ondra@anela.cz
**Context:** Follow-on to `2026-06-08-permission-source-of-truth-design.md`

## Problem

The permission-source-of-truth design requires every controller to carry two attributes:

```csharp
[GateOn(Feature.Customer_BankStatements)]
[Authorize(Roles = AccessRoles.CustomerBankStatementsRead)]
public class BankStatementsController : BaseApiController { ... }
```

`[GateOn]` is pure metadata — it has no runtime effect. It exists only so `GateConsistencyTests.EveryAuthorizeRole_MatchesGateOn` can verify that the hand-typed `AccessRoles` constant matches the declared feature. The test is a compensating control for a structural weakness: the developer types the role string independently of the feature enum, and the two can silently diverge.

The need for the compensating control disappears if the attribute derives the role string from the feature enum at construction time.

## Goal

One attribute — `[FeatureAuthorize(Feature.X, AccessLevel.Y)]` — replaces both. It:

- inherits `AuthorizeAttribute` so ASP.NET Core's pipeline enforces it at runtime
- sets `Roles` automatically from the `(Feature, AccessLevel)` pair
- carries `Feature` and `Level` as typed properties for test introspection

Drift between the declared feature and the enforced role is structurally impossible after this change.

## Non-goals

- Changing runtime authorization behavior. The same roles are enforced; only how they are declared changes.
- Changing `AccessRoles` constants, permission string conventions, or the `AccessMatrix`. Those are defined by the parent spec.
- Changing `PermissionClaimsTransformation`, the `/me` endpoint, or Entra role provisioning.

## Design

### `FeatureAuthorizeAttribute`

```csharp
// backend/src/Anela.Heblo.Domain/Features/Authorization/FeatureAuthorizeAttribute.cs
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class FeatureAuthorizeAttribute : AuthorizeAttribute
{
    public Feature Feature { get; }
    public AccessLevel Level { get; }

    public FeatureAuthorizeAttribute(Feature feature, AccessLevel level = AccessLevel.Read)
    {
        Feature = feature;
        Level = level;
        Roles = AccessRoles.For(feature, level);
    }
}
```

`AllowMultiple = true` preserves current ASP.NET Core AND semantics when a method carries a different permission than its class (see §Method-level overrides below).

### `AccessRoles.For` lookup

The codegen tool emits a `For(Feature, AccessLevel)` static method into `AccessRoles.generated.cs` alongside the existing constants:

```csharp
public static string For(Feature feature, AccessLevel level) => (feature, level) switch
{
    (Feature.Finance_FinancialOverview, AccessLevel.Read) => FinanceFinancialOverviewRead,
    (Feature.Products_Catalog,          AccessLevel.Read) => ProductsCatalogRead,
    (Feature.Products_Catalog,          AccessLevel.Write) => ProductsCatalogWrite,
    // … one arm per permitted (Feature, Level) combination
    _ => throw new ArgumentOutOfRangeException(
             $"Feature.{feature} does not support AccessLevel.{level}")
};
```

The generator only emits arms for `(Feature, Level)` pairs permitted by the feature's `HasWrite`/`HasAdmin` flags — identical logic to how constants are emitted today. Requesting an unsupported level (e.g. `AccessLevel.Write` for a read-only feature) throws at the moment the attribute is first reflected, which is early in app startup.

### Usage on controllers

Before:
```csharp
[GateOn(Feature.Customer_BankStatements)]
[Authorize(Roles = AccessRoles.CustomerBankStatementsRead)]
public class BankStatementsController : BaseApiController { ... }
```

After:
```csharp
[FeatureAuthorize(Feature.Customer_BankStatements)]
public class BankStatementsController : BaseApiController { ... }
```

`AccessLevel.Read` is the default, so `.Read` endpoints omit the second argument.

### Method-level overrides

When a method requires a different feature than its class, it carries its own `[FeatureAuthorize]`. ASP.NET Core ANDs the class and method attributes — both must pass. This is identical to the current behavior where `[Authorize]` at both levels is ANDed.

Before:
```csharp
[GateOn(Feature.Marketing_Article)]
[Authorize(Roles = AccessRoles.MarketingArticleWrite)]
public class ArticlesController : BaseApiController
{
    [GateOn(Feature.Admin_Administration)]
    [Authorize(Roles = AccessRoles.AdminAdministrationWrite)]
    public IActionResult BackfillRequestedBy() { ... }
}
```

After:
```csharp
[FeatureAuthorize(Feature.Marketing_Article, AccessLevel.Write)]
public class ArticlesController : BaseApiController
{
    [FeatureAuthorize(Feature.Admin_Administration, AccessLevel.Write)]
    public IActionResult BackfillRequestedBy() { ... }
}
```

### Tests

**`GateConsistencyTests`** — rewritten:

| Test | Before | After |
|---|---|---|
| `EveryAuthorizeRole_MatchesGateOn` | Parses role string, checks it matches `[GateOn]` | **Deleted.** Structurally impossible to drift; no compensating test needed. |
| `EveryGatedEndpoint_HasFeatureAuthorize` (renamed from `EveryGatedEndpoint_HasGateOn`) | Checks every `[Authorize]`-carrying member also has `[GateOn]` | Checks every non-`[AllowAnonymous]` public action has a class- or method-level `[FeatureAuthorize]`. Same logic, different attribute type. |

The `EveryMenuPath_FeatureHasController` and `EveryMenuPath_PermissionsResolveToKnownRoles` tests from the parent spec are unaffected; they read `AccessMatrix` and controller attributes independently.

**`GateOnAttributeTests`** — replaced with `FeatureAuthorizeAttributeTests`:

```csharp
public class FeatureAuthorizeAttributeTests
{
    [FeatureAuthorize(Feature.Manufacture_BatchPlanning)]
    private class SampleReadController { }

    [FeatureAuthorize(Feature.Products_Catalog, AccessLevel.Write)]
    private class SampleWriteController { }

    [Fact]
    public void FeatureAuthorize_SetsRolesFromFeatureAndLevel()
    {
        var attr = typeof(SampleReadController).GetCustomAttribute<FeatureAuthorizeAttribute>()!;
        attr.Feature.Should().Be(Feature.Manufacture_BatchPlanning);
        attr.Level.Should().Be(AccessLevel.Read);
        attr.Roles.Should().Be(AccessRoles.ManufactureBatchPlanningRead);
    }

    [Fact]
    public void FeatureAuthorize_SetsWriteRole_WhenLevelIsWrite()
    {
        var attr = typeof(SampleWriteController).GetCustomAttribute<FeatureAuthorizeAttribute>()!;
        attr.Roles.Should().Be(AccessRoles.ProductsCatalogWrite);
    }
}
```

## Files changed

| File | Change |
|---|---|
| `Domain/Features/Authorization/FeatureAuthorizeAttribute.cs` | New |
| `Domain/Features/Authorization/GateOnAttribute.cs` | Deleted |
| `Domain/Features/Authorization/AccessRoles.generated.cs` | Add `For(Feature, AccessLevel)` switch |
| `tools/Anela.Heblo.AccessMatrixGen` | Emit `For` method alongside constants |
| All ~40 controllers in `API/Controllers/` | Replace `[GateOn] + [Authorize]` with `[FeatureAuthorize]` |
| `Tests/Authorization/GateConsistencyTests.cs` | Delete `EveryAuthorizeRole_MatchesGateOn`; rename and rewrite `EveryGatedEndpoint_HasGateOn` |
| `Tests/Authorization/GateOnAttributeTests.cs` | Replace with `FeatureAuthorizeAttributeTests.cs` |

## Migration order

1. Add `FeatureAuthorize_For` to codegen; regenerate `AccessRoles.generated.cs` with the new `For` method. Tests still pass — no controllers changed yet.
2. Add `FeatureAuthorizeAttribute.cs`.
3. Sweep all controllers: replace `[GateOn(Feature.X)] + [Authorize(Roles = AccessRoles.XY)]` with `[FeatureAuthorize(Feature.X, AccessLevel.Y)]`. Mechanical — one PR.
4. Update `GateConsistencyTests`: delete `EveryAuthorizeRole_MatchesGateOn`, rewrite `EveryGatedEndpoint_HasGateOn`.
5. Replace `GateOnAttributeTests.cs` with `FeatureAuthorizeAttributeTests.cs`.
6. Delete `GateOnAttribute.cs`. Build confirms no remaining references.

Steps 1–2 are additive; step 3 is the only broad change but is fully mechanical and validated by the updated tests in step 4.
