### task: fix-unit-tests

The two DI-registration tests in `GraphServiceTests.cs` call `AddUserManagement()` to verify that `IGraphService` is wired up. After this refactor, registration moves to `AddMicrosoft365Adapter()`. Update those two tests so they call both methods (or just `AddMicrosoft365Adapter()`). Also update the `using` directives because `GraphService` and `MockGraphService` have moved namespaces.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs`

- [ ] Add `using Anela.Heblo.Adapters.Microsoft365;` and `using Anela.Heblo.Adapters.Microsoft365.UserManagement;` to the top of the file.

- [ ] Remove `using Anela.Heblo.Application.Features.UserManagement.Services;` — `GraphService` and `MockGraphService` no longer live there. The test still constructs `new GraphService(...)` directly, so it must import from the new namespace.

  The updated using block at the top of the file:

```csharp
using System.Net;
using Anela.Heblo.Adapters.Microsoft365;
using Anela.Heblo.Adapters.Microsoft365.UserManagement;
using Anela.Heblo.Application.Features.UserManagement;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using Moq;
```

  Wait — after the move `GraphService` lives in `Anela.Heblo.Adapters.Microsoft365.UserManagement`, so `using Anela.Heblo.Application.Features.UserManagement.Services;` should be **replaced** by `using Anela.Heblo.Adapters.Microsoft365.UserManagement;`. Keep `IGraphService` import via `Anela.Heblo.Application.Features.UserManagement.Services` (that namespace still holds `IGraphService.cs`). The correct final using block:

```csharp
using System.Net;
using Anela.Heblo.Adapters.Microsoft365;
using Anela.Heblo.Adapters.Microsoft365.UserManagement;
using Anela.Heblo.Application.Features.UserManagement;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using Moq;
```

  (`IGraphService` is in `Anela.Heblo.Application.Features.UserManagement.Services` — the `using` for that namespace stays.)

- [ ] Update `AddUserManagement_ProductionBranch_RegistersMicrosoftGraphNamedClient_AndResolvesGraphService`. The test must call `AddMicrosoft365Adapter(configuration)` in addition to `AddUserManagement(configuration)` so that `IGraphService` gets registered. Also add the services that `GraphService` constructor requires (`ITokenAcquisition`):

```csharp
[Fact]
public void AddUserManagement_ProductionBranch_RegistersMicrosoftGraphNamedClient_AndResolvesGraphService()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddMemoryCache();
    services.AddSingleton(Mock.Of<ITokenAcquisition>());
    var configuration = new ConfigurationBuilder().Build(); // no mock-auth keys => production branch
    services.AddSingleton<IConfiguration>(configuration);

    // Act
    services.AddMicrosoft365Adapter(configuration);
    services.AddUserManagement(configuration);

    // Assert
    using var provider = services.BuildServiceProvider();
    using var scope = provider.CreateScope();
    var resolved = scope.ServiceProvider.GetRequiredService<IGraphService>();
    resolved.Should().BeOfType<GraphService>();

    var factory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
    var client = factory.CreateClient("MicrosoftGraph");
    client.Should().NotBeNull();
}
```

- [ ] Update `AddUserManagement_MockBranch_RegistersMockGraphService`. Same pattern — call `AddMicrosoft365Adapter(configuration)` so that `MockGraphService` is registered via the adapter's `else` branch:

```csharp
[Fact]
public void AddUserManagement_MockBranch_RegistersMockGraphService()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddMemoryCache();
    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["UseMockAuth"] = "true"
        })
        .Build();

    // Act
    services.AddMicrosoft365Adapter(configuration);
    services.AddUserManagement(configuration);

    // Assert
    using var provider = services.BuildServiceProvider();
    using var scope = provider.CreateScope();
    var resolved = scope.ServiceProvider.GetRequiredService<IGraphService>();
    resolved.Should().BeOfType<MockGraphService>();
}
```

- [ ] Run all tests in the UserManagement test suite:

```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~UserManagement"
```

All tests must pass with zero failures.

- [ ] Run `dotnet format backend/backend.sln --verify-no-changes` to confirm no formatting drift. If it reports changes, run `dotnet format backend/backend.sln` and re-check.

- [ ] Final sanity check — full solution build:

```
dotnet build backend/backend.sln
```

Zero errors, zero warnings about missing types.