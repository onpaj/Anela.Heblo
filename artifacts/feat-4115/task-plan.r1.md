# GiftPackageManufacture Query/Command Interface Split — Implementation Plan

**Goal:** Split `IGiftPackageManufactureService` into a read-only `IGiftPackageQueryService` (the two lookup methods) and a narrowed `IGiftPackageManufactureService` (the two write methods), with `GiftPackageManufactureService` implementing both from a single shared DI instance, so each MediatR handler's constructor only names the capability it actually uses.
**Architecture:** One new file, `IGiftPackageQueryService.cs`, is added next to the existing `IGiftPackageManufactureService.cs` in the `GiftPackageManufacture/Services/` folder. `GiftPackageManufactureService` keeps all four method bodies and all private helpers unchanged and gains a second interface on its class declaration. `GiftPackageManufactureModule.cs` registers the concrete class once as the scoped root and aliases both interfaces to it via factory delegates (`sp.GetRequiredService<GiftPackageManufactureService>()`), guaranteeing one shared instance per DI scope. `GetAvailableGiftPackagesHandler` and `GetGiftPackageDetailHandler` switch their constructor dependency to `IGiftPackageQueryService`; `CreateGiftPackageManufactureHandler` and `DisassembleGiftPackageHandler` are untouched. No DTOs, MediatR contracts, controllers, or persistence are affected.
**Tech Stack:** .NET 8, MediatR, Microsoft.Extensions.DependencyInjection, xUnit, Moq, FluentAssertions, EF Core InMemory provider (test-only)

---

### task: add-gift-package-query-service-interface

**Context:** The two read methods that today live on `IGiftPackageManufactureService` (`GetAvailableGiftPackagesAsync`, `GetGiftPackageDetailAsync`) need a new, narrower home: `IGiftPackageQueryService`. This task only adds the new interface file with identical signatures (same parameter names, order, and defaults) to what exists today — it does not yet change `IGiftPackageManufactureService`, the concrete service class, the DI registration, or any handler, so the solution keeps compiling exactly as before once this file is added. This is purely additive; nothing consumes the new interface yet.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/IGiftPackageQueryService.cs`

- [ ] **Step 1: Create the new interface file**

  Create `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/IGiftPackageQueryService.cs` with exactly this content:
  ```csharp
  using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;

  namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services;

  public interface IGiftPackageQueryService
  {
      Task<List<GiftPackageDto>> GetAvailableGiftPackagesAsync(decimal salesCoefficient = 1.0m, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default);

      Task<GiftPackageDto> GetGiftPackageDetailAsync(string giftPackageCode, decimal salesCoefficient = 1.0m, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default);
  }
  ```

- [ ] **Step 2: Build the solution and confirm it still compiles cleanly**
  ```bash
  dotnet build Anela.Heblo.sln
  ```
  Expect a successful build with zero errors — the new interface is not referenced by anything yet, so nothing else can break.

- [ ] **Step 3: Commit**
  ```bash
  git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/IGiftPackageQueryService.cs
  git commit -m "$(cat <<'EOF'
  feat(gift-package-manufacture): add IGiftPackageQueryService interface

  Introduces the read-only contract that GetAvailableGiftPackagesAsync and
  GetGiftPackageDetailAsync will move to, per the ISP split for this module.
  Purely additive — no existing code references it yet.

  Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01DrJxdTYJuL7vx4BmzgnVoH
  EOF
  )"
  ```

---

### task: implement-query-interface-on-concrete-service

**Context:** `GiftPackageManufactureService` already contains the bodies for `GetAvailableGiftPackagesAsync` and `GetGiftPackageDetailAsync` (matching the new `IGiftPackageQueryService` interface's signatures exactly), so declaring that the class also implements `IGiftPackageQueryService` requires no method-body changes — only the class declaration line changes. `IGiftPackageManufactureService` still has all four methods at this point (it is narrowed in a later task), so this step compiles trivially: the class already satisfies both interfaces. Doing this before narrowing `IGiftPackageManufactureService` and before touching the DI registration keeps each task independently buildable.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs` (350 lines total; only the class declaration line changes)

