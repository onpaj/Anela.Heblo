# Move OrgChartService to Adapter Project Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development

**Goal:** Extract OrgChartService from the Application layer into a new Anela.Heblo.Adapters.OrgChart project to satisfy Clean Architecture's rule that the Application layer must not carry infrastructure dependencies.

**Architecture:** A new class-library project `Anela.Heblo.Adapters.OrgChart` is added under `backend/src/Adapters/`, mirroring the existing adapter layout (Cups being the closest structural match). OrgChartService.cs is moved there with its namespace updated; the DI registration migrates from OrgChartModule.cs into a new extension class in the adapter project, registered in Program.cs alongside the other adapters.

**Tech Stack:** .NET 8, C#, Microsoft.Extensions.Http, Microsoft.Extensions.Options.ConfigurationExtensions, MSBuild (.csproj + .sln)

---

### task: create-adapter-project

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/Anela.Heblo.Adapters.OrgChart.csproj`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartService.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartAdapterServiceCollectionExtensions.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartModule.cs`
- Delete: `backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs`
- Delete: `backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/` (directory)
- Modify: `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
- Modify: `backend/src/Anela.Heblo.API/Program.cs`
- Modify: `Anela.Heblo.sln`

- [ ] Step 1: Create the adapter project directory and csproj.

  ```bash
  mkdir -p backend/src/Adapters/Anela.Heblo.Adapters.OrgChart
  ```

  Create `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/Anela.Heblo.Adapters.OrgChart.csproj` with this exact content (modelled on Cups):

  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <TargetFramework>net8.0</TargetFramework>
      <Nullable>enable</Nullable>
      <ImplicitUsings>enable</ImplicitUsings>
      <RootNamespace>Anela.Heblo.Adapters.OrgChart</RootNamespace>
    </PropertyGroup>
    <ItemGroup>
      <PackageReference Include="Microsoft.Extensions.Http" Version="8.0.0" />
      <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="8.0.0" />
    </ItemGroup>
    <ItemGroup>
      <ProjectReference Include="..\..\Anela.Heblo.Application\Anela.Heblo.Application.csproj" />
    </ItemGroup>
  </Project>
  ```

- [ ] Step 2: Create `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartService.cs`.

  This is the existing `OrgChartService.cs` with only the namespace changed from `Anela.Heblo.Application.Features.OrgChart.Infrastructure` to `Anela.Heblo.Adapters.OrgChart`. All using directives and logic are preserved verbatim:

  ```csharp
  using System.Text.Json;
  using Anela.Heblo.Application.Features.OrgChart.Contracts;
  using Anela.Heblo.Application.Features.OrgChart.Services;
  using Microsoft.Extensions.Logging;
  using Microsoft.Extensions.Options;

  namespace Anela.Heblo.Adapters.OrgChart;

  /// <summary>
  /// Service for retrieving organizational chart data from external source
  /// </summary>
  public class OrgChartService : IOrgChartService
  {
      private static readonly JsonSerializerOptions JsonOptions = new()
      {
          PropertyNameCaseInsensitive = true
      };

      private readonly HttpClient _httpClient;
      private readonly OrgChartOptions _options;
      private readonly ILogger<OrgChartService> _logger;

      public OrgChartService(
          HttpClient httpClient,
          IOptions<OrgChartOptions> options,
          ILogger<OrgChartService> logger)
      {
          _httpClient = httpClient;
          _options = options.Value;
          _logger = logger;
      }

      /// <inheritdoc />
      public async Task<OrgChartResponse> GetOrganizationStructureAsync(CancellationToken cancellationToken = default)
      {
          try
          {
              _logger.LogInformation("Fetching organizational structure from {Url}", _options.DataSourceUrl);

              var response = await _httpClient.GetAsync(_options.DataSourceUrl, cancellationToken);
              response.EnsureSuccessStatusCode();

              var content = await response.Content.ReadAsStringAsync(cancellationToken);

              var orgChart = JsonSerializer.Deserialize<OrgChartResponse>(content, JsonOptions);

              if (orgChart == null)
              {
                  throw new InvalidOperationException("Failed to deserialize organizational structure");
              }

              _logger.LogInformation(
                  "Successfully loaded organizational structure: {PositionCount} positions, {EmployeeCount} employees",
                  orgChart.Organization.Positions.Count,
                  orgChart.Organization.Positions.Sum(p => p.Employees.Count));

              return orgChart;
          }
          catch (HttpRequestException ex)
          {
              throw new InvalidOperationException($"Failed to fetch organizational structure: {ex.Message}", ex);
          }
          catch (JsonException ex)
          {
              throw new InvalidOperationException($"Failed to parse organizational structure: {ex.Message}", ex);
          }
      }
  }
  ```

