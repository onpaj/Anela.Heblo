# Implementation Plan: `[FeatureAuthorize]` attribute

**Spec:** `docs/superpowers/specs/2026-06-08-feature-authorize-attribute-design.md`
**Branch:** cut from `main`

## Step 1 — Update codegen to emit `AccessRoles.For`

**File:** `backend/tools/Anela.Heblo.AccessMatrixGen/Program.cs`

After the loop that emits the constants in section `// 3. C# constants`, append a switch method:

```csharp
cs.AppendLine();
cs.AppendLine("    public static string For(Feature feature, AccessLevel level) => (feature, level) switch");
cs.AppendLine("    {");
foreach (var r in roles)
{
    if (!Enum.TryParse<Feature>(r.Feature, out var feature)) continue;
    var levelName = r.Level switch
    {
        AccessLevel.Read  => "Read",
        AccessLevel.Write => "Write",
        AccessLevel.Admin => "Admin",
        _ => throw new InvalidOperationException()
    };
    var suffix = $"{PermissionString.ConstantSuffix(feature)}{levelName}";
    cs.AppendLine($"        (Feature.{r.Feature}, AccessLevel.{levelName}) => {suffix},");
}
cs.AppendLine("        _ => throw new ArgumentOutOfRangeException($\"Feature.{{feature}} does not support AccessLevel.{{level}}\")");
cs.AppendLine("    };");
```

Re-run the generator (the build target does this automatically on `dotnet build`). Verify `AccessRoles.generated.cs` now has `For(Feature, AccessLevel)`.

**Check:** `dotnet build` in `backend/` passes. Existing tests pass.

---

## Step 2 — Create `FeatureAuthorizeAttribute.cs`

**New file:** `backend/src/Anela.Heblo.Domain/Features/Authorization/FeatureAuthorizeAttribute.cs`

```csharp
namespace Anela.Heblo.Domain.Features.Authorization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class FeatureAuthorizeAttribute : Microsoft.AspNetCore.Authorization.AuthorizeAttribute
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

**Check:** `dotnet build` passes. No controllers changed yet.

---

## Step 3 — Sweep controllers (the bulk mechanical step)

Work through every file in `backend/src/Anela.Heblo.API/Controllers/`. Three sub-cases:

### 3a. Standard pattern (~40 controllers)

Replace the pair at class level:
```csharp
// before
[GateOn(Feature.X)]
[Authorize(Roles = AccessRoles.XRead)]

// after
[FeatureAuthorize(Feature.X)]
```

Replace each method-level `[Authorize(Roles = AccessRoles.XWrite)]` that matches the class feature:
```csharp
// before
[Authorize(Roles = AccessRoles.XWrite)]

// after
[FeatureAuthorize(Feature.X, AccessLevel.Write)]
```

For method-level overrides where the feature differs from the class (e.g. `SmartsuppController` method, `ArticlesController.BackfillRequestedBy`):
```csharp
// before
[GateOn(Feature.Admin_Administration)]
[Authorize(Roles = AccessRoles.AdminAdministrationWrite)]

// after
[FeatureAuthorize(Feature.Admin_Administration, AccessLevel.Write)]
```

Remove the now-redundant `using Microsoft.AspNetCore.Authorization;` from any file where it's no longer used (keep it where `[AllowAnonymous]` is still referenced).

### 3b. Bare `[Authorize]` + `[GateOn]` (6 controllers)

`DashboardController`, `FileStorageController`, `GridLayoutsController`, `UserManagementController`, `WeatherForecastController`, `ShoptetOrdersController`:

```csharp
// before
[Authorize]
[GateOn(Feature.X)]

// after
[FeatureAuthorize(Feature.X)]
```

This adds the role enforcement that was previously missing.

### 3c. Special cases — keep existing `[Authorize]`, drop `[GateOn]`

**`AuthController`** (method-level on `GetLastLogin`):
```csharp
// before
[GateOn(Feature.Admin_Administration)]
[Authorize(Policy = "AuthenticatedUser")]

