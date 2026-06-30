# Implementation Plan: Authorization–UserManagement Module Boundary Fix

## Context

`GetEntraAccessUsersHandler` (Authorization module) currently injects `IGraphService`, an interface
that belongs to the UserManagement module. Cross-module communication must go exclusively through
`contracts/` directories. This plan introduces a consumer-owned contract in `Authorization/Contracts/`
and a provider-side adapter in `UserManagement/Infrastructure/`, mirroring the existing
`IArticleUserResolver` / `GraphArticleUserResolver` pattern. No functional change.

## File Map

| Action | Path | Responsibility |
|--------|------|----------------|
| **Create** | `backend/src/Anela.Heblo.Application/Features/Authorization/Contracts/IEntraAccessUserSource.cs` | Consumer-owned interface + record that the Authorization module depends on |
| **Create** | `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/EntraAccessUserSourceAdapter.cs` | Provider-side adapter: wraps `IGraphService`, implements `IEntraAccessUserSource` |
| **Modify** | `backend/src/Anela.Heblo.Application/Features/UserManagement/UserManagementModule.cs` | Register the adapter for DI |
| **Modify** | `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetEntraAccessUsers/GetEntraAccessUsersHandler.cs` | Swap `IGraphService` injection for `IEntraAccessUserSource` |
| **Modify** | `backend/test/Anela.Heblo.Tests/Authorization/GetEntraAccessUsersHandlerTests.cs` | Rewrite mocked dependency to use `IEntraAccessUserSource` / `EntraAccessUserRecord` |

---

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

### task: create-usermanagement-adapter

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/EntraAccessUserSourceAdapter.cs`

- [ ] **Step 1: Create the adapter.**

  The adapter is `internal sealed` — it must NOT be `public`. It lives in the `Infrastructure/`
  folder alongside `GraphArticleUserResolver`. It injects `IGraphService` (UserManagement's own
  interface) and maps the result to `EntraAccessUserRecord` (Authorization's contract type).
  No exception wrapping — see architecture decision in spec.

  ```csharp
  using Anela.Heblo.Application.Features.Authorization.Contracts;
  using Anela.Heblo.Application.Features.UserManagement.Services;
  using Anela.Heblo.Domain.Features.Authorization;

  namespace Anela.Heblo.Application.Features.UserManagement.Infrastructure;

  internal sealed class EntraAccessUserSourceAdapter : IEntraAccessUserSource
  {
      private readonly IGraphService _graph;

      public EntraAccessUserSourceAdapter(IGraphService graph) => _graph = graph;

      public async Task<List<EntraAccessUserRecord>> GetBaseMembersAsync(CancellationToken ct)
      {
          var users = await _graph.GetAppRoleMembersAsync(AccessRoles.Base, ct);
          return users
              .Select(u => new EntraAccessUserRecord(u.Id, u.Email, u.DisplayName))
              .ToList();
      }
  }
  ```

  Note on `AccessRoles.Base`: `AccessRoles` is a generated static class at
  `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs` in the
  `Anela.Heblo.Domain.Features.Authorization` namespace. The value `Base` was previously used
  directly in the handler — copy it here verbatim.

- [ ] **Step 2: Verify the file compiles.**

  ```bash
  dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
  ```

  Expected: build succeeds.

---

### task: register-adapter-in-di

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/UserManagement/UserManagementModule.cs`