- [ ] Step 3: Create `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartAdapterServiceCollectionExtensions.cs`:

  ```csharp
  using Anela.Heblo.Application.Features.OrgChart.Services;
  using Microsoft.Extensions.Configuration;
  using Microsoft.Extensions.DependencyInjection;

  namespace Anela.Heblo.Adapters.OrgChart;

  /// <summary>
  /// DI registration for the OrgChart adapter
  /// </summary>
  public static class OrgChartAdapterServiceCollectionExtensions
  {
      /// <summary>
      /// Registers the OrgChart typed HttpClient adapter
      /// </summary>
      public static IServiceCollection AddOrgChartAdapter(
          this IServiceCollection services,
          IConfiguration configuration) // reserved for future base-URL configuration
      {
          services.AddHttpClient<IOrgChartService, OrgChartService>();
          return services;
      }
  }
  ```

- [ ] Step 4: Modify `backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartModule.cs`.

  Remove the `AddHttpClient` call and the `using` for the old Infrastructure namespace. The file currently reads:

  ```csharp
  using Anela.Heblo.Application.Features.OrgChart.Services;
  using Anela.Heblo.Application.Features.OrgChart.Infrastructure;
  using Microsoft.Extensions.Configuration;
  using Microsoft.Extensions.DependencyInjection;

  namespace Anela.Heblo.Application.Features.OrgChart;

  /// <summary>
  /// Module for registering OrgChart feature services
  /// </summary>
  public static class OrgChartModule
  {
      /// <summary>
      /// Registers OrgChart feature services
      /// </summary>
      public static IServiceCollection AddOrgChartServices(this IServiceCollection services, IConfiguration configuration)
      {
          // Register configuration options with startup validation
          services
              .AddOptions<OrgChartOptions>()
              .Bind(configuration.GetSection(OrgChartOptions.SectionName))
              .ValidateDataAnnotations()
              .ValidateOnStart();

          // Register HTTP client for fetching organization data
          services.AddHttpClient<IOrgChartService, OrgChartService>();

          // MediatR handlers are automatically registered by AddMediatR scan

          return services;
      }
  }
  ```

  Replace it with:

  ```csharp
  using Microsoft.Extensions.Configuration;
  using Microsoft.Extensions.DependencyInjection;

  namespace Anela.Heblo.Application.Features.OrgChart;

  /// <summary>
  /// Module for registering OrgChart feature services
  /// </summary>
  public static class OrgChartModule
  {
      /// <summary>
      /// Registers OrgChart feature services
      /// </summary>
      public static IServiceCollection AddOrgChartServices(this IServiceCollection services, IConfiguration configuration)
      {
          // Register configuration options with startup validation
          services
              .AddOptions<OrgChartOptions>()
              .Bind(configuration.GetSection(OrgChartOptions.SectionName))
              .ValidateDataAnnotations()
              .ValidateOnStart();

          // MediatR handlers are automatically registered by AddMediatR scan

          return services;
      }
  }
  ```

- [ ] Step 5: Delete the now-empty Infrastructure directory:

  ```bash
  rm backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs
  rmdir backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure
  ```

- [ ] Step 6: Add a ProjectReference to `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`.

  In the `<ItemGroup>` block that contains all adapter ProjectReferences (ends at line 78 with Microsoft365), append after the Microsoft365 line:

  Old (the last two lines of the adapters ItemGroup):
  ```xml
          <ProjectReference Include="..\Adapters\Anela.Heblo.Adapters.OpenMeteo\Anela.Heblo.Adapters.OpenMeteo.csproj" />
          <ProjectReference Include="..\Adapters\Anela.Heblo.Adapters.Microsoft365\Anela.Heblo.Adapters.Microsoft365.csproj" />
      </ItemGroup>
  ```

  New:
  ```xml
          <ProjectReference Include="..\Adapters\Anela.Heblo.Adapters.OpenMeteo\Anela.Heblo.Adapters.OpenMeteo.csproj" />
          <ProjectReference Include="..\Adapters\Anela.Heblo.Adapters.Microsoft365\Anela.Heblo.Adapters.Microsoft365.csproj" />
          <ProjectReference Include="..\Adapters\Anela.Heblo.Adapters.OrgChart\Anela.Heblo.Adapters.OrgChart.csproj" />
      </ItemGroup>
  ```

