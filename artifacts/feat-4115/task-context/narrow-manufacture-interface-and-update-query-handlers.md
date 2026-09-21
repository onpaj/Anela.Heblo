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
