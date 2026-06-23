### task: update-project-files

Patch the two `.csproj` files: add `InternalsVisibleTo` to the adapter project (so tests can call `ParseMembersFromJson`), and strip the now-redundant `Microsoft.Graph` and `Microsoft.Identity.Web` PackageReferences from the Application project.

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Anela.Heblo.Adapters.Microsoft365.csproj`
- Modify: `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`

- [ ] Add `<InternalsVisibleTo Include="Anela.Heblo.Tests" />` to `Anela.Heblo.Adapters.Microsoft365.csproj`. The current file has no `InternalsVisibleTo` element at all. Add a new `<ItemGroup>` for it. Complete file after edit:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Anela.Heblo.Adapters.Microsoft365</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Graph" Version="5.92.0" />
    <PackageReference Include="Microsoft.Identity.Web" Version="3.14.1" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\Anela.Heblo.Application\Anela.Heblo.Application.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Anela.Heblo.Tests" />
  </ItemGroup>

</Project>
```

- [ ] Remove the `Microsoft.Graph` and `Microsoft.Identity.Web` PackageReferences from `Anela.Heblo.Application.csproj`. These are the two lines:

```xml
    <PackageReference Include="Microsoft.Graph" Version="5.92.0" />
    <PackageReference Include="Microsoft.Identity.Web" Version="3.14.1" />
```

After removing them the `<ItemGroup>` containing those two lines will be empty — remove the whole `<ItemGroup>` block too. The affected section currently sits between `<PackageReference Include="Microsoft.FeatureManagement" .../>` and `<PackageReference Include="Polly" .../>`. The remaining file keeps all other PackageReferences intact.

- [ ] Run `dotnet build backend/backend.sln` — must still be zero errors. This confirms Application no longer needs those packages.

---