- [ ] Step 7: Update `backend/src/Anela.Heblo.API/Program.cs`.

  Add `using Anela.Heblo.Adapters.OrgChart;` in the using block at the top (after `using Anela.Heblo.Adapters.Microsoft365;` on line 7):

  Old:
  ```csharp
  using Anela.Heblo.Adapters.Microsoft365;
  ```

  New:
  ```csharp
  using Anela.Heblo.Adapters.Microsoft365;
  using Anela.Heblo.Adapters.OrgChart;
  ```

  Then add the adapter call after `AddMicrosoft365Adapter` (line 119):

  Old:
  ```csharp
          builder.Services.AddMicrosoft365Adapter(builder.Configuration);

          builder.Services.AddSingleton<IIssuedInvoiceSource>(sp => sp.GetRequiredService<ShoptetApiInvoiceSource>());
  ```

  New:
  ```csharp
          builder.Services.AddMicrosoft365Adapter(builder.Configuration);
          builder.Services.AddOrgChartAdapter(builder.Configuration);

          builder.Services.AddSingleton<IIssuedInvoiceSource>(sp => sp.GetRequiredService<ShoptetApiInvoiceSource>());
  ```

- [ ] Step 8: Register the new project in `Anela.Heblo.sln`.

  Add the Project entry after the Microsoft365 entry (line 85, before `Global`). The GUID for the new project is `{C7A82E5F-3D91-4B06-A2F8-9E0C1D5B4738}` (new unique GUID as specified in the spec: note spec says `{4B6F17C3-...}` is the Adapters *folder* GUID — the project needs its own GUID; use the one from the spec: actually spec gives GUID for the *folder* already existing, so generate a fresh one).

  Insert after line 85 (`EndProject` for Microsoft365), before line 86 (`Global`):

  Old:
  ```
  Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Anela.Heblo.Adapters.Microsoft365", "backend\src\Adapters\Anela.Heblo.Adapters.Microsoft365\Anela.Heblo.Adapters.Microsoft365.csproj", "{FB4CF527-5C59-4D5A-9A47-835C6C1AC76B}"
  EndProject
  Global
  ```

  New:
  ```
  Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Anela.Heblo.Adapters.Microsoft365", "backend\src\Adapters\Anela.Heblo.Adapters.Microsoft365\Anela.Heblo.Adapters.Microsoft365.csproj", "{FB4CF527-5C59-4D5A-9A47-835C6C1AC76B}"
  EndProject
  Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Anela.Heblo.Adapters.OrgChart", "backend\src\Adapters\Anela.Heblo.Adapters.OrgChart\Anela.Heblo.Adapters.OrgChart.csproj", "{C7A82E5F-3D91-4B06-A2F8-9E0C1D5B4738}"
  EndProject
  Global
  ```

  Also add the NestedProjects entry (to place the project inside the Adapters solution folder). In the `GlobalSection(NestedProjects)` block, after line 520 (`{FB4CF527...} = {4B6F17C3...}`), add:

  Old:
  ```
  		{FB4CF527-5C59-4D5A-9A47-835C6C1AC76B} = {4B6F17C3-0A57-487A-BE8C-1808B40EC604}
  	EndGlobalSection
  ```

  New:
  ```
  		{FB4CF527-5C59-4D5A-9A47-835C6C1AC76B} = {4B6F17C3-0A57-487A-BE8C-1808B40EC604}
  		{C7A82E5F-3D91-4B06-A2F8-9E0C1D5B4738} = {4B6F17C3-0A57-487A-BE8C-1808B40EC604}
  	EndGlobalSection
  ```

- [ ] Step 9: Verify the build succeeds with zero errors and zero new warnings:

  ```bash
  cd backend && dotnet build Anela.Heblo.sln
  ```

  Expected: `Build succeeded.` with 0 Error(s).

- [ ] Step 10: Verify formatting is clean:

  ```bash
  cd backend && dotnet format Anela.Heblo.sln --verify-no-changes
  ```

  Expected: exits 0 with no reported changes.

- [ ] Step 11: **Commit**

  ```bash
  git add \
    backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/Anela.Heblo.Adapters.OrgChart.csproj \
    backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartService.cs \
    backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartAdapterServiceCollectionExtensions.cs \
    backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartModule.cs \
    backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj \
    backend/src/Anela.Heblo.API/Program.cs \
    Anela.Heblo.sln
  git rm backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs
  git commit -m "feat: move OrgChartService to Anela.Heblo.Adapters.OrgChart

  Extracts the HttpClient-based OrgChartService from the Application layer
  into a new dedicated adapter project, satisfying Clean Architecture's
  requirement that the Application layer must not carry infrastructure
  dependencies. No runtime behaviour change."
  ```
