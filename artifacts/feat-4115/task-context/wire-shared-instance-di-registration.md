### task: wire-shared-instance-di-registration


**Context:** The spec's FR-4 acceptance criterion requires that resolving `IGiftPackageManufactureService` and `IGiftPackageQueryService` from the same DI scope returns the same object reference — matching today's implicit behavior where every caller shares one `GiftPackageManufactureService` instance per scope. Two independent `AddScoped<TInterface, GiftPackageManufactureService>()` calls would NOT guarantee this (each resolution would construct its own instance). Per the architecture review's Decision 2, this task registers the concrete `GiftPackageManufactureService` once as the scoped root and aliases both interfaces to it via factory delegates that call `sp.GetRequiredService<GiftPackageManufactureService>()`. This is the one behavioral guarantee this refactor introduces that no handler-level test can catch, so this task adds a small, focused DI-resolution test alongside the registration change — written first so it fails for the right reason (the interface isn't registered yet), then turned green by the registration change.

This task depends on `implement-query-interface-on-concrete-service` (the concrete class must already implement `IGiftPackageQueryService` for the DI factory lambda to type-check) but does NOT depend on narrowing `IGiftPackageManufactureService` yet — that happens in the next task. At the end of this task, `IGiftPackageManufactureService` still has all four methods; only the registration shape changes.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/GiftPackageManufactureModule.cs`
- Create: `backend/test/Anela.Heblo.Tests/Application/GiftPackageManufacture/GiftPackageManufactureModuleTests.cs`

- [ ] **Step 1: Write the DI-scope test**

  Create `backend/test/Anela.Heblo.Tests/Application/GiftPackageManufacture/GiftPackageManufactureModuleTests.cs` with exactly this content:
  ```csharp
  using Anela.Heblo.Application.Features.Logistics.Contracts;
  using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture;
  using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services;
  using Anela.Heblo.Domain.Features.Manufacture;
  using Anela.Heblo.Persistence;
  using AutoMapper;
  using FluentAssertions;
  using Microsoft.EntityFrameworkCore;
  using Microsoft.Extensions.DependencyInjection;
  using Moq;
  using Xunit;

  namespace Anela.Heblo.Tests.Application.GiftPackageManufacture;

  public class GiftPackageManufactureModuleTests
  {
      private static ServiceProvider BuildProvider()
      {
          var services = new ServiceCollection();
          services.AddLogging();

          services.AddDbContext<ApplicationDbContext>(options =>
              options.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()));

          services.AddSingleton(new Mock<IManufactureClient>().Object);
          services.AddSingleton(new Mock<ILogisticsCatalogSource>().Object);
          services.AddSingleton(new Mock<ILogisticsStockOperationService>().Object);
          services.AddSingleton(new Mock<IMapper>().Object);
          services.AddSingleton(TimeProvider.System);

          services.AddGiftPackageManufactureModule();

          return services.BuildServiceProvider();
      }

      [Fact]
      public void Resolving_BothInterfaces_FromSameScope_ReturnsSameInstance()
      {
          using var provider = BuildProvider();
          using var scope = provider.CreateScope();

          var manufactureService = scope.ServiceProvider.GetRequiredService<IGiftPackageManufactureService>();
          var queryService = scope.ServiceProvider.GetRequiredService<IGiftPackageQueryService>();

          queryService.Should().BeSameAs(manufactureService);
      }

      [Fact]
      public void Resolving_BothInterfaces_FromDifferentScopes_ReturnsDifferentInstances()
      {
          using var provider = BuildProvider();

          using var scopeA = provider.CreateScope();
          using var scopeB = provider.CreateScope();

          var fromScopeA = scopeA.ServiceProvider.GetRequiredService<IGiftPackageManufactureService>();
          var fromScopeB = scopeB.ServiceProvider.GetRequiredService<IGiftPackageQueryService>();

          fromScopeB.Should().NotBeSameAs(fromScopeA);
      }
  }
  ```
  (The second test guards against over-correcting to a singleton registration, which would also make the first test pass but would violate the scoped-per-request lifetime the module has always used.)

- [ ] **Step 2: Run the new tests and confirm they fail**
  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GiftPackageManufactureModuleTests"
  ```
  Expect `Resolving_BothInterfaces_FromSameScope_ReturnsSameInstance` and `Resolving_BothInterfaces_FromDifferentScopes_ReturnsDifferentInstances` to both fail with an `InvalidOperationException` such as `No service for type 'Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services.IGiftPackageQueryService' has been registered.` — because `GiftPackageManufactureModule.cs` does not register `IGiftPackageQueryService` at all yet.

- [ ] **Step 3: Update the DI registration to Decision 2's shared-instance shape**

  In `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/GiftPackageManufactureModule.cs`, find this line:
  ```csharp
          // Register services
          services.AddScoped<IGiftPackageManufactureService, GiftPackageManufactureService>();
  ```
  Replace it with:
  ```csharp
          // Register services — the concrete type is the shared scoped root; both interfaces
          // alias to it so any resolution within one DI scope returns the same instance.
          services.AddScoped<GiftPackageManufactureService>();
          services.AddScoped<IGiftPackageManufactureService>(sp => sp.GetRequiredService<GiftPackageManufactureService>());
          services.AddScoped<IGiftPackageQueryService>(sp => sp.GetRequiredService<GiftPackageManufactureService>());
  ```
  The rest of the file (the repository factory registration and the `return services;` line) is unchanged.

- [ ] **Step 4: Run the new tests again and confirm they pass**
  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GiftPackageManufactureModuleTests"
  ```
  Expect both tests to pass.

- [ ] **Step 5: Build the whole solution to confirm nothing else broke**
  ```bash
  dotnet build Anela.Heblo.sln
  ```
  Expect a successful build with zero errors. `IGiftPackageManufactureService` still has all four methods at this point, so `GetAvailableGiftPackagesHandler` and `GetGiftPackageDetailHandler` (which still depend on it) keep compiling unchanged.

- [ ] **Step 6: Commit**
  ```bash
  git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/GiftPackageManufactureModule.cs \
          backend/test/Anela.Heblo.Tests/Application/GiftPackageManufacture/GiftPackageManufactureModuleTests.cs
  git commit -m "$(cat <<'EOF'
  feat(gift-package-manufacture): register GiftPackageManufactureService as shared scoped root

  IGiftPackageManufactureService and IGiftPackageQueryService now alias the
  same GiftPackageManufactureService instance within one DI scope, per
  arch-review Decision 2. Adds a focused DI-resolution test proving the
  same-instance guarantee (and that two different scopes still get
  different instances).

  Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01DrJxdTYJuL7vx4BmzgnVoH
  EOF
  )"
  ```

---
