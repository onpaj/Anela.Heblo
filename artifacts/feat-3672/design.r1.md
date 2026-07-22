# Design: Remove unused `IConfiguration` parameter from `AddOrgChartAdapter`

## Component Design
`OrgChartAdapterServiceCollectionExtensions.AddOrgChartAdapter` (in `Anela.Heblo.Adapters.OrgChart`) drops its unused `IConfiguration configuration` parameter, becoming `AddOrgChartAdapter(this IServiceCollection services)`. Its sole responsibility — registering `HttpClient<IOrgChartService, OrgChartService>()` — is unchanged. The unused `using Microsoft.Extensions.Configuration;` directive is removed. The single call site, `Program.cs:128`, is updated from `AddOrgChartAdapter(builder.Configuration)` to `AddOrgChartAdapter()`. No other component, interface, or module boundary is affected.

## Data Schemas
Not applicable — no database, API, or event payload is involved; this is a compile-time DI method signature change only.
