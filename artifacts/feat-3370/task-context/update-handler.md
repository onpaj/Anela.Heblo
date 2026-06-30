### task: update-handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetEntraAccessUsers/GetEntraAccessUsersHandler.cs`

- [ ] **Step 1: Replace the handler's dependency from `IGraphService` to `IEntraAccessUserSource`.**

  Current file content:

  ```csharp
  using Anela.Heblo.Application.Features.UserManagement.Services;
  using Anela.Heblo.Domain.Features.Authorization;
  using MediatR;

  namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetEntraAccessUsers;

  public class GetEntraAccessUsersHandler : IRequestHandler<GetEntraAccessUsersRequest, GetEntraAccessUsersResponse>
  {
      private readonly IGraphService _graphService;

      public GetEntraAccessUsersHandler(IGraphService graphService) => _graphService = graphService;

      public async Task<GetEntraAccessUsersResponse> Handle(GetEntraAccessUsersRequest request, CancellationToken ct)
      {
          var users = await _graphService.GetAppRoleMembersAsync(AccessRoles.Base, ct);
          return new GetEntraAccessUsersResponse
          {
              Users = users.Select(u => new EntraUserDto
              {
                  EntraObjectId = u.Id,
                  Email = u.Email,
                  DisplayName = u.DisplayName,
              }).OrderBy(u => u.DisplayName).ToList(),
          };
      }
  }
  ```

  Updated file content:

  ```csharp
  using Anela.Heblo.Application.Features.Authorization.Contracts;
  using MediatR;

  namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetEntraAccessUsers;

  public class GetEntraAccessUsersHandler : IRequestHandler<GetEntraAccessUsersRequest, GetEntraAccessUsersResponse>
  {
      private readonly IEntraAccessUserSource _source;

      public GetEntraAccessUsersHandler(IEntraAccessUserSource source) => _source = source;

      public async Task<GetEntraAccessUsersResponse> Handle(GetEntraAccessUsersRequest request, CancellationToken ct)
      {
          var users = await _source.GetBaseMembersAsync(ct);
          return new GetEntraAccessUsersResponse
          {
              Users = users.Select(u => new EntraUserDto
              {
                  EntraObjectId = u.Id,
                  Email = u.Email,
                  DisplayName = u.DisplayName,
              }).OrderBy(u => u.DisplayName).ToList(),
          };
      }
  }
  ```

  Key changes:
  - Remove `using Anela.Heblo.Application.Features.UserManagement.Services;` (eliminates the cross-module violation)
  - Remove `using Anela.Heblo.Domain.Features.Authorization;` (`AccessRoles.Base` is now only used inside the adapter)
  - Field renamed `_graphService` → `_source` for accuracy
  - Call site: `_graphService.GetAppRoleMembersAsync(AccessRoles.Base, ct)` → `_source.GetBaseMembersAsync(ct)`
  - The mapping of `Id`, `Email`, `DisplayName` is unchanged; the ordering by `DisplayName` is unchanged

- [ ] **Step 2: Verify the file compiles.**

  ```bash
  dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
  ```

  Expected: build succeeds with no warnings about missing usings.

---
