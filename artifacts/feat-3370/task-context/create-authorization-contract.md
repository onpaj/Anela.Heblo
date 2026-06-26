### task: create-authorization-contract

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Authorization/Contracts/IEntraAccessUserSource.cs`

- [ ] **Step 1: Create the `Contracts/` directory and the interface file.**

  The `Contracts/` folder for the Authorization module does not yet exist — create both the
  directory and the file. The interface has exactly one method. The record is a `sealed record`
  (internal domain transport — Authorization module owns both, so no OpenAPI generator sees it).

  ```csharp
  namespace Anela.Heblo.Application.Features.Authorization.Contracts;

  public interface IEntraAccessUserSource
  {
      Task<List<EntraAccessUserRecord>> GetBaseMembersAsync(CancellationToken ct);
  }

  public sealed record EntraAccessUserRecord(string Id, string Email, string DisplayName);
  ```

- [ ] **Step 2: Verify the file compiles in isolation.**

  ```bash
  dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
  ```

  Expected: build succeeds (new file adds no dependencies).

---
