### task: remove-unused-orgchart-config-param

You are working in a .NET 8 backend repository. Your job is a small, mechanical cleanup across exactly two files. Follow the steps below in order.

#### Step 1: Confirm the current state of the target file

Read `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartAdapterServiceCollectionExtensions.cs`. Its current full contents are exactly:

```csharp
using Anela.Heblo.Application.Features.OrgChart.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.OrgChart;

public static class OrgChartAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddOrgChartAdapter(
        this IServiceCollection services,
        IConfiguration configuration) // reserved for future base-URL configuration
    {
        services.AddHttpClient<IOrgChartService, OrgChartService>();
        return services;
    }
}
```

If the file does not match this exactly, stop and report the discrepancy instead of proceeding — do not guess at edits.

#### Step 2: Edit the target file

Make two changes to `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartAdapterServiceCollectionExtensions.cs`:

1. Remove the now-unused using directive. Change:
   ```csharp
   using Anela.Heblo.Application.Features.OrgChart.Services;
   using Microsoft.Extensions.Configuration;
   using Microsoft.Extensions.DependencyInjection;
   ```
   to:
   ```csharp
   using Anela.Heblo.Application.Features.OrgChart.Services;
   using Microsoft.Extensions.DependencyInjection;
   ```

2. Simplify the method signature. Change:
   ```csharp
   public static IServiceCollection AddOrgChartAdapter(
       this IServiceCollection services,
       IConfiguration configuration) // reserved for future base-URL configuration
   {
   ```
   to:
   ```csharp
   public static IServiceCollection AddOrgChartAdapter(this IServiceCollection services)
   {
   ```

The method body (`services.AddHttpClient<IOrgChartService, OrgChartService>(); return services;`) must remain exactly unchanged.

The resulting file must be exactly:

```csharp
using Anela.Heblo.Application.Features.OrgChart.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.OrgChart;

public static class OrgChartAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddOrgChartAdapter(this IServiceCollection services)
    {
        services.AddHttpClient<IOrgChartService, OrgChartService>();
        return services;
    }
}
```

#### Step 3: Update the call site in Program.cs

Read `backend/src/Anela.Heblo.API/Program.cs` around line 128. The current line is:

```csharp
        builder.Services.AddOrgChartAdapter(builder.Configuration);
```

It sits between these two unchanged lines (do not touch them — they are shown only so you can locate the correct line uniquely):

```csharp
        builder.Services.AddPlaudAdapter(builder.Configuration);
        builder.Services.AddMicrosoft365Adapter(builder.Configuration);
        builder.Services.AddOrgChartAdapter(builder.Configuration);

        builder.Services.AddSingleton<IIssuedInvoiceSource>(sp => sp.GetRequiredService<ShoptetApiInvoiceSource>());
```

Change only the `AddOrgChartAdapter` line from:
```csharp
        builder.Services.AddOrgChartAdapter(builder.Configuration);
```
to:
```csharp
        builder.Services.AddOrgChartAdapter();
```

Do not modify the `AddPlaudAdapter`, `AddMicrosoft365Adapter`, or `AddSingleton<IIssuedInvoiceSource>` lines, or any other line in `Program.cs`.

#### Step 4: Search for any other call sites

Run a repository-wide search to confirm there are no other callers of `AddOrgChartAdapter` that would break:

```bash
grep -rn "AddOrgChartAdapter" backend/ --include="*.cs"
```

Expected output: exactly two matches — the method definition in `OrgChartAdapterServiceCollectionExtensions.cs` and the call site in `Program.cs`. If any additional call site appears (e.g. in a test project), update it the same way: remove the `builder.Configuration` (or equivalent `IConfiguration` variable) argument from the call.

#### Step 5: Build the backend

From the repository root, run:

```bash
cd backend && dotnet build
```

The build must succeed with no errors. If it fails, read the compiler error carefully — a failure here almost always means either the signature edit or the call-site edit was not applied exactly as specified above, or an additional call site was missed in Step 4.

#### Step 6: Format the backend

From the repository root, run:

```bash
cd backend && dotnet format
```

This should complete without needing further manual changes (the edit already follows the existing file's style). If `dotnet format` modifies any file beyond the two touched above, review the diff to ensure it only applies whitespace/style normalization, not unrelated logic changes.

#### Step 7: Run the affected backend tests

There are no tests specific to `AddOrgChartAdapter` (it is a DI registration extension method with no dedicated unit test). Run the full backend test suite to make sure nothing else references the old signature:

```bash
cd backend && dotnet test
```

All tests must pass. A failure referencing `AddOrgChartAdapter` or `OrgChartAdapterServiceCollectionExtensions` indicates a missed call site — go back to Step 4.

#### Step 8: Verify the final diff

Run:

```bash
git diff backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartAdapterServiceCollectionExtensions.cs backend/src/Anela.Heblo.API/Program.cs
```

Confirm the diff shows exactly:
- In `OrgChartAdapterServiceCollectionExtensions.cs`: removal of the `using Microsoft.Extensions.Configuration;` line, and the method signature collapsed to a single-line `public static IServiceCollection AddOrgChartAdapter(this IServiceCollection services)` with the `// reserved for future base-URL configuration` comment gone.
- In `Program.cs`: the single line `builder.Services.AddOrgChartAdapter(builder.Configuration);` changed to `builder.Services.AddOrgChartAdapter();`, with no other line changed.

If the diff contains any other changes (e.g. from `dotnet format` touching unrelated files), revert those unrelated changes and keep only the two files listed in the spec.

#### Step 9: Commit

Stage exactly the two files and commit:

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartAdapterServiceCollectionExtensions.cs backend/src/Anela.Heblo.API/Program.cs
git commit -m "Remove unused IConfiguration parameter from AddOrgChartAdapter

YAGNI cleanup: the parameter was never read in the method body and was
marked 'reserved for future base-URL configuration'. Removed it and
updated the single call site in Program.cs."
```

**Verification checklist for this task:**
- [ ] `OrgChartAdapterServiceCollectionExtensions.cs` matches the exact target contents in Step 2.
- [ ] `Program.cs` line (formerly 128) reads `builder.Services.AddOrgChartAdapter();` and no adjacent line changed.
- [ ] `grep -rn "AddOrgChartAdapter" backend/ --include="*.cs"` returns exactly 2 matches (definition + call site).
- [ ] `cd backend && dotnet build` succeeds with zero errors.
- [ ] `cd backend && dotnet format` completes cleanly.
- [ ] `cd backend && dotnet test` — all tests pass.
- [ ] `git diff` touches only the two named files, with only the described changes.
- [ ] Changes committed.
