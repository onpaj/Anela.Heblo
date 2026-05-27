# Relocate GetGroupMembers Endpoint to UserManagementController — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the `GET /responsible-persons` endpoint out of `ManufactureOrderController` into a new `UserManagementController`, expose `ManufactureGroupId` via `ConfigurationController`, and update all callers (frontend hook+combobox, MCP tool, MCP docs, tests) accordingly.

**Architecture:** Backend: new thin `UserManagementController` delegates to existing `GetGroupMembersHandler` via MediatR; `IConfiguration` is dropped from `ManufactureOrderController`; `GetConfigurationHandler` exposes a new nullable `ManufactureGroupId` field; MCP tool relocates from `ManufactureOrderMcpTools` to a new `UserManagementMcpTools`. Frontend: new `useConfigurationQuery` hook (React Query, `staleTime: Infinity`) replaces ad-hoc config fetches; `useResponsiblePersonsQuery` takes `groupId`; `ResponsiblePersonCombobox` adds a required `groupId` prop; three manufacture call sites pass the value through.

**Tech Stack:** .NET 8 (xUnit + Moq + FluentAssertions), MediatR, ASP.NET Core, React 18 + TypeScript, TanStack Query v5, Jest + React Testing Library.

---

## File Structure

**Backend — new files:**
- `backend/src/Anela.Heblo.API/Controllers/UserManagementController.cs`
- `backend/src/Anela.Heblo.API/MCP/Tools/UserManagementMcpTools.cs`
- `backend/test/Anela.Heblo.Tests/Controllers/UserManagementControllerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/MCP/Tools/UserManagementMcpToolsTests.cs`

**Backend — modified files:**
- `backend/src/Anela.Heblo.API/Controllers/ManufactureOrderController.cs` — delete `GetResponsiblePersons`, drop `IConfiguration`, drop UserManagement using
- `backend/src/Anela.Heblo.API/MCP/Tools/ManufactureOrderMcpTools.cs` — delete `GetResponsiblePersons`, drop UserManagement using
- `backend/src/Anela.Heblo.API/MCP/McpModule.cs` — register `UserManagementMcpTools`
- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationResponse.cs` — add `ManufactureGroupId`
- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs` — populate `ManufactureGroupId`
- `backend/test/Anela.Heblo.Tests/Controllers/ManufactureOrderControllerTests.cs` — delete `GetResponsiblePersons` region, drop `IConfiguration` mock
- `backend/test/Anela.Heblo.Tests/MCP/Tools/ManufactureOrderMcpToolsTests.cs` — delete `GetResponsiblePersons*` tests
- `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationEndpointTests.cs` — assert presence of `ManufactureGroupId`

**Frontend — new files:**
- `frontend/src/api/hooks/useConfiguration.ts`

**Frontend — modified files:**
- `frontend/src/api/hooks/useUserManagement.ts` — accept `groupId`, change URL, change query key
- `frontend/src/components/common/ResponsiblePersonCombobox.tsx` — accept required `groupId` prop
- `frontend/src/components/common/__tests__/ResponsiblePersonCombobox.test.tsx` — pass `groupId` in tests
- `frontend/src/components/modals/CreateManufactureOrderModal.tsx` — read `manufactureGroupId`, pass to combobox
- `frontend/src/components/manufacture/detail/BasicInfoSection.tsx` — same
- `frontend/src/components/manufacture/list/ManufactureOrderFilters.tsx` — same

**Docs — modified files:**
- `docs/integrations/mcp-server.md` — section counts + new "User Management (1)" section

---

## Task 1: Extend `GetConfigurationResponse` with `ManufactureGroupId`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationResponse.cs`

- [ ] **Step 1: Add the nullable property**

Replace the file contents with:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Configuration;

/// <summary>
/// Response containing application configuration information
/// </summary>
public class GetConfigurationResponse : BaseResponse
{
    /// <summary>
    /// Application version from CI/CD pipeline or assembly
    /// </summary>
    public string Version { get; set; } = default!;

    /// <summary>
    /// Current environment (Development, Test, Production)
    /// </summary>
    public string Environment { get; set; } = default!;

    /// <summary>
    /// Whether mock authentication is enabled
    /// </summary>
    public bool UseMockAuth { get; set; }

    /// <summary>
    /// Response timestamp in UTC
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Microsoft Entra group ID used for manufacture responsible-person lookups.
    /// Null when the "ManufactureGroupId" configuration key is missing or empty.
    /// </summary>
    public string? ManufactureGroupId { get; set; }
}
```

- [ ] **Step 2: Verify build still succeeds**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: BUILD SUCCESS, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationResponse.cs
git commit -m "feat: add nullable ManufactureGroupId to GetConfigurationResponse"
```

---

## Task 2: Populate `ManufactureGroupId` in `GetConfigurationHandler` (TDD)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Configuration;