- [ ] **Step 1: Add one `AddScoped` line after the existing `IArticleUserResolver` registration.**

  Current file content:

  ```csharp
  using Anela.Heblo.Application.Common.Behaviors;
  using Anela.Heblo.Application.Features.Article.Contracts;
  using Anela.Heblo.Application.Features.UserManagement.Infrastructure;
  using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;
  using Anela.Heblo.Application.Features.UserManagement.Validators;
  using FluentValidation;
  using MediatR;
  using Microsoft.Extensions.Configuration;
  using Microsoft.Extensions.DependencyInjection;

  namespace Anela.Heblo.Application.Features.UserManagement;

  public static class UserManagementModule
  {
      public static IServiceCollection AddUserManagement(this IServiceCollection services, IConfiguration configuration)
      {
          // IGraphService is registered by the adapter layer via AddMicrosoft365Adapter(), not here.

          // Cross-module: IArticleUserResolver delegates to IGraphService (Mock or real) from adapter layer.
          services.AddScoped<IArticleUserResolver, GraphArticleUserResolver>();

          services.AddScoped<IValidator<GetGroupMembersRequest>, GetGroupMembersRequestValidator>();
          services.AddScoped<
              IPipelineBehavior<GetGroupMembersRequest, GetGroupMembersResponse>,
              ValidationBehavior<GetGroupMembersRequest, GetGroupMembersResponse>>();

          // Note: HttpContextAccessor must be registered in the API layer

          return services;
      }
  }
  ```

  Updated file content — add one using and one `AddScoped` line:

  ```csharp
  using Anela.Heblo.Application.Common.Behaviors;
  using Anela.Heblo.Application.Features.Article.Contracts;
  using Anela.Heblo.Application.Features.Authorization.Contracts;
  using Anela.Heblo.Application.Features.UserManagement.Infrastructure;
  using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;
  using Anela.Heblo.Application.Features.UserManagement.Validators;
  using FluentValidation;
  using MediatR;
  using Microsoft.Extensions.Configuration;
  using Microsoft.Extensions.DependencyInjection;

  namespace Anela.Heblo.Application.Features.UserManagement;

  public static class UserManagementModule
  {
      public static IServiceCollection AddUserManagement(this IServiceCollection services, IConfiguration configuration)
      {
          // IGraphService is registered by the adapter layer via AddMicrosoft365Adapter(), not here.

          // Cross-module: IArticleUserResolver delegates to IGraphService (Mock or real) from adapter layer.
          services.AddScoped<IArticleUserResolver, GraphArticleUserResolver>();
          services.AddScoped<IEntraAccessUserSource, EntraAccessUserSourceAdapter>();

          services.AddScoped<IValidator<GetGroupMembersRequest>, GetGroupMembersRequestValidator>();
          services.AddScoped<
              IPipelineBehavior<GetGroupMembersRequest, GetGroupMembersResponse>,
              ValidationBehavior<GetGroupMembersRequest, GetGroupMembersResponse>>();

          // Note: HttpContextAccessor must be registered in the API layer

          return services;
      }
  }
  ```

- [ ] **Step 2: Verify the file compiles.**

  ```bash
  dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
  ```

  Expected: build succeeds.

---

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

### task: update-handler-tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Authorization/GetEntraAccessUsersHandlerTests.cs`

