## Module
OrgChart

## Finding
`OrgChartOptions.DataSourceUrl` defaults to `string.Empty`, and the module registration in `OrgChartModule.AddOrgChartServices` binds the section but adds no startup validation:

```csharp
// backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartOptions.cs
public string DataSourceUrl { get; set; } = string.Empty;

// backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartModule.cs:19
services.Configure<OrgChartOptions>(configuration.GetSection(OrgChartOptions.SectionName));
```

If the `OrgChart:DataSourceUrl` configuration key is absent (e.g., the Key Vault secret was not set, or it was renamed), the application starts without error. The misconfiguration only surfaces at the first HTTP request, deep inside `OrgChartService.GetOrganizationStructureAsync`, as an `HttpRequestException` or `UriFormatException` with a message that gives no hint that the root cause is a missing config value.

## Why it matters
Misconfiguration of an external data-source URL should be a deployment-time failure, not a user-facing runtime error. Without startup validation, a missing secret can silently pass CI and only fail in production when a user opens the OrgChart page.

## Suggested fix
1. Add `[Required]` to `DataSourceUrl`:
   ```csharp
   using System.ComponentModel.DataAnnotations;
   
   public class OrgChartOptions
   {
       public const string SectionName = "OrgChart";
   
       [Required]
       public string DataSourceUrl { get; set; } = string.Empty;
   }
   ```

2. Chain options validation in `OrgChartModule`:
   ```csharp
   services
       .AddOptions<OrgChartOptions>()
       .Bind(configuration.GetSection(OrgChartOptions.SectionName))
       .ValidateDataAnnotations()
       .ValidateOnStart();
   ```

This causes the host to throw at startup if `DataSourceUrl` is empty, making misconfiguration immediately obvious.

---
_Filed by daily arch-review routine on 2026-06-04._