public class GetConfigurationHandlerTests
{
    private readonly Mock<IConfiguration> _configurationMock = new();
    private readonly Mock<IHostEnvironment> _environmentMock = new();
    private readonly Mock<ILogger<GetConfigurationHandler>> _loggerMock = new();

    private GetConfigurationHandler CreateHandler()
    {
        _environmentMock.SetupGet(e => e.EnvironmentName).Returns("Test");
        return new GetConfigurationHandler(
            _configurationMock.Object,
            _environmentMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WhenManufactureGroupIdConfigured_ReturnsValueInResponse()
    {
        // Arrange
        var configuredGroupId = "11111111-2222-3333-4444-555555555555";
        _configurationMock.Setup(c => c["ManufactureGroupId"]).Returns(configuredGroupId);

        var handler = CreateHandler();

        // Act
        var response = await handler.Handle(new GetConfigurationRequest(), CancellationToken.None);

        // Assert
        response.ManufactureGroupId.Should().Be(configuredGroupId);
    }

    [Fact]
    public async Task Handle_WhenManufactureGroupIdMissing_ReturnsNull()
    {
        // Arrange
        _configurationMock.Setup(c => c["ManufactureGroupId"]).Returns((string?)null);

        var handler = CreateHandler();

        // Act
        var response = await handler.Handle(new GetConfigurationRequest(), CancellationToken.None);

        // Assert
        response.ManufactureGroupId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenManufactureGroupIdEmpty_ReturnsNull()
    {
        // Arrange
        _configurationMock.Setup(c => c["ManufactureGroupId"]).Returns(string.Empty);

        var handler = CreateHandler();

        // Act
        var response = await handler.Handle(new GetConfigurationRequest(), CancellationToken.None);

        // Assert
        response.ManufactureGroupId.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetConfigurationHandlerTests"`
Expected: 3 FAIL (`ManufactureGroupId` is always null because handler does not read it yet).

- [ ] **Step 3: Update the handler to populate the field**

In `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`, replace the response creation block (lines 34–40) with:

```csharp
            var manufactureGroupId = _configuration["ManufactureGroupId"];
            if (string.IsNullOrEmpty(manufactureGroupId))
            {
                manufactureGroupId = null;
            }

            var response = new GetConfigurationResponse
            {
                Version = appConfig.Version,
                Environment = appConfig.Environment,
                UseMockAuth = appConfig.UseMockAuth,
                Timestamp = appConfig.Timestamp,
                ManufactureGroupId = manufactureGroupId
            };
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetConfigurationHandlerTests"`
Expected: 3 PASS.

- [ ] **Step 5: Extend the integration test to assert presence of the field**

In `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationEndpointTests.cs`, append a new test inside the class (before the closing `}`):

```csharp
    [Fact]
    public async Task GetConfiguration_ShouldExposeManufactureGroupIdField()
    {
        // Act
        var response = await _client.GetAsync("/api/configuration");
        var configResponse = await response.Content.ReadFromJsonAsync<GetConfigurationResponse>();

        // Assert
        configResponse.Should().NotBeNull();
        // Field must exist on the response shape; value is allowed to be null when key is unset in Test config.
        var hasProperty = typeof(GetConfigurationResponse)
            .GetProperty(nameof(GetConfigurationResponse.ManufactureGroupId)) != null;
        hasProperty.Should().BeTrue();
    }
```

- [ ] **Step 6: Run the integration test**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetConfigurationEndpointTests"`
Expected: all green (5 tests).

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationEndpointTests.cs
git commit -m "feat: expose ManufactureGroupId via GetConfigurationHandler"
```

---

## Task 3: Create `UserManagementController` with `GetGroupMembers` endpoint (TDD)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Controllers/UserManagementControllerTests.cs`
- Create: `backend/src/Anela.Heblo.API/Controllers/UserManagementController.cs`

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/Controllers/UserManagementControllerTests.cs`:

```csharp
using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Controllers;

public class UserManagementControllerTests
{
    private readonly Mock<IMediator> _mediatorMock = new();
    private readonly UserManagementController _controller;

    public UserManagementControllerTests()
    {
        _controller = new UserManagementController(_mediatorMock.Object);

        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = sp };
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    [Fact]
    public async Task GetGroupMembers_ReturnsOk_WithMembers_OnSuccessfulHandlerResponse()
    {
        // Arrange
        var groupId = "group-abc";
        var handlerResponse = new GetGroupMembersResponse
        {
            Success = true,
            Members = new List<UserDto>
            {
                new() { Id = "1", DisplayName = "Alice", Email = "alice@anela.cz" },
                new() { Id = "2", DisplayName = "Bob",   Email = "bob@anela.cz" }
            }
        };

        _mediatorMock
            .Setup(m => m.Send(It.Is<GetGroupMembersRequest>(r => r.GroupId == groupId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(handlerResponse);

        // Act
        var result = await _controller.GetGroupMembers(groupId, CancellationToken.None);

        // Assert
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<GetGroupMembersResponse>().Subject;
        payload.Members.Should().HaveCount(2);
        payload.Members[0].Email.Should().Be("alice@anela.cz");

        _mediatorMock.Verify(
            m => m.Send(It.Is<GetGroupMembersRequest>(r => r.GroupId == groupId), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetGroupMembers_ReturnsHandlerFailure_ThroughHandleResponse()
    {
        // Arrange
        var groupId = "group-xyz";
        var failed = new GetGroupMembersResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.InternalServerError,
            Members = new List<UserDto>()
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetGroupMembersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failed);

        // Act
        var result = await _controller.GetGroupMembers(groupId, CancellationToken.None);

        // Assert — InternalServerError maps to 500 via BaseApiController.HandleResponse
        var status = result.Result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task GetGroupMembers_DelegatesToMediator_WithoutAnyControllerSideValidation()
    {
        // The [ApiController]+[Required] short-circuit is enforced by the MVC framework
        // before the action runs. This test exists to lock the contract that the action
        // body itself is only: build request → Send → HandleResponse — no manual logging,
        // no manual string.IsNullOrEmpty check, no IConfiguration access.

        // Arrange
        var handlerResponse = new GetGroupMembersResponse { Success = true };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetGroupMembersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(handlerResponse);

        // Act
        await _controller.GetGroupMembers("any-id", CancellationToken.None);

        // Assert
        _mediatorMock.Verify(
            m => m.Send(It.Is<GetGroupMembersRequest>(r => r.GroupId == "any-id"), It.IsAny<CancellationToken>()),
            Times.Once);
        _mediatorMock.VerifyNoOtherCalls();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UserManagementControllerTests"`
Expected: COMPILATION ERROR (`UserManagementController` does not exist).

- [ ] **Step 3: Create the controller**

Create `backend/src/Anela.Heblo.API/Controllers/UserManagementController.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class UserManagementController : BaseApiController
{
    private readonly IMediator _mediator;

    public UserManagementController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get members of a Microsoft Entra ID group.
    /// </summary>
    [HttpGet("group-members")]
    [ProducesResponseType(typeof(GetGroupMembersResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<GetGroupMembersResponse>> GetGroupMembers(
        [FromQuery, Required] string groupId,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new GetGroupMembersRequest { GroupId = groupId }, cancellationToken);
        return HandleResponse(response);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UserManagementControllerTests"`
Expected: 3 PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/UserManagementController.cs backend/test/Anela.Heblo.Tests/Controllers/UserManagementControllerTests.cs
git commit -m "feat: add UserManagementController with GET group-members endpoint"
```

---

## Task 4: Remove `GetResponsiblePersons` and `IConfiguration` from `ManufactureOrderController`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/ManufactureOrderController.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Controllers/ManufactureOrderControllerTests.cs`

- [ ] **Step 1: Delete the action from the controller**

In `backend/src/Anela.Heblo.API/Controllers/ManufactureOrderController.cs`:

1. Delete lines 149–192 (the entire `/// <summary> ... </summary>` block plus the `GetResponsiblePersons` method).
2. Delete line 12: `using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;`
3. Delete line 17: `using Microsoft.Extensions.Configuration;`
4. Delete line 27: `private readonly IConfiguration _configuration;`
5. Change the constructor (currently lines 29–35) to:

```csharp
    public ManufactureOrderController(IMediator mediator)
    {
        _mediator = mediator;
    }
```

- [ ] **Step 2: Verify nothing else in the file references `_configuration` or the deleted using**

Run: `grep -n "_configuration\|UserManagement.UseCases.GetGroupMembers\|Microsoft.Extensions.Configuration" backend/src/Anela.Heblo.API/Controllers/ManufactureOrderController.cs`
Expected: NO MATCHES.

- [ ] **Step 3: Update the controller tests — drop the IConfiguration mock and the GetResponsiblePersons region**

In `backend/test/Anela.Heblo.Tests/Controllers/ManufactureOrderControllerTests.cs`:

1. Delete line 10: `using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;`
2. Delete line 11: `using Anela.Heblo.Application.Features.UserManagement.Contracts;`
3. Delete line 18: `using Microsoft.Extensions.Configuration;`
4. Delete line 33: `private readonly Mock<IConfiguration> _configurationMock;`
5. Delete line 39: `_configurationMock = new Mock<IConfiguration>();`
6. Change line 40 from:
   ```csharp
   _controller = new ManufactureOrderController(_mediatorMock.Object, _configurationMock.Object);
   ```
   to:
   ```csharp
   _controller = new ManufactureOrderController(_mediatorMock.Object);
   ```
7. Delete the entire `#region GetResponsiblePersons Tests ... #endregion` block (lines 615–727 — four tests plus the region markers).

- [ ] **Step 4: Build and run the controller tests**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: BUILD SUCCESS.

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ManufactureOrderControllerTests"`
Expected: all remaining tests PASS, four GetResponsiblePersons tests no longer present.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/ManufactureOrderController.cs backend/test/Anela.Heblo.Tests/Controllers/ManufactureOrderControllerTests.cs
git commit -m "refactor: remove GetResponsiblePersons and IConfiguration from ManufactureOrderController"
```

---

## Task 5: Create `UserManagementMcpTools` and register it (TDD)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/MCP/Tools/UserManagementMcpToolsTests.cs`
- Create: `backend/src/Anela.Heblo.API/MCP/Tools/UserManagementMcpTools.cs`
- Modify: `backend/src/Anela.Heblo.API/MCP/McpModule.cs`

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/MCP/Tools/UserManagementMcpToolsTests.cs`:

```csharp
using System.Text.Json;
using Anela.Heblo.API.MCP.Tools;
using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;
using MediatR;
using ModelContextProtocol;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP.Tools;

public class UserManagementMcpToolsTests
{
    private readonly Mock<IMediator> _mediatorMock = new();
    private readonly UserManagementMcpTools _tools;

    public UserManagementMcpToolsTests()
    {
        _tools = new UserManagementMcpTools(_mediatorMock.Object);
    }

    [Fact]
    public async Task GetGroupMembers_ShouldMapParametersCorrectly()
    {
        // Arrange
        var expected = new GetGroupMembersResponse { Success = true };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetGroupMembersRequest>(), default))
            .ReturnsAsync(expected);

        // Act
        var json = await _tools.GetGroupMembers("group-id-123");

        // Assert
        _mediatorMock.Verify(
            m => m.Send(It.Is<GetGroupMembersRequest>(r => r.GroupId == "group-id-123"), default),
            Times.Once);

        var deserialized = JsonSerializer.Deserialize<GetGroupMembersResponse>(json);
        Assert.NotNull(deserialized);
        Assert.True(deserialized!.Success);
    }

    [Fact]
    public async Task GetGroupMembers_ShouldThrowMcpException_WhenExternalServiceFails()
    {
        // Arrange
        var failed = new GetGroupMembersResponse
        {
            Success = false,
            ErrorCode = Anela.Heblo.Application.Shared.ErrorCodes.ExternalServiceError,
            Params = new Dictionary<string, string> { { "GroupId", "group-id-999" } }
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetGroupMembersRequest>(), default))
            .ReturnsAsync(failed);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<McpException>(() => _tools.GetGroupMembers("group-id-999"));
        Assert.Contains("ExternalServiceError", ex.Message);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UserManagementMcpToolsTests"`
Expected: COMPILATION ERROR (`UserManagementMcpTools` does not exist).

- [ ] **Step 3: Create the MCP tool class**

Create `backend/src/Anela.Heblo.API/MCP/Tools/UserManagementMcpTools.cs`:

```csharp
using System.ComponentModel;
using System.Text.Json;
using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;
using MediatR;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Anela.Heblo.API.MCP.Tools;

/// <summary>
/// MCP tools for user-directory lookups against Microsoft Entra ID.
/// </summary>
[McpServerToolType]
public class UserManagementMcpTools
{
    private readonly IMediator _mediator;

    public UserManagementMcpTools(IMediator mediator)
    {
        _mediator = mediator;
    }

    [McpServerTool]
    public async Task<string> GetGroupMembers(
        [Description("Microsoft Entra ID group ID to fetch members for")]
        string groupId,
        CancellationToken cancellationToken = default
    )
    {
        var request = new GetGroupMembersRequest { GroupId = groupId };
        var response = await _mediator.Send(request, cancellationToken);

        if (!response.Success)
        {
            throw new McpException($"[{response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR"}] {response.FullError()}");
        }

        return JsonSerializer.Serialize(response);
    }
}
```

- [ ] **Step 4: Register the tool class with the MCP server**

In `backend/src/Anela.Heblo.API/MCP/McpModule.cs`, replace lines 15–21 (the `services.AddMcpServer()...` chain) with:

```csharp
        services.AddMcpServer()
            .WithHttpTransport()
            .WithTools<CatalogMcpTools>()
            .WithTools<ManufactureOrderMcpTools>()
            .WithTools<ManufactureBatchMcpTools>()
            .WithTools<KnowledgeBaseTools>()
            .WithTools<LeafletTools>()
            .WithTools<UserManagementMcpTools>();
```

- [ ] **Step 5: Run the new tool tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UserManagementMcpToolsTests"`
Expected: 2 PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.API/MCP/Tools/UserManagementMcpTools.cs backend/src/Anela.Heblo.API/MCP/McpModule.cs backend/test/Anela.Heblo.Tests/MCP/Tools/UserManagementMcpToolsTests.cs
git commit -m "feat: add UserManagementMcpTools.GetGroupMembers and register with MCP server"
```

---

## Task 6: Remove `GetResponsiblePersons` from `ManufactureOrderMcpTools` and its tests

**Files:**
- Modify: `backend/src/Anela.Heblo.API/MCP/Tools/ManufactureOrderMcpTools.cs`
- Modify: `backend/test/Anela.Heblo.Tests/MCP/Tools/ManufactureOrderMcpToolsTests.cs`

- [ ] **Step 1: Remove the tool method and unused using**

In `backend/src/Anela.Heblo.API/MCP/Tools/ManufactureOrderMcpTools.cs`:

1. Delete line 6: `using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;`
2. Delete lines 73–89 (the `[McpServerTool] public async Task<string> GetResponsiblePersons(...)` method).

- [ ] **Step 2: Remove the related tests**

In `backend/test/Anela.Heblo.Tests/MCP/Tools/ManufactureOrderMcpToolsTests.cs`:

1. Delete line 6: `using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;`
2. Delete the two `GetResponsiblePersons_*` tests (lines 166–211).

- [ ] **Step 3: Verify build and run remaining tests**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: BUILD SUCCESS.

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ManufactureOrderMcpToolsTests"`
Expected: all remaining (5) tests PASS.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.API/MCP/Tools/ManufactureOrderMcpTools.cs backend/test/Anela.Heblo.Tests/MCP/Tools/ManufactureOrderMcpToolsTests.cs
git commit -m "refactor: remove GetResponsiblePersons from ManufactureOrderMcpTools"
```

---

## Task 7: Backend full build, format, and full test pass

**Files:** none (verification gate).

- [ ] **Step 1: Run dotnet format**

Run: `dotnet format backend/Anela.Heblo.sln`
Expected: no errors, files may be reformatted.

- [ ] **Step 2: Run full backend build**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: BUILD SUCCESS, 0 errors, 0 warnings introduced by these changes.

- [ ] **Step 3: Run the full backend test suite**

Run: `dotnet test backend/Anela.Heblo.sln`
Expected: all tests PASS.

- [ ] **Step 4: Commit any formatting changes**

```bash
git status
# Only commit if dotnet format produced changes:
git add -A
git commit -m "chore: dotnet format" || echo "no formatting changes"
```

---

## Task 8: Update MCP documentation

**Files:**
- Modify: `docs/integrations/mcp-server.md`

- [ ] **Step 1: Update the intro and tool inventory**

In `docs/integrations/mcp-server.md`:

1. Line 3 — replace:
   ```
   The application exposes MCP tools for AI assistants to query catalog data, manufacturing orders, and perform batch calculations.
   ```
   with:
   ```
   The application exposes MCP tools for AI assistants to query catalog data, manufacturing orders, perform batch calculations, and user-directory lookups.
   ```

2. Line 16 — change `**Manufacture Orders (4)**` to `**Manufacture Orders (3)**`.

3. Delete line 20 (the bullet ``- `GetResponsiblePersons` — responsible persons from Entra ID``).

4. Insert a new section between the "Manufacture Batch (4)" section and the "Knowledge Base (2)" section:
   ```
   **User Management (1)**
   - `GetGroupMembers` — Entra ID group members by group ID

   ```

- [ ] **Step 2: Commit**

```bash
git add docs/integrations/mcp-server.md
git commit -m "docs: update MCP tool inventory for UserManagement.GetGroupMembers"
```

---

## Task 9: Regenerate the frontend OpenAPI client

**Files:**
- Regenerates: `frontend/src/api/generated/api-client.ts`

- [ ] **Step 1: Build the frontend to trigger OpenAPI client regeneration**

Run: `cd frontend && npm run build`
Expected: build succeeds; `frontend/src/api/generated/api-client.ts` is regenerated with the new `manufactureGroupId` field on `GetConfigurationResponse` and **without** the `manufactureOrder_GetResponsiblePersons` method.

- [ ] **Step 2: Verify the changes**

Run: `grep -n "manufactureGroupId\|manufactureOrder_GetResponsiblePersons" frontend/src/api/generated/api-client.ts`
Expected: at least one match for `manufactureGroupId`; NO matches for `manufactureOrder_GetResponsiblePersons`.

- [ ] **Step 3: Verify no other frontend code references the removed generated method**

Run: `grep -rn "manufactureOrder_GetResponsiblePersons" frontend/src/`
Expected: NO matches (raw fetch in `useUserManagement.ts` is the only consumer of this endpoint, and it builds the URL by hand).

- [ ] **Step 4: Commit the regenerated client**

```bash
git add frontend/src/api/generated/api-client.ts
git commit -m "chore: regenerate OpenAPI client (drops GetResponsiblePersons, adds manufactureGroupId)"
```

---

## Task 10: Add `useConfigurationQuery` hook

**Files:**
- Create: `frontend/src/api/hooks/useConfiguration.ts`

- [ ] **Step 1: Create the hook**

Create `frontend/src/api/hooks/useConfiguration.ts`:

```typescript
import { useQuery, UseQueryResult } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';
import { GetConfigurationResponse } from '../generated/api-client';

const CONFIGURATION_QUERY_KEY = ['configuration'] as const;

/**
 * Fetches the application configuration payload exactly once per session
 * and shares the result across all consumers via the React Query cache.
 *
 * Uses staleTime/gcTime = Infinity because the values returned by
 * /api/Configuration do not change for the lifetime of the SPA.
 */
export const useConfigurationQuery = (): UseQueryResult<GetConfigurationResponse> =>
  useQuery({
    queryKey: CONFIGURATION_QUERY_KEY,
    queryFn: async () => {
      const apiClient = await getAuthenticatedApiClient();
      return apiClient.configuration_GetConfiguration();
    },
    staleTime: Infinity,
    gcTime: Infinity,
    retry: 1,
  });
```

- [ ] **Step 2: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit`
Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/api/hooks/useConfiguration.ts
git commit -m "feat: add useConfigurationQuery hook with staleTime: Infinity"
```

---

## Task 11: Update `useResponsiblePersonsQuery` to take `groupId`

**Files:**
- Modify: `frontend/src/api/hooks/useUserManagement.ts`

- [ ] **Step 1: Replace the hook implementation**

Replace the contents of `frontend/src/api/hooks/useUserManagement.ts` with:

```typescript
import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

export interface UserDto {
  id: string;
  displayName: string;
  email: string;
}

export interface GetGroupMembersResponse {
  success: boolean;
  errorCode?: number;
  params?: Record<string, string>;
  members: UserDto[];
}

export const useResponsiblePersonsQuery = (groupId: string) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.userManagement, 'group-members', groupId],
    enabled: Boolean(groupId),
    queryFn: async (): Promise<GetGroupMembersResponse> => {
      const apiClient = await getAuthenticatedApiClient();
      const relativeUrl = `/api/UserManagement/group-members?groupId=${encodeURIComponent(groupId)}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
        headers: { 'Content-Type': 'application/json' },
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      return response.json();
    },
    staleTime: 15 * 60 * 1000, // 15 minutes cache
    retry: 2,
    retryDelay: 1000,
  });
};
```

- [ ] **Step 2: Verify TypeScript compiles standalone**

Run: `cd frontend && npx tsc --noEmit src/api/hooks/useUserManagement.ts`
Expected: file compiles in isolation. (A full project `tsc --noEmit` will fail until callers are updated — that is expected and addressed in Task 12.)

- [ ] **Step 3: Commit**

```bash
git add frontend/src/api/hooks/useUserManagement.ts
git commit -m "refactor: accept groupId in useResponsiblePersonsQuery and call new URL"
```

---

## Task 12: Add required `groupId` prop to `ResponsiblePersonCombobox`

**Files:**
- Modify: `frontend/src/components/common/ResponsiblePersonCombobox.tsx`
- Modify: `frontend/src/components/common/__tests__/ResponsiblePersonCombobox.test.tsx`

- [ ] **Step 1: Add `groupId` to the props and disable behaviour**

In `frontend/src/components/common/ResponsiblePersonCombobox.tsx`:

1. Replace the `ResponsiblePersonComboboxProps` interface (lines 13–21) with:

```tsx
interface ResponsiblePersonComboboxProps {
  groupId: string;                   // Microsoft Entra group ID to fetch members from; combobox stays disabled when empty
  value?: string | null;
  onChange: (value: string | null) => void;
  placeholder?: string;
  disabled?: boolean;
  error?: string;
  className?: string;
  allowManualEntry?: boolean;        // Allow typing custom values as fallback
}
```

2. Replace the component signature (line 31 onward, the destructuring) to include `groupId`:

```tsx
const ResponsiblePersonCombobox: React.FC<ResponsiblePersonComboboxProps> = ({
  groupId,
  value,
  onChange,
  placeholder = "Select responsible person...",
  disabled = false,
  error,
  className = "",
  allowManualEntry = true,
}) => {
```

3. Replace the `useResponsiblePersonsQuery` call (currently line 41) with:

```tsx
  const { data: response, isLoading, isError } = useResponsiblePersonsQuery(groupId);
```

4. Replace the `isDisabled={disabled}` prop on the `<Select>` (line 194) with:

```tsx
        isDisabled={disabled || !groupId}
```

- [ ] **Step 2: Update the component test to pass `groupId`**

In `frontend/src/components/common/__tests__/ResponsiblePersonCombobox.test.tsx`, update the `defaultProps` block (lines 33–37) to:

```tsx
    const defaultProps = {
        groupId: 'test-group-id',
        value: '',
        onChange: jest.fn(),
        placeholder: 'Select responsible person',
    };
```

- [ ] **Step 3: Run the combobox tests**

Run: `cd frontend && npx jest src/components/common/__tests__/ResponsiblePersonCombobox.test.tsx`
Expected: all 7 tests PASS.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/components/common/ResponsiblePersonCombobox.tsx frontend/src/components/common/__tests__/ResponsiblePersonCombobox.test.tsx
git commit -m "refactor: require groupId prop on ResponsiblePersonCombobox"
```

---

## Task 13: Wire `manufactureGroupId` through `CreateManufactureOrderModal`

**Files:**
- Modify: `frontend/src/components/modals/CreateManufactureOrderModal.tsx`

- [ ] **Step 1: Import the configuration hook**

In `frontend/src/components/modals/CreateManufactureOrderModal.tsx`, add this import alongside the existing imports near the top of the file (right after the `ResponsiblePersonCombobox` import on line 4):

```tsx
import { useConfigurationQuery } from "../../api/hooks/useConfiguration";
```

- [ ] **Step 2: Read `manufactureGroupId` inside the component**

Inside the component function body, near the top (alongside other hook calls), add:

```tsx
  const { data: appConfig } = useConfigurationQuery();
  const manufactureGroupId = appConfig?.manufactureGroupId ?? "";
```

- [ ] **Step 3: Pass `groupId` to the combobox**

Update the `<ResponsiblePersonCombobox ... />` usage (around line 147) so the first prop is `groupId`:

```tsx
              <ResponsiblePersonCombobox
                groupId={manufactureGroupId}
                value={responsiblePerson}
                onChange={(value) => setResponsiblePerson(value || "")}
```

- [ ] **Step 4: Type-check this file**

Run: `cd frontend && npx tsc --noEmit`
Expected: no errors for `CreateManufactureOrderModal.tsx` (other files may still error until Task 14 + Task 15 land — that is expected; final full pass happens in Task 16).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/modals/CreateManufactureOrderModal.tsx
git commit -m "feat: pass manufactureGroupId to ResponsiblePersonCombobox in CreateManufactureOrderModal"
```

---

## Task 14: Wire `manufactureGroupId` through `BasicInfoSection`

**Files:**
- Modify: `frontend/src/components/manufacture/detail/BasicInfoSection.tsx`

- [ ] **Step 1: Import the configuration hook**

In `frontend/src/components/manufacture/detail/BasicInfoSection.tsx`, add the import next to the existing `ResponsiblePersonCombobox` import (line 11):

```tsx
import { useConfigurationQuery } from "../../../api/hooks/useConfiguration";
```

- [ ] **Step 2: Read `manufactureGroupId` and pass it to the combobox**

Inside the component function body, add:

```tsx
  const { data: appConfig } = useConfigurationQuery();
  const manufactureGroupId = appConfig?.manufactureGroupId ?? "";
```

Then update the `<ResponsiblePersonCombobox ... />` usage (around line 76) so `groupId` is the first prop:

```tsx
              <ResponsiblePersonCombobox
                groupId={manufactureGroupId}
                value={editableResponsiblePerson}
                onChange={(value) => onResponsiblePersonChange(value)}
                placeholder="Vyberte..."
```

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/manufacture/detail/BasicInfoSection.tsx
git commit -m "feat: pass manufactureGroupId to ResponsiblePersonCombobox in BasicInfoSection"
```

---

## Task 15: Wire `manufactureGroupId` through `ManufactureOrderFilters`

**Files:**
- Modify: `frontend/src/components/manufacture/list/ManufactureOrderFilters.tsx`

- [ ] **Step 1: Import the configuration hook**

In `frontend/src/components/manufacture/list/ManufactureOrderFilters.tsx`, add the import next to the existing `ResponsiblePersonCombobox` import (line 12):

```tsx
import { useConfigurationQuery } from "../../../api/hooks/useConfiguration";
```

- [ ] **Step 2: Read `manufactureGroupId` and pass it to the combobox**

Inside the component function body, add:

```tsx
  const { data: appConfig } = useConfigurationQuery();
  const manufactureGroupId = appConfig?.manufactureGroupId ?? "";
```

Then update the `<ResponsiblePersonCombobox ... />` usage (around line 294) so `groupId` is the first prop:

```tsx
              <ResponsiblePersonCombobox
                groupId={manufactureGroupId}
                value={responsiblePersonInput}
                onChange={(value) => setResponsiblePersonInput(value || "")}
                placeholder="Odpovědná osoba"
```

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/manufacture/list/ManufactureOrderFilters.tsx
git commit -m "feat: pass manufactureGroupId to ResponsiblePersonCombobox in ManufactureOrderFilters"
```

---

## Task 16: Frontend full type-check, lint, build, and test pass

**Files:** none (verification gate).

- [ ] **Step 1: Type-check the whole frontend**

Run: `cd frontend && npx tsc --noEmit`
Expected: 0 errors.

- [ ] **Step 2: Lint**

Run: `cd frontend && npm run lint`
Expected: 0 errors.

- [ ] **Step 3: Build**

Run: `cd frontend && npm run build`
Expected: build succeeds.

- [ ] **Step 4: Run the impacted frontend tests**

Run: `cd frontend && npx jest src/components/common/__tests__/ResponsiblePersonCombobox.test.tsx`
Expected: all 7 PASS.

Run: `cd frontend && npm test -- --watchAll=false`
Expected: all tests PASS (no other tests should have been broken; this run is the safety net).

- [ ] **Step 5: Commit any incidental formatter/lint fixes**

```bash
git status
git diff
# If files changed:
git add -A
git commit -m "chore: lint/format follow-up" || echo "no follow-up changes"
```

---

## Task 17: Manual smoke verification in the browser

**Files:** none (manual verification — only required for the UI changes).

- [ ] **Step 1: Start backend + frontend locally**

Follow `docs/development/setup.md` to run backend (`https://localhost:5001`) and frontend (`http://localhost:3001`).

- [ ] **Step 2: Verify `/api/Configuration` exposes `manufactureGroupId`**

In a browser or with `curl`:

```bash
curl -sk https://localhost:5001/api/Configuration | jq
```

Expected: JSON contains a `manufactureGroupId` property (string or null).

- [ ] **Step 3: Open the manufacture flows and verify the responsible-person dropdown**

Verify in the browser, with `ManufactureGroupId` configured in `appsettings.Development.json`:

1. Navigate to the Manufacture Orders list. The "Odpovědná osoba" filter dropdown should open and load members.
2. Open the "Create manufacture order" modal. The "Odpovědná osoba" dropdown should populate.
3. Open a manufacture order detail. The responsible-person combobox should populate.

In DevTools → Network tab, confirm the request URL is `GET /api/UserManagement/group-members?groupId=...` and that **no** request hits `/api/ManufactureOrder/responsible-persons`.

- [ ] **Step 4 (optional): Verify the combobox stays disabled when `ManufactureGroupId` is unset**

Temporarily remove or blank out `ManufactureGroupId` in `backend/src/Anela.Heblo.API/appsettings.Development.json`, restart the backend, refresh the SPA: the combobox should be disabled and no network call to `/api/UserManagement/group-members` should fire. Restore the value before continuing.

- [ ] **Step 5: No commit needed**

Smoke verification only — no code changes.

---

## Self-Review Notes (recorded after writing this plan)

**Spec coverage map** (spec requirement → task):

- FR-1 (new `UserManagementController`) → Task 3
- FR-2 (remove endpoint + `IConfiguration` from `ManufactureOrderController`) → Task 4
- FR-3 (constructor-injected logger; handler already logs) → covered implicitly by Tasks 3 + 4 (no service-locator logger in new controller; handler unchanged)
- FR-4 (expose `ManufactureGroupId` via `ConfigurationController`) → Tasks 1 + 2
- FR-5 (`useResponsiblePersonsQuery(groupId)`) → Task 11
- FR-6 (combobox gains `groupId`, callers feed it from a central config hook) → Tasks 10, 12, 13, 14, 15
- FR-7 (move + rename MCP tool) → Tasks 5 + 6
- FR-8 (MCP docs update) → Task 8
- NFR-1 (perf preserved) → no per-request changes; same handler, same Graph SDK call; React Query cache preserved (Task 11)
- NFR-2 (security; spec amendment that ConfigurationController is anonymous) → addressed in Task 1 docstring and accepted per arch-review Amendment 1; group ID is non-sensitive
- NFR-3 (no backwards-compat aliasing — all callers updated in one PR) → Tasks 4, 6, 11–15
- NFR-4 (tests at or above current bar) → new test files in Tasks 2, 3, 5; deletions in Tasks 4, 6 are 1:1 replacements

**Arch-review amendments applied:**

1. Amendment 1 (anonymous ConfigurationController wording) — captured in the docstring added to `ManufactureGroupId` in Task 1 and explicitly accepted; no enforcement work required.
2. Amendment 2 (`UserManagementController` is `[Authorize]` itself) — implemented in Task 3.
3. Amendment 3 (introduce `useConfigurationQuery`) — Task 10.
4. Amendment 4 (`[Required]` → MVC `ValidationProblemDetails` 400, not `BaseResponse`-shaped) — implemented in Task 3; the third controller test locks the contract that the action body does **not** add manual `string.IsNullOrEmpty` checks.
5. Amendment 5 (test folder convention) — `backend/test/Anela.Heblo.Tests/Controllers/UserManagementControllerTests.cs` per Task 3.
6. Amendment 6 (drop `IConfiguration` ctor param + update test constructions) — Task 4.
7. Amendment 7 (optional handler success log) — intentionally **not included**; spec marked it nice-to-have and the rule "every changed line should trace directly to the request" applies.

**Risks tracked in arch-review:**

- Whitespace-only `groupId` — handler's existing try/catch funnels Graph SDK errors into a `BaseResponse.Success = false`, which `HandleResponse` maps to a clear status code (Task 3's second test covers this funnel). No additional code added; documented here.
- `useConfigurationQuery` vs `versionService` overlap — `versionService.checkVersion` keeps its existing call path; we do not refactor it (out of scope).