- [ ] **Step 1: Rewrite the test file to mock `IEntraAccessUserSource` instead of `IGraphService`.**

  Current file content:

  ```csharp
  using Anela.Heblo.Application.Features.Authorization.UseCases.GetEntraAccessUsers;
  using Anela.Heblo.Application.Features.UserManagement.Contracts;
  using Anela.Heblo.Application.Features.UserManagement.Services;
  using Anela.Heblo.Domain.Features.Authorization;
  using FluentAssertions;
  using Moq;
  using Xunit;

  namespace Anela.Heblo.Tests.Authorization;

  public class GetEntraAccessUsersHandlerTests
  {
      private static GetEntraAccessUsersHandler NewHandler(IGraphService graphService)
          => new(graphService);

      [Fact]
      public async Task Handle_ReturnsEntraUsersOrderedByDisplayName()
      {
          var mock = new Mock<IGraphService>();
          mock.Setup(g => g.GetAppRoleMembersAsync(AccessRoles.Base, default))
              .ReturnsAsync(new List<UserDto>
              {
                  new() { Id = "obj-2", DisplayName = "Zdenek Novak", Email = "z@x.cz" },
                  new() { Id = "obj-1", DisplayName = "Anna Novak", Email = "a@x.cz" },
              });

          var result = await NewHandler(mock.Object).Handle(new GetEntraAccessUsersRequest(), default);

          result.Success.Should().BeTrue();
          result.Users.Should().HaveCount(2);
          result.Users[0].DisplayName.Should().Be("Anna Novak");
          result.Users[0].EntraObjectId.Should().Be("obj-1");
          result.Users[1].DisplayName.Should().Be("Zdenek Novak");
      }

      [Fact]
      public async Task Handle_WhenGraphReturnsEmpty_ReturnsEmptyList()
      {
          var mock = new Mock<IGraphService>();
          mock.Setup(g => g.GetAppRoleMembersAsync(It.IsAny<string>(), default))
              .ReturnsAsync(new List<UserDto>());

          var result = await NewHandler(mock.Object).Handle(new GetEntraAccessUsersRequest(), default);

          result.Success.Should().BeTrue();
          result.Users.Should().BeEmpty();
      }
  }
  ```

  Updated file content:

  ```csharp
  using Anela.Heblo.Application.Features.Authorization.Contracts;
  using Anela.Heblo.Application.Features.Authorization.UseCases.GetEntraAccessUsers;
  using FluentAssertions;
  using Moq;
  using Xunit;

  namespace Anela.Heblo.Tests.Authorization;

  public class GetEntraAccessUsersHandlerTests
  {
      private static GetEntraAccessUsersHandler NewHandler(IEntraAccessUserSource source)
          => new(source);

      [Fact]
      public async Task Handle_ReturnsEntraUsersOrderedByDisplayName()
      {
          var mock = new Mock<IEntraAccessUserSource>();
          mock.Setup(s => s.GetBaseMembersAsync(default))
              .ReturnsAsync(new List<EntraAccessUserRecord>
              {
                  new("obj-2", "z@x.cz", "Zdenek Novak"),
                  new("obj-1", "a@x.cz", "Anna Novak"),
              });

          var result = await NewHandler(mock.Object).Handle(new GetEntraAccessUsersRequest(), default);

          result.Success.Should().BeTrue();
          result.Users.Should().HaveCount(2);
          result.Users[0].DisplayName.Should().Be("Anna Novak");
          result.Users[0].EntraObjectId.Should().Be("obj-1");
          result.Users[1].DisplayName.Should().Be("Zdenek Novak");
      }

      [Fact]
      public async Task Handle_WhenSourceReturnsEmpty_ReturnsEmptyList()
      {
          var mock = new Mock<IEntraAccessUserSource>();
          mock.Setup(s => s.GetBaseMembersAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<EntraAccessUserRecord>());

          var result = await NewHandler(mock.Object).Handle(new GetEntraAccessUsersRequest(), default);

          result.Success.Should().BeTrue();
          result.Users.Should().BeEmpty();
      }
  }
  ```

  Key changes:
  - Remove usings for `UserManagement.Contracts`, `UserManagement.Services`, `Domain.Features.Authorization`
  - Add using for `Authorization.Contracts`
  - `NewHandler` parameter type: `IGraphService` → `IEntraAccessUserSource`
  - `Mock<IGraphService>` → `Mock<IEntraAccessUserSource>`
  - Setup method: `g.GetAppRoleMembersAsync(AccessRoles.Base, default)` → `s.GetBaseMembersAsync(default)`
  - Test data: `List<UserDto> { new() { ... } }` → `List<EntraAccessUserRecord> { new("id", "email", "name") }`
    - `EntraAccessUserRecord` constructor parameter order is `(Id, Email, DisplayName)` — match the record declaration exactly
  - Empty-list test: `It.IsAny<string>()` arg removed (method takes only `CancellationToken`); use `It.IsAny<CancellationToken>()` for robustness
  - Assertions are unchanged — they test handler output (`EntraUserDto`), not mock input

- [ ] **Step 2: Run the Authorization tests.**

  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "Authorization"
  ```

  Expected: 2 tests pass, 0 fail.

---

### task: verify

**Files:** (none — verification only)

- [ ] **Step 1: Full build.**

  ```bash
  dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
  ```

  Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 2: Authorization tests.**

  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "Authorization"
  ```

  Expected: `Passed! - Failed: 0, Passed: 2, Skipped: 0`.

- [ ] **Step 3: Confirm module boundary is clean.**

  ```bash
  grep -r "UserManagement.Services" backend/src/Anela.Heblo.Application/Features/Authorization/ || echo "CLEAN"
  ```

  Expected output: `CLEAN` (no matches — the violation is gone).
