## Module
OrgChart

## Finding
`OrgChartAdapterServiceCollectionExtensions.AddOrgChartAdapter` declares an `IConfiguration configuration` parameter (line 10) that is never read inside the method body. The inline comment `// reserved for future base-URL configuration` makes the YAGNI violation explicit.

File: `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartAdapterServiceCollectionExtensions.cs`

```csharp
public static IServiceCollection AddOrgChartAdapter(
    this IServiceCollection services,
    IConfiguration configuration) // reserved for future base-URL configuration  ← never used
{
    services.AddHttpClient();
    return services;
}
```

The call site in `Program.cs:128` passes `builder.Configuration`, which is also thrown away.

## Why it matters
The parameter signals to readers that it does something — they spend time looking for the configuration it supposedly affects. "Reserved for future use" in production code is speculative design; when the need arises, adding a parameter is a one-line change. Until then, the dead parameter is misleading noise.

## Suggested fix
Remove the parameter from the method signature and update the single call site in `Program.cs`:

```csharp
// OrgChartAdapterServiceCollectionExtensions.cs
public static IServiceCollection AddOrgChartAdapter(this IServiceCollection services)
{
    services.AddHttpClient();
    return services;
}

// Program.cs
builder.Services.AddOrgChartAdapter();
```

---
_Filed by daily arch-review routine on 2026-07-16._