- [ ] **Step 1: Change the class declaration to implement both interfaces**

  In `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs`, find this line:
  ```csharp
  public class GiftPackageManufactureService : IGiftPackageManufactureService
  ```
  Replace it with:
  ```csharp
  public class GiftPackageManufactureService : IGiftPackageManufactureService, IGiftPackageQueryService
  ```
  No other line in this file changes. `IGiftPackageQueryService` is already visible without a new `using` statement — it lives in the same namespace (`Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services`) as `GiftPackageManufactureService` itself.

- [ ] **Step 2: Build the solution and confirm it compiles cleanly**
  ```bash
  dotnet build Anela.Heblo.sln
  ```
  Expect a successful build with zero errors. `GiftPackageManufactureService` already implements every member `IGiftPackageQueryService` declares (`GetAvailableGiftPackagesAsync`, `GetGiftPackageDetailAsync`), so no new compile errors are introduced.

- [ ] **Step 3: Run the existing service-level tests to confirm no behavioral change**
  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GiftPackageManufactureServiceTests"
  ```
  Expect all existing tests in `GiftPackageManufactureServiceTests` to pass unmodified — this file instantiates the concrete class directly and calls its methods, so adding a second interface to the class declaration cannot affect it.

- [ ] **Step 4: Commit**
  ```bash
  git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs
  git commit -m "$(cat <<'EOF'
  feat(gift-package-manufacture): implement IGiftPackageQueryService on GiftPackageManufactureService

  Adds IGiftPackageQueryService to the class declaration; no method bodies
  change. GiftPackageManufactureService already implements both read methods,
  so this is a pure interface-shape addition.

  Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01DrJxdTYJuL7vx4BmzgnVoH
  EOF
  )"
  ```

---

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

### task: narrow-manufacture-interface-and-update-query-handlers

**Context:** This task performs the actual ISP split: `IGiftPackageManufactureService` loses its two read methods (FR-2), and the two query handlers (`GetAvailableGiftPackagesHandler`, `GetGiftPackageDetailHandler`) switch their constructor dependency to the new `IGiftPackageQueryService` (FR-5). These three file changes are bundled into one task because narrowing the interface alone leaves the two query handlers non-compiling (they call methods no longer on `IGiftPackageManufactureService`) — the solution is only buildable again once all three files are updated together. `CreateGiftPackageManufactureHandler` and `DisassembleGiftPackageHandler` are not touched: they already only call `CreateManufactureAsync`/`DisassembleGiftPackageAsync`, both of which remain on the narrowed `IGiftPackageManufactureService`. Their existing tests (`CreateGiftPackageManufactureHandlerTests.cs`, `DisassembleGiftPackageHandlerTests.cs`) mock only those two write methods and are confirmed (by direct read) to need no changes.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/IGiftPackageManufactureService.cs` (whole file, 24 lines)
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/GetAvailableGiftPackages/GetAvailableGiftPackagesHandler.cs` (whole file, 27 lines)
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/GetGiftPackageDetail/GetGiftPackageDetailHandler.cs` (whole file, 49 lines)
- No test files are created or modified in this task (none exist for these two handlers today; the spec marks adding them out of scope).