// after
[Authorize(Policy = "AuthenticatedUser")]   // keep as-is
```

**`E2ETestController`** (two methods):
```csharp
// before
[GateOn(Feature.Admin_Administration)]
[Authorize(AuthenticationSchemes = "E2ETestCookies")]

// after
[Authorize(AuthenticationSchemes = "E2ETestCookies")]   // keep as-is
```

**`FeatureFlagsController`**:
```csharp
// before (class)
[GateOn(Feature.Admin_FeatureFlags)]
[Authorize]

// after (class)
// nothing — the class-level auth is removed entirely

// before (write methods)
[Authorize(Roles = AccessRoles.AdminFeatureFlagsWrite)]

// after (write methods)
[FeatureAuthorize(Feature.Admin_FeatureFlags, AccessLevel.Write)]
```

**Check after 3a–3c:** `dotnet build` passes. The existing `GateConsistencyTests` will start failing (`EveryAuthorizeRole_MatchesGateOn` may report issues) — that's expected and resolved in step 4.

---

## Step 4 — Update tests

### 4a. Rewrite `GateConsistencyTests.cs`

Delete `EveryAuthorizeRole_MatchesGateOn` entirely.

Rewrite `EveryGatedEndpoint_HasGateOn` as `EveryGatedEndpoint_HasFeatureAuthorize`:

```csharp
[Fact]
public void EveryGatedEndpoint_HasFeatureAuthorize()
{
    var problems = new List<string>();
    foreach (var ctl in AllControllers())
    {
        var classHasFeatureAuth = ctl.GetCustomAttribute<FeatureAuthorizeAttribute>() is not null;

        foreach (var method in ctl.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (method.GetCustomAttribute<AllowAnonymousAttribute>() is not null) continue;

            var authorizeAttrs = method.GetCustomAttributes<AuthorizeAttribute>().ToList();
            if (authorizeAttrs.Count == 0 && !classHasFeatureAuth) continue; // not gated at all

            // Exempt: policy-based or scheme-based auth (not role-based)
            bool IsNonRoleAuth(AuthorizeAttribute a) =>
                !string.IsNullOrEmpty(a.Policy) || !string.IsNullOrEmpty(a.AuthenticationSchemes);

            var hasFeatureAuth = method.GetCustomAttribute<FeatureAuthorizeAttribute>() is not null
                                 || classHasFeatureAuth;
            var allNonRole = authorizeAttrs.Any() && authorizeAttrs.All(IsNonRoleAuth);

            if (!hasFeatureAuth && !allNonRole)
                problems.Add($"{ctl.Name}.{method.Name}: role-gated endpoint without [FeatureAuthorize]");
        }
    }
    problems.Should().BeEmpty();
}
```

### 4b. Replace `GateOnAttributeTests.cs` with `FeatureAuthorizeAttributeTests.cs`

Delete `GateOnAttributeTests.cs`. Create `FeatureAuthorizeAttributeTests.cs`:

```csharp
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

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

**Check:** `dotnet test` passes for the Authorization test class.

---

## Step 5 — Delete `GateOnAttribute.cs`

Delete `backend/src/Anela.Heblo.Domain/Features/Authorization/GateOnAttribute.cs`.

**Check:** `dotnet build` fails if any reference remains → fix it. Should be zero remaining references after step 3.

---

## Step 6 — Final validation

```bash
cd backend
dotnet build
dotnet test --filter "FullyQualifiedName~Authorization"
dotnet format --verify-no-changes
```

All three must be clean before the branch is ready for review.

---

## Risk notes

- **Step 3b adds real authorization**: the 6 controllers gaining a role check from bare `[Authorize]` to `[FeatureAuthorize]` will now return 403 for users missing the feature role. Verify these endpoints are not called by unauthenticated paths or by roles that don't have those permissions assigned.
- **`FeatureFlagsController` GET is `[AllowAnonymous]`**: removing the class-level `[Authorize]` safety net is safe because the public GET is already `[AllowAnonymous]` and the write methods now have explicit `[FeatureAuthorize]`.
