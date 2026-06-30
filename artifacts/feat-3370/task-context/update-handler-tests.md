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
