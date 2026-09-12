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