- [ ] **Step 1: Narrow `IGiftPackageManufactureService` to the two write methods**

  Replace the entire contents of `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/IGiftPackageManufactureService.cs` with:
  ```csharp
  using System.ComponentModel;
  using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;

  namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services;

  public interface IGiftPackageManufactureService
  {
      [DisplayName("GiftPackageManufacture-{0}-{1}x")]
      Task<GiftPackageManufactureDto> CreateManufactureAsync(
          string giftPackageCode,
          int quantity,
          bool allowStockOverride,
          string userName,
          CancellationToken cancellationToken = default);

      Task<GiftPackageDisassemblyDto> DisassembleGiftPackageAsync(
          string giftPackageCode,
          int quantity,
          string userName,
          CancellationToken cancellationToken = default);
  }
  ```
  (`GetAvailableGiftPackagesAsync` and `GetGiftPackageDetailAsync` are removed. The `[DisplayName("GiftPackageManufacture-{0}-{1}x")]` attribute is kept byte-for-byte as-is, including its pre-existing text mismatch against the implementation's `[DisplayName("GiftPackageManufacture-{0}-{1}")]` on line 138 of `GiftPackageManufactureService.cs` — that mismatch is out of scope for this change and must not be touched.)

- [ ] **Step 2: Build the solution and confirm errors appear only in the two query handler files**
  ```bash
  dotnet build Anela.Heblo.sln
  ```
  Expect build errors only in `GetAvailableGiftPackagesHandler.cs` and `GetGiftPackageDetailHandler.cs`, each similar to:
  ```
  error CS1061: 'IGiftPackageManufactureService' does not contain a definition for 'GetAvailableGiftPackagesAsync' and no accessible extension method 'GetAvailableGiftPackagesAsync' accepting a first argument of type 'IGiftPackageManufactureService' could be found
  error CS1061: 'IGiftPackageManufactureService' does not contain a definition for 'GetGiftPackageDetailAsync' and no accessible extension method 'GetGiftPackageDetailAsync' accepting a first argument of type 'IGiftPackageManufactureService' could be found
  ```
  No other file should report an error — `GiftPackageManufactureService` itself still compiles (it satisfies `IGiftPackageQueryService` for those two methods and the narrowed `IGiftPackageManufactureService` for the other two), `CreateGiftPackageManufactureHandler.cs` and `DisassembleGiftPackageHandler.cs` still compile (they never called the removed methods), and `GiftPackageManufactureModule.cs` still compiles (its registration references the concrete class and both interfaces, not any specific member).

- [ ] **Step 3: Update `GetAvailableGiftPackagesHandler` to depend on `IGiftPackageQueryService`**

  Replace the entire contents of `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/GetAvailableGiftPackages/GetAvailableGiftPackagesHandler.cs` with:
  ```csharp
  using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services;
  using MediatR;

  namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetAvailableGiftPackages;

  public class GetAvailableGiftPackagesHandler : IRequestHandler<GetAvailableGiftPackagesRequest, GetAvailableGiftPackagesResponse>
  {
      private readonly IGiftPackageQueryService _giftPackageService;

      public GetAvailableGiftPackagesHandler(IGiftPackageQueryService giftPackageService)
      {
          _giftPackageService = giftPackageService;
      }

      public async Task<GetAvailableGiftPackagesResponse> Handle(GetAvailableGiftPackagesRequest request, CancellationToken cancellationToken)
      {
          var giftPackages = await _giftPackageService.GetAvailableGiftPackagesAsync(
              request.SalesCoefficient,
              request.FromDate,
              request.ToDate,
              cancellationToken);

          return new GetAvailableGiftPackagesResponse
          {
              GiftPackages = giftPackages
          };
      }
  }
  ```
  (Only the field type and constructor parameter type change, from `IGiftPackageManufactureService` to `IGiftPackageQueryService`. The field name `_giftPackageService`, the constructor parameter name `giftPackageService`, and the `Handle` method body are unchanged.)

- [ ] **Step 4: Build again and confirm only `GetGiftPackageDetailHandler.cs` still errors**
  ```bash
  dotnet build Anela.Heblo.sln
  ```
  Expect exactly one remaining error, in `GetGiftPackageDetailHandler.cs`:
  ```
  error CS1061: 'IGiftPackageManufactureService' does not contain a definition for 'GetGiftPackageDetailAsync' and no accessible extension method 'GetGiftPackageDetailAsync' accepting a first argument of type 'IGiftPackageManufactureService' could be found
  ```

- [ ] **Step 5: Update `GetGiftPackageDetailHandler` to depend on `IGiftPackageQueryService`**

  Replace the entire contents of `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/GetGiftPackageDetail/GetGiftPackageDetailHandler.cs` with:
  ```csharp
  using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services;
  using Anela.Heblo.Application.Shared;
  using MediatR;

  namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetGiftPackageDetail;

  public class GetGiftPackageDetailHandler : IRequestHandler<GetGiftPackageDetailRequest, GetGiftPackageDetailResponse>
  {
      private readonly IGiftPackageQueryService _giftPackageService;

      public GetGiftPackageDetailHandler(IGiftPackageQueryService giftPackageService)
      {
          _giftPackageService = giftPackageService;
      }

      public async Task<GetGiftPackageDetailResponse> Handle(GetGiftPackageDetailRequest request, CancellationToken cancellationToken)
      {
          try
          {
              var giftPackage = await _giftPackageService.GetGiftPackageDetailAsync(
                  request.GiftPackageCode,
                  request.SalesCoefficient,
                  request.FromDate,
                  request.ToDate,
                  cancellationToken);

              return new GetGiftPackageDetailResponse
              {
                  GiftPackage = giftPackage,
                  Success = true
              };
          }
          catch (ArgumentException ex)
          {
              return new GetGiftPackageDetailResponse
              {
                  Success = false,
                  ErrorCode = ErrorCodes.ValidationError
              };
          }
          catch (Exception ex)
          {
              return new GetGiftPackageDetailResponse
              {
                  Success = false,
                  ErrorCode = ErrorCodes.InternalServerError
              };
          }
      }
  }
  ```
  (Only the field type and constructor parameter type change, from `IGiftPackageManufactureService` to `IGiftPackageQueryService`. The `try`/`catch` structure, both `catch` bodies, and the exception variable names `ex` are unchanged byte-for-byte.)

- [ ] **Step 6: Build the whole solution and confirm zero errors**
  ```bash
  dotnet build Anela.Heblo.sln
  ```
  Expect a fully successful build.

- [ ] **Step 7: Run the full GiftPackageManufacture test slice and confirm everything passes**
  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GiftPackageManufacture"
  ```
  Expect every test in `CreateGiftPackageManufactureHandlerTests`, `DisassembleGiftPackageHandlerTests`, `GiftPackageManufactureServiceTests`, and `GiftPackageManufactureModuleTests` to pass. Confirm via `git status`/`git diff` that `CreateGiftPackageManufactureHandlerTests.cs` and `DisassembleGiftPackageHandlerTests.cs` show no changes:
  ```bash
  git diff --stat backend/test/Anela.Heblo.Tests/Application/GiftPackageManufacture/CreateGiftPackageManufactureHandlerTests.cs backend/test/Anela.Heblo.Tests/Application/GiftPackageManufacture/DisassembleGiftPackageHandlerTests.cs
  ```
  Expect empty output (no diff) — these files were never touched in this plan, confirming FR-6's acceptance criterion.

- [ ] **Step 8: Commit**
  ```bash
  git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/IGiftPackageManufactureService.cs \
          backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/GetAvailableGiftPackages/GetAvailableGiftPackagesHandler.cs \
          backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/GetGiftPackageDetail/GetGiftPackageDetailHandler.cs
  git commit -m "$(cat <<'EOF'
  refactor(gift-package-manufacture): split IGiftPackageManufactureService by ISP

  IGiftPackageManufactureService is narrowed to the two write methods
  (CreateManufactureAsync, DisassembleGiftPackageAsync). GetAvailableGiftPackagesHandler
  and GetGiftPackageDetailHandler now depend on IGiftPackageQueryService instead,
  matching the operations they actually consume. No behavioral change — the two
  write handlers and the concrete service implementation are unaffected.

  Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01DrJxdTYJuL7vx4BmzgnVoH
  EOF
  )"
  ```

---

### task: verify-full-solution

**Context:** A final end-to-end check that the whole refactor — across all five preceding commits — leaves the solution in a clean, fully green state: a full build, the full backend test suite (not just the GiftPackageManufacture slice), and `dotnet format` verification. This also re-confirms the module boundary architecture test still passes, since this change touches DI composition roots.

**Files:** None created or modified — verification only.

- [ ] **Step 1: Full solution build**
  ```bash
  dotnet build Anela.Heblo.sln
  ```
  Expect zero errors and no new warnings.

- [ ] **Step 2: Full backend test suite**
  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
  ```
  Expect the entire suite to pass, including `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` (runs implicitly as part of the full suite) — confirm it still passes unmodified; this refactor does not touch any cross-module dependency, only intra-module interface shape.

- [ ] **Step 3: Formatting check**
  ```bash
  dotnet format --verify-no-changes
  ```
  Expect no formatting violations. If this reports files needing changes, run `dotnet format` (without `--verify-no-changes`), review the diff to confirm it only reformats whitespace in files this plan touched, and commit that separately with message `style: dotnet format`.

- [ ] **Step 4: Confirm the working tree is clean**
  ```bash
  git status --short
  ```
  Expect no output — everything from this plan was committed at the end of its own task.
