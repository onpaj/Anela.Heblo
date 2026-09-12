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
