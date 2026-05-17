# Meeting Access Gating Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Introduce three-level per-meeting access control (Private / Public / Restricted) with a `meeting_manager` gatekeeper role so that auto-ingested meeting transcripts are not visible to all authenticated users by default.

**Architecture:** Access is enforced at the handler level via an `IMeetingAccessGuard` service — no MediatR pipeline behavior, since the background ingest job runs without an HttpContext. The repository's `GetListAsync` gains an `isManager + userEmail` parameter so pagination counts reflect only visible meetings. Existing meetings default to `Private` via the migration's column default.

**Tech Stack:** .NET 8, MediatR, EF Core + PostgreSQL, xUnit + FluentAssertions + Moq + EF InMemory (tests), React 18 + TanStack Query + react-select (frontend).

---

## File Map

### Backend — New Files
| File | Purpose |
|------|---------|
| `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingAccessLevel.cs` | Enum: Private / Public / Restricted |
| `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingAccessGrant.cs` | Entity: per-meeting user grant |
| `backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingAccessGrantConfiguration.cs` | EF config for `MeetingAccessGrants` table |
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IMeetingAccessGuard.cs` | Interface: IsManager / CanAccess |
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/MeetingAccessGuard.cs` | Implementation of IMeetingAccessGuard |
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/MeetingAccessGrantDto.cs` | DTO for access grants in detail response |
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateMeetingAccess/UpdateMeetingAccessRequest.cs` | Request: transcriptId, accessLevel, restrictedUserEmails |
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateMeetingAccess/UpdateMeetingAccessResponse.cs` | Response: updated access level + grants |
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateMeetingAccess/UpdateMeetingAccessHandler.cs` | Handler: manager-only, replaces grants |
| `backend/test/Anela.Heblo.Tests/Application/MeetingTasks/MeetingAccessGuardTests.cs` | Unit tests for MeetingAccessGuard |
| `backend/test/Anela.Heblo.Tests/Application/MeetingTasks/UpdateMeetingAccessHandlerTests.cs` | Unit tests for UpdateMeetingAccess handler |
| `backend/test/Anela.Heblo.Tests/Application/MeetingTasks/GetTranscriptDetailHandlerAccessTests.cs` | Access-denied tests for the 6 transcript handlers |
| `backend/test/Anela.Heblo.Tests/Repositories/MeetingTranscriptRepositoryTests.cs` | Repository tests: filtered list, SetAccessAsync |
| `backend/test/Anela.Heblo.Tests/Application/Users/CurrentUserServiceIsInRoleTests.cs` | Unit tests for IsInRole |

### Backend — Modified Files
| File | Change |
|------|--------|
| `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingTranscript.cs` | Add `AccessLevel` + `AccessGrants` properties |
| `backend/src/Anela.Heblo.Domain/Features/Authorization/AuthorizationConstants.cs` | Add `MeetingManager = "meeting_manager"` |
| `backend/src/Anela.Heblo.Domain/Features/Users/ICurrentUserService.cs` | Add `bool IsInRole(string role)` |
| `backend/src/Anela.Heblo.Application/Features/Users/CurrentUserService.cs` | Implement `IsInRole` |
| `backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptConfiguration.cs` | Map `AccessLevel` + `AccessGrants` relationship + index |
| `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` | Add `DbSet<MeetingAccessGrant>` |
| `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/IMeetingTranscriptRepository.cs` | Update `GetListAsync` signature + add `SetAccessAsync` |
| `backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptRepository.cs` | Update `GetByIdAsync`, `GetListAsync`, add `SetAccessAsync` |
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs` | Register `IMeetingAccessGuard` as Scoped |
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/MeetingTranscriptDto.cs` | Add `AccessLevel` + `AccessGrants` |
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptDetail/GetTranscriptDetailHandler.cs` | Inject guard, check CanAccess |
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/AddProposedTask/AddProposedTaskHandler.cs` | Inject guard, check CanAccess |
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTask/UpdateProposedTaskHandler.cs` | Inject guard, check CanAccess |
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTaskStatus/UpdateProposedTaskStatusHandler.cs` | Inject guard, check CanAccess |
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/SubmitToTodo/SubmitToTodoHandler.cs` | Inject guard, check CanAccess |
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ExplainSummary/ExplainSummaryHandler.cs` | Inject guard, check CanAccess |
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptList/GetTranscriptListHandler.cs` | Pass isManager + email to GetListAsync |
| `backend/src/Anela.Heblo.API/Controllers/MeetingTasksController.cs` | Add `PUT {transcriptId}/access` endpoint |
| `backend/src/Anela.Heblo.API/Infrastructure/Authentication/MockAuthenticationHandler.cs` | Add `meeting_manager` role claim |

### Frontend — New Files
| File | Purpose |
|------|---------|
| `frontend/src/api/hooks/useMeetingManagerPermission.ts` | Role check hook for meeting_manager |
| `frontend/src/components/pages/automation/access/ManageAccessModal.tsx` | Manager modal: radio + user picker |

### Frontend — Modified Files
| File | Change |
|------|--------|
| `frontend/src/auth/mockAuth.ts` | Add `meeting_manager` to mock user roles |
| `frontend/src/api/hooks/useMeetingTasks.ts` | Add DTO types + `useUpdateMeetingAccess` mutation |
| `frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx` | Access badge + "Spravovat přístup" button |

---

## Task 1: Domain Types

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingAccessLevel.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingAccessGrant.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingTranscript.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Authorization/AuthorizationConstants.cs`

No unit tests needed — pure data types with no logic.

- [ ] **Step 1: Create `MeetingAccessLevel.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.MeetingTasks;

public enum MeetingAccessLevel
{
    Private = 0,
    Public = 1,
    Restricted = 2
}
```

- [ ] **Step 2: Create `MeetingAccessGrant.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.MeetingTasks;

public class MeetingAccessGrant
{
    public Guid Id { get; set; }
    public Guid MeetingTranscriptId { get; set; }
    public MeetingTranscript MeetingTranscript { get; set; } = null!;
    public string UserEmail { get; set; } = null!;
    public string? UserDisplayName { get; set; }
    public DateTime GrantedAt { get; set; }
    public string GrantedByUser { get; set; } = null!;
}
```

- [ ] **Step 3: Modify `MeetingTranscript.cs` — append two properties after `Tasks`**

Open `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingTranscript.cs` and add after `public List<ProposedTask> Tasks { get; set; } = new();`:

```csharp
    public MeetingAccessLevel AccessLevel { get; set; } = MeetingAccessLevel.Private;
    public List<MeetingAccessGrant> AccessGrants { get; set; } = new();
```

- [ ] **Step 4: Add `MeetingManager` constant to `AuthorizationConstants.cs`**

Open `backend/src/Anela.Heblo.Domain/Features/Authorization/AuthorizationConstants.cs` and add inside the `Roles` class after `MarketingWriter`:

```csharp
        /// <summary>
        /// Role required for managing meeting access levels and grants
        /// </summary>
        public const string MeetingManager = "meeting_manager";
```

- [ ] **Step 5: Build to verify no compilation errors**

```bash
dotnet build backend/src/Anela.Heblo.sln
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingAccessLevel.cs \
        backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingAccessGrant.cs \
        backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingTranscript.cs \
        backend/src/Anela.Heblo.Domain/Features/Authorization/AuthorizationConstants.cs
git commit -m "feat: add MeetingAccessLevel enum, MeetingAccessGrant entity, and meeting_manager role constant"
```

---

## Task 2: CurrentUserService.IsInRole

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Users/ICurrentUserService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Users/CurrentUserService.cs`
- Create: `backend/test/Anela.Heblo.Tests/Application/Users/CurrentUserServiceIsInRoleTests.cs`

- [ ] **Step 1: Write failing test**

Create `backend/test/Anela.Heblo.Tests/Application/Users/CurrentUserServiceIsInRoleTests.cs`:

```csharp
using System.Security.Claims;
using Anela.Heblo.Application.Features.Users;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;

namespace Anela.Heblo.Tests.Application.Users;

public class CurrentUserServiceIsInRoleTests
{
    [Fact]
    public void IsInRole_ReturnsTrue_WhenUserHasMatchingRole()
    {
        // Arrange
        var claims = new[] { new Claim(ClaimTypes.Role, "meeting_manager") };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        var accessorMock = new Mock<IHttpContextAccessor>();
        accessorMock.Setup(x => x.HttpContext).Returns(httpContext);
        var sut = new CurrentUserService(accessorMock.Object);

        // Act
        var result = sut.IsInRole("meeting_manager");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsInRole_ReturnsFalse_WhenUserDoesNotHaveRole()
    {
        // Arrange
        var claims = new[] { new Claim(ClaimTypes.Role, "heblo_user") };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        var accessorMock = new Mock<IHttpContextAccessor>();
        accessorMock.Setup(x => x.HttpContext).Returns(httpContext);
        var sut = new CurrentUserService(accessorMock.Object);

        // Act
        var result = sut.IsInRole("meeting_manager");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsInRole_ReturnsFalse_WhenHttpContextIsNull()
    {
        // Arrange
        var accessorMock = new Mock<IHttpContextAccessor>();
        accessorMock.Setup(x => x.HttpContext).Returns((HttpContext?)null);
        var sut = new CurrentUserService(accessorMock.Object);

        // Act
        var result = sut.IsInRole("meeting_manager");

        // Assert
        result.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CurrentUserServiceIsInRoleTests" -v minimal
```

Expected: FAIL — `CurrentUserService` has no `IsInRole` method.

- [ ] **Step 3: Add `IsInRole` to the interface**

Open `backend/src/Anela.Heblo.Domain/Features/Users/ICurrentUserService.cs` and add after `CurrentUser GetCurrentUser();`:

```csharp
    bool IsInRole(string role);
```

Full file becomes:
```csharp
namespace Anela.Heblo.Domain.Features.Users;

public interface ICurrentUserService
{
    CurrentUser GetCurrentUser();
    bool IsInRole(string role);
}
```

- [ ] **Step 4: Implement `IsInRole` in `CurrentUserService.cs`**

Open `backend/src/Anela.Heblo.Application/Features/Users/CurrentUserService.cs` and add after `GetCurrentUser()`:

```csharp
    public bool IsInRole(string role)
    {
        return _httpContextAccessor.HttpContext?.User?.IsInRole(role) ?? false;
    }
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CurrentUserServiceIsInRoleTests" -v minimal
```

Expected: 3 tests PASS.

- [ ] **Step 6: Build full solution to ensure no other implementations break**

```bash
dotnet build backend/src/Anela.Heblo.sln
```

Expected: 0 errors. (If any class implements `ICurrentUserService`, add `IsInRole` there too.)

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Users/ICurrentUserService.cs \
        backend/src/Anela.Heblo.Application/Features/Users/CurrentUserService.cs \
        backend/test/Anela.Heblo.Tests/Application/Users/CurrentUserServiceIsInRoleTests.cs
git commit -m "feat: add IsInRole to ICurrentUserService"
```

---

## Task 3: MeetingAccessGuard

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IMeetingAccessGuard.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/MeetingAccessGuard.cs`
- Create: `backend/test/Anela.Heblo.Tests/Application/MeetingTasks/MeetingAccessGuardTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs`

- [ ] **Step 1: Write failing tests**

Create `backend/test/Anela.Heblo.Tests/Application/MeetingTasks/MeetingAccessGuardTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Application.MeetingTasks;

public class MeetingAccessGuardTests
{
    private readonly Mock<ICurrentUserService> _userServiceMock;
    private readonly MeetingAccessGuard _guard;

    public MeetingAccessGuardTests()
    {
        _userServiceMock = new Mock<ICurrentUserService>();
        _guard = new MeetingAccessGuard(_userServiceMock.Object);
    }

    // --- IsManager ---

    [Fact]
    public void IsManager_ReturnsTrue_WhenUserHasMeetingManagerRole()
    {
        _userServiceMock.Setup(x => x.IsInRole(AuthorizationConstants.Roles.MeetingManager)).Returns(true);
        _guard.IsManager().Should().BeTrue();
    }

    [Fact]
    public void IsManager_ReturnsFalse_WhenUserLacksRole()
    {
        _userServiceMock.Setup(x => x.IsInRole(AuthorizationConstants.Roles.MeetingManager)).Returns(false);
        _guard.IsManager().Should().BeFalse();
    }

    // --- CanAccess: manager ---

    [Fact]
    public void CanAccess_ReturnsTrue_ForManagerRegardlessOfAccessLevel()
    {
        _userServiceMock.Setup(x => x.IsInRole(AuthorizationConstants.Roles.MeetingManager)).Returns(true);
        _userServiceMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser(null, "Manager", "manager@test.com", true));

        var privateTranscript = MakeTranscript(MeetingAccessLevel.Private);
        _guard.CanAccess(privateTranscript).Should().BeTrue();
    }

    // --- CanAccess: Public ---

    [Fact]
    public void CanAccess_ReturnsTrue_ForPublicTranscript_WhenNonManager()
    {
        SetupNonManager("user@test.com");
        var transcript = MakeTranscript(MeetingAccessLevel.Public);
        _guard.CanAccess(transcript).Should().BeTrue();
    }

    // --- CanAccess: Private ---

    [Fact]
    public void CanAccess_ReturnsFalse_ForPrivateTranscript_WhenNonManager()
    {
        SetupNonManager("user@test.com");
        var transcript = MakeTranscript(MeetingAccessLevel.Private);
        _guard.CanAccess(transcript).Should().BeFalse();
    }

    // --- CanAccess: Restricted ---

    [Fact]
    public void CanAccess_ReturnsTrue_ForRestrictedTranscript_WhenEmailMatches()
    {
        SetupNonManager("user@test.com");
        var transcript = MakeTranscript(MeetingAccessLevel.Restricted, grantedEmails: ["user@test.com"]);
        _guard.CanAccess(transcript).Should().BeTrue();
    }

    [Fact]
    public void CanAccess_ReturnsTrue_ForRestrictedTranscript_CaseInsensitiveMatch()
    {
        SetupNonManager("USER@TEST.COM");
        var transcript = MakeTranscript(MeetingAccessLevel.Restricted, grantedEmails: ["user@test.com"]);
        _guard.CanAccess(transcript).Should().BeTrue();
    }

    [Fact]
    public void CanAccess_ReturnsFalse_ForRestrictedTranscript_WhenEmailDoesNotMatch()
    {
        SetupNonManager("other@test.com");
        var transcript = MakeTranscript(MeetingAccessLevel.Restricted, grantedEmails: ["user@test.com"]);
        _guard.CanAccess(transcript).Should().BeFalse();
    }

    [Fact]
    public void CanAccess_ReturnsFalse_WhenEmailIsNull()
    {
        _userServiceMock.Setup(x => x.IsInRole(AuthorizationConstants.Roles.MeetingManager)).Returns(false);
        _userServiceMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser(null, "Anonymous", null, false));
        var transcript = MakeTranscript(MeetingAccessLevel.Public);
        _guard.CanAccess(transcript).Should().BeFalse();
    }

    [Fact]
    public void CanAccess_ReturnsFalse_WhenEmailIsWhitespace()
    {
        _userServiceMock.Setup(x => x.IsInRole(AuthorizationConstants.Roles.MeetingManager)).Returns(false);
        _userServiceMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser(null, "User", "   ", false));
        var transcript = MakeTranscript(MeetingAccessLevel.Public);
        _guard.CanAccess(transcript).Should().BeFalse();
    }

    private void SetupNonManager(string email)
    {
        _userServiceMock.Setup(x => x.IsInRole(AuthorizationConstants.Roles.MeetingManager)).Returns(false);
        _userServiceMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser(null, "User", email, true));
    }

    private static MeetingTranscript MakeTranscript(MeetingAccessLevel level, string[]? grantedEmails = null)
    {
        var grants = (grantedEmails ?? []).Select(e => new MeetingAccessGrant { UserEmail = e }).ToList();
        return new MeetingTranscript
        {
            Id = Guid.NewGuid(),
            PlaudRecordingId = "test",
            PlaudCreatedAt = DateTime.UtcNow,
            Subject = "Test",
            Summary = "Test",
            RawTranscript = "Test",
            Status = MeetingTranscriptStatus.PendingReview,
            ReceivedAt = DateTime.UtcNow,
            AccessLevel = level,
            AccessGrants = grants
        };
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~MeetingAccessGuardTests" -v minimal
```

Expected: FAIL — `MeetingAccessGuard` doesn't exist.

- [ ] **Step 3: Create the interface**

Create `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IMeetingAccessGuard.cs`:

```csharp
using Anela.Heblo.Domain.Features.MeetingTasks;

namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public interface IMeetingAccessGuard
{
    bool IsManager();
    bool CanAccess(MeetingTranscript transcript);
}
```

- [ ] **Step 4: Create the implementation**

Create `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/MeetingAccessGuard.cs`:

```csharp
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Domain.Features.Users;

namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public class MeetingAccessGuard : IMeetingAccessGuard
{
    private readonly ICurrentUserService _currentUserService;

    public MeetingAccessGuard(ICurrentUserService currentUserService)
    {
        _currentUserService = currentUserService;
    }

    public bool IsManager() => _currentUserService.IsInRole(AuthorizationConstants.Roles.MeetingManager);

    public bool CanAccess(MeetingTranscript transcript)
    {
        if (IsManager()) return true;

        var email = _currentUserService.GetCurrentUser().Email;
        if (string.IsNullOrWhiteSpace(email)) return false;

        return transcript.AccessLevel switch
        {
            MeetingAccessLevel.Public => true,
            MeetingAccessLevel.Private => false,
            MeetingAccessLevel.Restricted => transcript.AccessGrants.Any(
                g => g.UserEmail.Equals(email, StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~MeetingAccessGuardTests" -v minimal
```

Expected: 9 tests PASS.

- [ ] **Step 6: Register guard in MeetingTasksModule**

Open `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs` and add after `services.AddSingleton<IMeetingUserDirectory, MeetingUserDirectory>();`:

```csharp
        services.AddScoped<IMeetingAccessGuard, MeetingAccessGuard>();
```

- [ ] **Step 7: Build solution**

```bash
dotnet build backend/src/Anela.Heblo.sln
```

Expected: 0 errors.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IMeetingAccessGuard.cs \
        backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/MeetingAccessGuard.cs \
        backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs \
        backend/test/Anela.Heblo.Tests/Application/MeetingTasks/MeetingAccessGuardTests.cs
git commit -m "feat: add IMeetingAccessGuard with Private/Public/Restricted logic"
```

---

## Task 4: Persistence — EF Config + Repository

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingAccessGrantConfiguration.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/IMeetingTranscriptRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptRepository.cs`
- Create: `backend/test/Anela.Heblo.Tests/Repositories/MeetingTranscriptRepositoryTests.cs`

- [ ] **Step 1: Write failing repository tests**

Create `backend/test/Anela.Heblo.Tests/Repositories/MeetingTranscriptRepositoryTests.cs`:

```csharp
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.MeetingTasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Tests.Repositories;

public class MeetingTranscriptRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly MeetingTranscriptRepository _repository;

    public MeetingTranscriptRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);
        _repository = new MeetingTranscriptRepository(_context);
    }

    public void Dispose() => _context.Dispose();

    // --- GetListAsync filtering ---

    [Fact]
    public async Task GetListAsync_Manager_SeesAllTranscripts()
    {
        // Arrange
        await SeedTranscriptAsync(MeetingAccessLevel.Private);
        await SeedTranscriptAsync(MeetingAccessLevel.Public);
        await SeedTranscriptAsync(MeetingAccessLevel.Restricted, grantedEmail: "other@test.com");

        // Act
        var (items, total) = await _repository.GetListAsync(
            statusFilter: null, isManager: true, userEmail: "manager@test.com",
            page: 1, pageSize: 20, ct: default);

        // Assert
        total.Should().Be(3);
        items.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetListAsync_NonManager_SeesOnlyPublic()
    {
        // Arrange
        await SeedTranscriptAsync(MeetingAccessLevel.Private);
        await SeedTranscriptAsync(MeetingAccessLevel.Public);

        // Act
        var (items, total) = await _repository.GetListAsync(
            statusFilter: null, isManager: false, userEmail: "user@test.com",
            page: 1, pageSize: 20, ct: default);

        // Assert
        total.Should().Be(1);
        items.Should().HaveCount(1);
        items[0].AccessLevel.Should().Be(MeetingAccessLevel.Public);
    }

    [Fact]
    public async Task GetListAsync_NonManager_SeesRestrictedWhenGranted()
    {
        // Arrange
        await SeedTranscriptAsync(MeetingAccessLevel.Restricted, grantedEmail: "user@test.com");
        await SeedTranscriptAsync(MeetingAccessLevel.Restricted, grantedEmail: "other@test.com");

        // Act
        var (items, total) = await _repository.GetListAsync(
            statusFilter: null, isManager: false, userEmail: "user@test.com",
            page: 1, pageSize: 20, ct: default);

        // Assert
        total.Should().Be(1);
        items[0].AccessGrants.Should().ContainSingle(g => g.UserEmail == "user@test.com");
    }

    [Fact]
    public async Task GetListAsync_NonManager_PrivateNotReturned()
    {
        // Arrange
        await SeedTranscriptAsync(MeetingAccessLevel.Private);

        // Act
        var (items, total) = await _repository.GetListAsync(
            statusFilter: null, isManager: false, userEmail: "user@test.com",
            page: 1, pageSize: 20, ct: default);

        // Assert
        total.Should().Be(0);
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetListAsync_PaginationReflectsFilteredSet()
    {
        // Arrange — 3 public, 1 private
        for (var i = 0; i < 3; i++) await SeedTranscriptAsync(MeetingAccessLevel.Public);
        await SeedTranscriptAsync(MeetingAccessLevel.Private);

        // Act — page 1 of 2
        var (items, total) = await _repository.GetListAsync(
            statusFilter: null, isManager: false, userEmail: "user@test.com",
            page: 1, pageSize: 2, ct: default);

        // Assert
        total.Should().Be(3);  // private excluded from count
        items.Should().HaveCount(2);
    }

    // --- GetByIdAsync includes AccessGrants ---

    [Fact]
    public async Task GetByIdAsync_IncludesAccessGrants()
    {
        // Arrange
        var id = await SeedTranscriptAsync(MeetingAccessLevel.Restricted, grantedEmail: "user@test.com");

        // Act
        var result = await _repository.GetByIdAsync(id);

        // Assert
        result.Should().NotBeNull();
        result!.AccessGrants.Should().ContainSingle(g => g.UserEmail == "user@test.com");
    }

    // --- SetAccessAsync ---

    [Fact]
    public async Task SetAccessAsync_ReplacesGrantsAndSetsLevel()
    {
        // Arrange
        var id = await SeedTranscriptAsync(MeetingAccessLevel.Restricted, grantedEmail: "old@test.com");
        var transcript = await _context.MeetingTranscripts
            .Include(x => x.AccessGrants)
            .FirstAsync(x => x.Id == id);

        var newGrants = new List<MeetingAccessGrant>
        {
            new() { Id = Guid.NewGuid(), MeetingTranscriptId = id, UserEmail = "new@test.com", GrantedAt = DateTime.UtcNow, GrantedByUser = "manager@test.com" }
        };

        // Act
        await _repository.SetAccessAsync(transcript, MeetingAccessLevel.Public, newGrants, default);

        // Assert
        var updated = await _context.MeetingTranscripts
            .Include(x => x.AccessGrants)
            .FirstAsync(x => x.Id == id);
        updated.AccessLevel.Should().Be(MeetingAccessLevel.Public);
        updated.AccessGrants.Should().ContainSingle(g => g.UserEmail == "new@test.com");
        updated.AccessGrants.Should().NotContain(g => g.UserEmail == "old@test.com");
    }

    private async Task<Guid> SeedTranscriptAsync(MeetingAccessLevel level, string? grantedEmail = null)
    {
        var transcript = new MeetingTranscript
        {
            Id = Guid.NewGuid(),
            PlaudRecordingId = Guid.NewGuid().ToString(),
            PlaudCreatedAt = DateTime.UtcNow,
            Subject = "Test",
            Summary = "Test",
            RawTranscript = "Test",
            Status = MeetingTranscriptStatus.PendingReview,
            ReceivedAt = DateTime.UtcNow,
            AccessLevel = level,   // set explicitly — EF InMemory ignores HasDefaultValue
            AccessGrants = grantedEmail is null
                ? []
                : [new MeetingAccessGrant { Id = Guid.NewGuid(), UserEmail = grantedEmail, GrantedAt = DateTime.UtcNow, GrantedByUser = "seeder" }]
        };
        _context.MeetingTranscripts.Add(transcript);
        await _context.SaveChangesAsync();
        return transcript.Id;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~MeetingTranscriptRepositoryTests" -v minimal
```

Expected: FAIL — `GetListAsync` has wrong signature, `SetAccessAsync` doesn't exist.

- [ ] **Step 3: Add `DbSet<MeetingAccessGrant>` to `ApplicationDbContext.cs`**

Open `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` and add after the existing `DbSet<ProposedTask>` line:

```csharp
    public DbSet<MeetingAccessGrant> MeetingAccessGrants { get; set; } = null!;
```

- [ ] **Step 4: Update `MeetingTranscriptConfiguration.cs`**

Open `backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptConfiguration.cs`.

After the `builder.HasMany(x => x.Tasks)...OnDelete(DeleteBehavior.Cascade);` block, add:

```csharp
        builder.Property(x => x.AccessLevel)
            .IsRequired()
            .HasMaxLength(20)
            .HasConversion<string>()
            .HasDefaultValue(MeetingAccessLevel.Private);

        builder.HasMany(x => x.AccessGrants)
            .WithOne(x => x.MeetingTranscript)
            .HasForeignKey(x => x.MeetingTranscriptId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.AccessLevel)
            .HasDatabaseName("IX_MeetingTranscripts_AccessLevel");
```

- [ ] **Step 5: Create `MeetingAccessGrantConfiguration.cs`**

Create `backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingAccessGrantConfiguration.cs`:

```csharp
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.MeetingTasks;

public class MeetingAccessGrantConfiguration : IEntityTypeConfiguration<MeetingAccessGrant>
{
    public void Configure(EntityTypeBuilder<MeetingAccessGrant> builder)
    {
        builder.ToTable("MeetingAccessGrants", "public");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserEmail)
            .IsRequired()
            .HasMaxLength(320);

        builder.Property(x => x.UserDisplayName)
            .HasMaxLength(200)
            .IsRequired(false);

        builder.Property(x => x.GrantedAt)
            .IsRequired()
            .AsUtcTimestamp();

        builder.Property(x => x.GrantedByUser)
            .IsRequired()
            .HasMaxLength(320);

        builder.HasIndex(x => new { x.MeetingTranscriptId, x.UserEmail })
            .IsUnique()
            .HasDatabaseName("UX_MeetingAccessGrants_TranscriptId_UserEmail");
    }
}
```

- [ ] **Step 6: Update `IMeetingTranscriptRepository.cs`**

Replace the file with:

```csharp
using Anela.Heblo.Domain.Features.MeetingTasks;

namespace Anela.Heblo.Domain.Features.MeetingTasks;

public interface IMeetingTranscriptRepository
{
    Task<MeetingTranscript?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<(List<MeetingTranscript> Items, int TotalCount)> GetListAsync(
        MeetingTranscriptStatus? statusFilter,
        bool isManager,
        string? userEmail,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<bool> ExistsByPlaudIdAsync(string plaudRecordingId, CancellationToken ct = default);

    Task AddAsync(MeetingTranscript transcript, CancellationToken ct = default);

    Task SetAccessAsync(
        MeetingTranscript transcript,
        MeetingAccessLevel level,
        IReadOnlyList<MeetingAccessGrant> newGrants,
        CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 7: Update `MeetingTranscriptRepository.cs`**

Replace the file with:

```csharp
using Anela.Heblo.Domain.Features.MeetingTasks;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.MeetingTasks;

public class MeetingTranscriptRepository : IMeetingTranscriptRepository
{
    private readonly ApplicationDbContext _context;

    public MeetingTranscriptRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<MeetingTranscript?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return _context.MeetingTranscripts
            .Include(x => x.Tasks)
            .Include(x => x.AccessGrants)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<(List<MeetingTranscript> Items, int TotalCount)> GetListAsync(
        MeetingTranscriptStatus? statusFilter,
        bool isManager,
        string? userEmail,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _context.MeetingTranscripts.AsQueryable();

        if (statusFilter.HasValue)
            query = query.Where(x => x.Status == statusFilter.Value);

        if (!isManager)
        {
            var email = (userEmail ?? string.Empty).ToLowerInvariant();
            query = query.Where(x =>
                x.AccessLevel == MeetingAccessLevel.Public ||
                (x.AccessLevel == MeetingAccessLevel.Restricted &&
                 x.AccessGrants.Any(g => g.UserEmail == email)));
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .Include(x => x.Tasks)
            .Include(x => x.AccessGrants)
            .OrderByDescending(x => x.PlaudCreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public Task<bool> ExistsByPlaudIdAsync(string plaudRecordingId, CancellationToken ct = default)
    {
        return _context.MeetingTranscripts
            .AnyAsync(x => x.PlaudRecordingId == plaudRecordingId, ct);
    }

    public async Task AddAsync(MeetingTranscript transcript, CancellationToken ct = default)
    {
        await _context.MeetingTranscripts.AddAsync(transcript, ct);
    }

    public async Task SetAccessAsync(
        MeetingTranscript transcript,
        MeetingAccessLevel level,
        IReadOnlyList<MeetingAccessGrant> newGrants,
        CancellationToken ct = default)
    {
        _context.MeetingAccessGrants.RemoveRange(transcript.AccessGrants);
        transcript.AccessLevel = level;
        await _context.MeetingAccessGrants.AddRangeAsync(newGrants, ct);
        await _context.SaveChangesAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _context.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 8: Run repository tests to verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~MeetingTranscriptRepositoryTests" -v minimal
```

Expected: 7 tests PASS.

- [ ] **Step 9: Build to catch any callers of the old `GetListAsync` signature**

```bash
dotnet build backend/src/Anela.Heblo.sln
```

Expected: Compile error in `GetTranscriptListHandler.cs` — wrong number of arguments. We'll fix that in Task 8.

- [ ] **Step 10: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptConfiguration.cs \
        backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingAccessGrantConfiguration.cs \
        backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs \
        backend/src/Anela.Heblo.Domain/Features/MeetingTasks/IMeetingTranscriptRepository.cs \
        backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptRepository.cs \
        backend/test/Anela.Heblo.Tests/Repositories/MeetingTranscriptRepositoryTests.cs
git commit -m "feat: add MeetingAccessGrant persistence config and update repository with access filtering"
```

---

## Task 5: EF Migration

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddMeetingAccessGating.cs` (generated)

- [ ] **Step 1: Fix the compile error in GetTranscriptListHandler temporarily**

Temporarily update `GetTranscriptListHandler.cs` to pass placeholder values so the project builds (we'll do the real fix in Task 8). Open the file and change the `_repository.GetListAsync(...)` call to:

```csharp
        var (items, totalCount) = await _repository.GetListAsync(
            statusFilter,
            isManager: true,     // temporary — Task 8 fixes this properly
            userEmail: null,
            request.PageNumber,
            request.PageSize,
            cancellationToken);
```

Then build to confirm it compiles:

```bash
dotnet build backend/src/Anela.Heblo.sln
```

Expected: 0 errors.

- [ ] **Step 2: Generate migration**

```bash
dotnet ef migrations add AddMeetingAccessGating \
  --project backend/src/Anela.Heblo.Persistence \
  --startup-project backend/src/Anela.Heblo.API \
  --output-dir Migrations
```

Expected: New migration file created.

- [ ] **Step 3: Verify migration Up/Down**

Open the generated migration file (e.g. `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddMeetingAccessGating.cs`) and confirm:
- `Up()` creates table `public.MeetingAccessGrants` with columns `Id`, `MeetingTranscriptId`, `UserEmail varchar(320)`, `UserDisplayName varchar(200) nullable`, `GrantedAt`, `GrantedByUser varchar(320)`, FK to `MeetingTranscripts` with Cascade delete, unique index `UX_MeetingAccessGrants_TranscriptId_UserEmail`.
- `Up()` adds column `AccessLevel varchar(20)` to `MeetingTranscripts` with default `'Private'`.
- `Up()` adds index `IX_MeetingTranscripts_AccessLevel`.
- `Down()` drops the index, table, and column.

If any of these are missing, add them manually.

- [ ] **Step 4: Apply migration to local database**

```bash
dotnet ef database update AddMeetingAccessGating \
  --project backend/src/Anela.Heblo.Persistence \
  --startup-project backend/src/Anela.Heblo.API
```

Expected: Migration applied. Verify `public.MeetingAccessGrants` table exists and `public.MeetingTranscripts` has `AccessLevel` column with `'Private'` as default for existing rows.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Migrations/
git commit -m "feat: add EF migration AddMeetingAccessGating"
```

---

## Task 6: Contract/DTO Updates

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/MeetingAccessGrantDto.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/MeetingTranscriptDto.cs`

- [ ] **Step 1: Create `MeetingAccessGrantDto.cs`**

Create `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/MeetingAccessGrantDto.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MeetingTasks.Contracts;

public class MeetingAccessGrantDto
{
    public string UserEmail { get; set; } = null!;
    public string? UserDisplayName { get; set; }
}
```

- [ ] **Step 2: Add `AccessLevel` and `AccessGrants` to `MeetingTranscriptDto.cs`**

Open `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/MeetingTranscriptDto.cs` and add after `public List<ProposedTaskDto> Tasks { get; set; } = new();`:

```csharp
    public string AccessLevel { get; set; } = "Private";
    public List<MeetingAccessGrantDto> AccessGrants { get; set; } = new();
```

- [ ] **Step 3: Build**

```bash
dotnet build backend/src/Anela.Heblo.sln
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/MeetingAccessGrantDto.cs \
        backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/MeetingTranscriptDto.cs
git commit -m "feat: add AccessLevel and AccessGrants to MeetingTranscriptDto"
```

---

## Task 7: Update 6 Transcript-Scoped Handlers

**Files:**
- Modify: `GetTranscriptDetailHandler.cs`, `AddProposedTaskHandler.cs`, `UpdateProposedTaskHandler.cs`, `UpdateProposedTaskStatusHandler.cs`, `SubmitToTodoHandler.cs`, `ExplainSummaryHandler.cs`
- Create: `backend/test/Anela.Heblo.Tests/Application/MeetingTasks/GetTranscriptDetailHandlerAccessTests.cs`

Each handler needs the same change: inject `IMeetingAccessGuard`, and after loading the transcript add the access check.

- [ ] **Step 1: Write failing access tests**

Create `backend/test/Anela.Heblo.Tests/Application/MeetingTasks/GetTranscriptDetailHandlerAccessTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Contracts;
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptDetail;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Application.MeetingTasks;

public class GetTranscriptDetailHandlerAccessTests
{
    private readonly Mock<IMeetingTranscriptRepository> _repositoryMock;
    private readonly Mock<IMeetingAccessGuard> _guardMock;
    private readonly GetTranscriptDetailHandler _handler;

    public GetTranscriptDetailHandlerAccessTests()
    {
        _repositoryMock = new Mock<IMeetingTranscriptRepository>();
        _guardMock = new Mock<IMeetingAccessGuard>();
        _handler = new GetTranscriptDetailHandler(
            _repositoryMock.Object,
            _guardMock.Object,
            new Mock<ILogger<GetTranscriptDetailHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenTranscriptIsNull()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repositoryMock.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MeetingTranscript?)null);

        // Act
        var result = await _handler.Handle(new GetTranscriptDetailRequest { Id = id }, default);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenAccessDenied()
    {
        // Arrange — transcript exists but guard denies access
        var id = Guid.NewGuid();
        var transcript = MakeTranscript(id, MeetingAccessLevel.Private);
        _repositoryMock.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);
        _guardMock.Setup(x => x.CanAccess(transcript)).Returns(false);

        // Act
        var result = await _handler.Handle(new GetTranscriptDetailRequest { Id = id }, default);

        // Assert — same 404 as missing id; no existence leak
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
    }

    [Fact]
    public async Task Handle_ReturnsTranscript_WhenAccessAllowed()
    {
        // Arrange
        var id = Guid.NewGuid();
        var transcript = MakeTranscript(id, MeetingAccessLevel.Public);
        _repositoryMock.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);
        _guardMock.Setup(x => x.CanAccess(transcript)).Returns(true);

        // Act
        var result = await _handler.Handle(new GetTranscriptDetailRequest { Id = id }, default);

        // Assert
        result.Success.Should().BeTrue();
        result.Transcript.Should().NotBeNull();
        result.Transcript!.Id.Should().Be(id);
    }

    private static MeetingTranscript MakeTranscript(Guid id, MeetingAccessLevel level) => new()
    {
        Id = id,
        PlaudRecordingId = "test",
        PlaudCreatedAt = DateTime.UtcNow,
        Subject = "Test",
        Summary = "Test",
        RawTranscript = "Test",
        Status = MeetingTranscriptStatus.PendingReview,
        ReceivedAt = DateTime.UtcNow,
        AccessLevel = level,
        Tasks = []
    };
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetTranscriptDetailHandlerAccessTests" -v minimal
```

Expected: FAIL — `GetTranscriptDetailHandler` constructor doesn't accept `IMeetingAccessGuard`.

- [ ] **Step 3: Update `GetTranscriptDetailHandler.cs`**

Replace the file with:

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Contracts;
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptDetail;

public class GetTranscriptDetailHandler : IRequestHandler<GetTranscriptDetailRequest, GetTranscriptDetailResponse>
{
    private readonly IMeetingTranscriptRepository _repository;
    private readonly IMeetingAccessGuard _accessGuard;
    private readonly ILogger<GetTranscriptDetailHandler> _logger;

    public GetTranscriptDetailHandler(
        IMeetingTranscriptRepository repository,
        IMeetingAccessGuard accessGuard,
        ILogger<GetTranscriptDetailHandler> logger)
    {
        _repository = repository;
        _accessGuard = accessGuard;
        _logger = logger;
    }

    public async Task<GetTranscriptDetailResponse> Handle(GetTranscriptDetailRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting meeting transcript detail — Id: {Id}", request.Id);

        var transcript = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (transcript is null)
        {
            _logger.LogWarning("Meeting transcript {Id} not found", request.Id);
            return new GetTranscriptDetailResponse(ErrorCodes.ResourceNotFound);
        }

        if (!_accessGuard.CanAccess(transcript))
        {
            _logger.LogWarning("Access denied to meeting transcript {Id} for current user", request.Id);
            return new GetTranscriptDetailResponse(ErrorCodes.ResourceNotFound);
        }

        var dto = new MeetingTranscriptDto
        {
            Id = transcript.Id,
            PlaudRecordingId = transcript.PlaudRecordingId,
            PlaudCreatedAt = transcript.PlaudCreatedAt,
            Subject = transcript.Subject,
            Summary = transcript.Summary,
            RawTranscript = transcript.RawTranscript,
            Status = transcript.Status.ToString(),
            ReceivedAt = transcript.ReceivedAt,
            ReviewedAt = transcript.ReviewedAt,
            ReviewedByUser = transcript.ReviewedByUser,
            TaskCount = transcript.Tasks.Count,
            ApprovedTaskCount = transcript.Tasks.Count(x => x.Status == ProposedTaskStatus.Approved),
            RejectedTaskCount = transcript.Tasks.Count(x => x.Status == ProposedTaskStatus.Rejected),
            AccessLevel = transcript.AccessLevel.ToString(),
            AccessGrants = transcript.AccessGrants.Select(g => new MeetingAccessGrantDto
            {
                UserEmail = g.UserEmail,
                UserDisplayName = g.UserDisplayName
            }).ToList(),
            Tasks = transcript.Tasks.Select(t => new ProposedTaskDto
            {
                Id = t.Id,
                Title = t.Title,
                Description = t.Description,
                Assignee = t.Assignee,
                AssigneeEmail = t.AssigneeEmail,
                DueDate = t.DueDate,
                Status = t.Status.ToString(),
                ExternalTaskId = t.ExternalTaskId,
                IsManuallyAdded = t.IsManuallyAdded
            }).ToList()
        };

        return new GetTranscriptDetailResponse { Transcript = dto };
    }
}
```

- [ ] **Step 4: Update the remaining 5 handlers with the same access check pattern**

For each of the following handlers, add `IMeetingAccessGuard _accessGuard` to the constructor and insert the access check after loading the transcript. The pattern is identical to Step 3 — inject guard, check, return `ResourceNotFound` on deny.

**`AddProposedTaskHandler.cs`** — after `if (transcript is null) return ...NotFound...`, add:
```csharp
        if (!_accessGuard.CanAccess(transcript))
        {
            _logger.LogWarning("Access denied to meeting transcript {TranscriptId} for current user", request.TranscriptId);
            return new AddProposedTaskResponse(ErrorCodes.ResourceNotFound);
        }
```
Constructor: add `IMeetingAccessGuard accessGuard` parameter, assign to `_accessGuard`.

**`UpdateProposedTaskHandler.cs`** — same pattern, same `ResourceNotFound`.

**`UpdateProposedTaskStatusHandler.cs`** — same pattern.

**`SubmitToTodoHandler.cs`** — same pattern.

**`ExplainSummaryHandler.cs`** — same pattern.

- [ ] **Step 5: Run all access tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetTranscriptDetailHandlerAccessTests" -v minimal
```

Expected: 3 tests PASS.

- [ ] **Step 6: Build solution**

```bash
dotnet build backend/src/Anela.Heblo.sln
```

Expected: 0 errors.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/
git commit -m "feat: gate 6 transcript handlers behind IMeetingAccessGuard.CanAccess"
```

---

## Task 8: Update GetTranscriptListHandler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptList/GetTranscriptListHandler.cs`

The list handler needs `ICurrentUserService` injected to compute `isManager` and `userEmail`.

- [ ] **Step 1: Write failing test (list is server-filtered, no access-denied case — just verify the handler passes correct params)**

Add to `backend/test/Anela.Heblo.Tests/Application/MeetingTasks/GetTranscriptDetailHandlerAccessTests.cs` a new test class:

```csharp
// Append after GetTranscriptDetailHandlerAccessTests class in the same file

public class GetTranscriptListHandlerTests
{
    private readonly Mock<IMeetingTranscriptRepository> _repositoryMock;
    private readonly Mock<ICurrentUserService> _userServiceMock;
    private readonly GetTranscriptListHandler _handler;

    public GetTranscriptListHandlerTests()
    {
        _repositoryMock = new Mock<IMeetingTranscriptRepository>();
        _userServiceMock = new Mock<ICurrentUserService>();
        _handler = new GetTranscriptListHandler(
            _repositoryMock.Object,
            _userServiceMock.Object,
            new Mock<ILogger<GetTranscriptListHandler>>().Object);
    }

    [Fact]
    public async Task Handle_PassesIsManagerAndEmail_ToRepository()
    {
        // Arrange
        _userServiceMock.Setup(x => x.IsInRole("meeting_manager")).Returns(true);
        _userServiceMock.Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(null, "Manager", "manager@test.com", true));
        _repositoryMock
            .Setup(x => x.GetListAsync(null, true, "manager@test.com", 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<MeetingTranscript>(), 0));

        // Act
        var result = await _handler.Handle(new GetTranscriptListRequest { PageNumber = 1, PageSize = 20 }, default);

        // Assert
        result.Success.Should().BeTrue();
        _repositoryMock.Verify(
            x => x.GetListAsync(null, true, "manager@test.com", 1, 20, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
```

Add the missing using at the top of the file:
```csharp
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptList;
using Anela.Heblo.Domain.Features.Users;
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetTranscriptListHandlerTests" -v minimal
```

Expected: FAIL — handler constructor doesn't accept `ICurrentUserService`.

- [ ] **Step 3: Update `GetTranscriptListHandler.cs`**

Replace the file with:

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Contracts;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptList;

public class GetTranscriptListHandler : IRequestHandler<GetTranscriptListRequest, GetTranscriptListResponse>
{
    private readonly IMeetingTranscriptRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<GetTranscriptListHandler> _logger;

    public GetTranscriptListHandler(
        IMeetingTranscriptRepository repository,
        ICurrentUserService currentUserService,
        ILogger<GetTranscriptListHandler> logger)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<GetTranscriptListResponse> Handle(GetTranscriptListRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Getting meeting transcript list — StatusFilter: {StatusFilter}, PageNumber: {PageNumber}, PageSize: {PageSize}",
            request.StatusFilter, request.PageNumber, request.PageSize);

        MeetingTranscriptStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(request.StatusFilter)
            && Enum.TryParse<MeetingTranscriptStatus>(request.StatusFilter, ignoreCase: true, out var parsed))
        {
            statusFilter = parsed;
        }

        var isManager = _currentUserService.IsInRole(AuthorizationConstants.Roles.MeetingManager);
        var userEmail = _currentUserService.GetCurrentUser().Email;

        var (items, totalCount) = await _repository.GetListAsync(
            statusFilter,
            isManager,
            userEmail,
            request.PageNumber,
            request.PageSize,
            cancellationToken);

        var dtos = items.Select(t => new MeetingTranscriptDto
        {
            Id = t.Id,
            PlaudRecordingId = t.PlaudRecordingId,
            PlaudCreatedAt = t.PlaudCreatedAt,
            Subject = t.Subject,
            Summary = t.Summary,
            Status = t.Status.ToString(),
            ReceivedAt = t.ReceivedAt,
            ReviewedAt = t.ReviewedAt,
            ReviewedByUser = t.ReviewedByUser,
            TaskCount = t.Tasks.Count,
            ApprovedTaskCount = t.Tasks.Count(x => x.Status == ProposedTaskStatus.Approved),
            RejectedTaskCount = t.Tasks.Count(x => x.Status == ProposedTaskStatus.Rejected),
            AccessLevel = t.AccessLevel.ToString(),
            Tasks = new()
        }).ToList();

        return new GetTranscriptListResponse
        {
            Items = dtos,
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize
        };
    }
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetTranscriptListHandlerTests" -v minimal
```

Expected: 1 test PASS.

- [ ] **Step 5: Build**

```bash
dotnet build backend/src/Anela.Heblo.sln
```

Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptList/GetTranscriptListHandler.cs
git commit -m "feat: pass isManager + userEmail to GetListAsync in GetTranscriptListHandler"
```

---

## Task 9: UpdateMeetingAccess Use Case

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateMeetingAccess/UpdateMeetingAccessRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateMeetingAccess/UpdateMeetingAccessResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateMeetingAccess/UpdateMeetingAccessHandler.cs`
- Create: `backend/test/Anela.Heblo.Tests/Application/MeetingTasks/UpdateMeetingAccessHandlerTests.cs`

- [ ] **Step 1: Write failing tests**

Create `backend/test/Anela.Heblo.Tests/Application/MeetingTasks/UpdateMeetingAccessHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateMeetingAccess;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Application.MeetingTasks;

public class UpdateMeetingAccessHandlerTests
{
    private readonly Mock<IMeetingTranscriptRepository> _repositoryMock;
    private readonly Mock<IMeetingAccessGuard> _guardMock;
    private readonly Mock<ICurrentUserService> _userServiceMock;
    private readonly Mock<IMeetingUserDirectory> _directoryMock;
    private readonly UpdateMeetingAccessHandler _handler;

    public UpdateMeetingAccessHandlerTests()
    {
        _repositoryMock = new Mock<IMeetingTranscriptRepository>();
        _guardMock = new Mock<IMeetingAccessGuard>();
        _userServiceMock = new Mock<ICurrentUserService>();
        _directoryMock = new Mock<IMeetingUserDirectory>();

        _handler = new UpdateMeetingAccessHandler(
            _repositoryMock.Object,
            _guardMock.Object,
            _userServiceMock.Object,
            _directoryMock.Object,
            new Mock<ILogger<UpdateMeetingAccessHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ReturnsForbidden_WhenNotManager()
    {
        // Arrange
        _guardMock.Setup(x => x.IsManager()).Returns(false);

        // Act
        var result = await _handler.Handle(
            new UpdateMeetingAccessRequest { TranscriptId = Guid.NewGuid(), AccessLevel = "Public" },
            default);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenTranscriptMissing()
    {
        // Arrange
        _guardMock.Setup(x => x.IsManager()).Returns(true);
        var id = Guid.NewGuid();
        _repositoryMock.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MeetingTranscript?)null);

        // Act
        var result = await _handler.Handle(
            new UpdateMeetingAccessRequest { TranscriptId = id, AccessLevel = "Public" },
            default);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
    }

    [Fact]
    public async Task Handle_ReturnsValidationError_WhenRestrictedWithEmptyEmails()
    {
        // Arrange
        SetupManagerWithTranscript(Guid.NewGuid());

        // Act
        var result = await _handler.Handle(
            new UpdateMeetingAccessRequest
            {
                TranscriptId = Guid.NewGuid(),
                AccessLevel = "Restricted",
                RestrictedUserEmails = []
            },
            default);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task Handle_ReturnsValidationError_WhenEmailNotInDirectory()
    {
        // Arrange
        var id = Guid.NewGuid();
        SetupManagerWithTranscript(id);
        _directoryMock.Setup(x => x.GetAll()).Returns(
        [
            new MeetingUser("known@test.com", "Known User", [])
        ]);

        // Act
        var result = await _handler.Handle(
            new UpdateMeetingAccessRequest
            {
                TranscriptId = id,
                AccessLevel = "Restricted",
                RestrictedUserEmails = ["unknown@test.com"]
            },
            default);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.Params!["email"].Should().Be("unknown@test.com");
    }

    [Fact]
    public async Task Handle_HappyPath_Public_CallsSetAccess_ReturnsSuccess()
    {
        // Arrange
        var id = Guid.NewGuid();
        var transcript = SetupManagerWithTranscript(id);
        _userServiceMock.Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(null, "Manager", "manager@test.com", true));

        // Act
        var result = await _handler.Handle(
            new UpdateMeetingAccessRequest { TranscriptId = id, AccessLevel = "Public", RestrictedUserEmails = [] },
            default);

        // Assert
        result.Success.Should().BeTrue();
        result.AccessLevel.Should().Be("Public");
        _repositoryMock.Verify(
            x => x.SetAccessAsync(transcript, MeetingAccessLevel.Public, It.IsAny<IReadOnlyList<MeetingAccessGrant>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_HappyPath_Restricted_ResolvesGrantsAndCallsSetAccess()
    {
        // Arrange
        var id = Guid.NewGuid();
        var transcript = SetupManagerWithTranscript(id);
        _userServiceMock.Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(null, "Manager", "manager@test.com", true));
        _directoryMock.Setup(x => x.GetAll()).Returns(
        [
            new MeetingUser("user@test.com", "Test User", [])
        ]);

        // Act
        var result = await _handler.Handle(
            new UpdateMeetingAccessRequest
            {
                TranscriptId = id,
                AccessLevel = "Restricted",
                RestrictedUserEmails = ["USER@TEST.COM"]  // uppercase — should be normalized
            },
            default);

        // Assert
        result.Success.Should().BeTrue();
        result.AccessLevel.Should().Be("Restricted");
        result.Grants.Should().ContainSingle(g => g.UserEmail == "user@test.com");
        _repositoryMock.Verify(
            x => x.SetAccessAsync(
                transcript,
                MeetingAccessLevel.Restricted,
                It.Is<IReadOnlyList<MeetingAccessGrant>>(grants =>
                    grants.Count == 1 && grants[0].UserEmail == "user@test.com"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private MeetingTranscript SetupManagerWithTranscript(Guid id)
    {
        _guardMock.Setup(x => x.IsManager()).Returns(true);
        var transcript = new MeetingTranscript
        {
            Id = id,
            PlaudRecordingId = "test",
            PlaudCreatedAt = DateTime.UtcNow,
            Subject = "Test",
            Summary = "Test",
            RawTranscript = "Test",
            Status = MeetingTranscriptStatus.PendingReview,
            ReceivedAt = DateTime.UtcNow,
            AccessLevel = MeetingAccessLevel.Private,
            AccessGrants = []
        };
        _repositoryMock.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);
        return transcript;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~UpdateMeetingAccessHandlerTests" -v minimal
```

Expected: FAIL — `UpdateMeetingAccessHandler` doesn't exist.

- [ ] **Step 3: Create `UpdateMeetingAccessRequest.cs`**

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateMeetingAccess;

public class UpdateMeetingAccessRequest : IRequest<UpdateMeetingAccessResponse>
{
    public Guid TranscriptId { get; set; }
    public string AccessLevel { get; set; } = null!;
    public List<string> RestrictedUserEmails { get; set; } = new();
}
```

- [ ] **Step 4: Create `UpdateMeetingAccessResponse.cs`**

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateMeetingAccess;

public class UpdateMeetingAccessResponse : BaseResponse
{
    public string? AccessLevel { get; set; }
    public List<MeetingAccessGrantDto> Grants { get; set; } = new();

    public UpdateMeetingAccessResponse() { }
    public UpdateMeetingAccessResponse(ErrorCodes errorCode) : base(errorCode) { }
    public UpdateMeetingAccessResponse(ErrorCodes errorCode, Dictionary<string, string> parameters)
        : base(errorCode, parameters) { }
}
```

- [ ] **Step 5: Create `UpdateMeetingAccessHandler.cs`**

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Contracts;
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateMeetingAccess;

public class UpdateMeetingAccessHandler : IRequestHandler<UpdateMeetingAccessRequest, UpdateMeetingAccessResponse>
{
    private readonly IMeetingTranscriptRepository _repository;
    private readonly IMeetingAccessGuard _accessGuard;
    private readonly ICurrentUserService _currentUserService;
    private readonly IMeetingUserDirectory _userDirectory;
    private readonly ILogger<UpdateMeetingAccessHandler> _logger;

    public UpdateMeetingAccessHandler(
        IMeetingTranscriptRepository repository,
        IMeetingAccessGuard accessGuard,
        ICurrentUserService currentUserService,
        IMeetingUserDirectory userDirectory,
        ILogger<UpdateMeetingAccessHandler> logger)
    {
        _repository = repository;
        _accessGuard = accessGuard;
        _currentUserService = currentUserService;
        _userDirectory = userDirectory;
        _logger = logger;
    }

    public async Task<UpdateMeetingAccessResponse> Handle(UpdateMeetingAccessRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Updating meeting access — TranscriptId: {TranscriptId}, AccessLevel: {AccessLevel}",
            request.TranscriptId, request.AccessLevel);

        if (!_accessGuard.IsManager())
        {
            _logger.LogWarning("Non-manager attempted to update meeting access for {TranscriptId}", request.TranscriptId);
            return new UpdateMeetingAccessResponse(ErrorCodes.Forbidden);
        }

        var transcript = await _repository.GetByIdAsync(request.TranscriptId, cancellationToken);
        if (transcript is null)
        {
            _logger.LogWarning("Meeting transcript {TranscriptId} not found", request.TranscriptId);
            return new UpdateMeetingAccessResponse(ErrorCodes.ResourceNotFound);
        }

        if (!Enum.TryParse<MeetingAccessLevel>(request.AccessLevel, ignoreCase: true, out var level))
        {
            return new UpdateMeetingAccessResponse(ErrorCodes.ValidationError,
                new Dictionary<string, string> { ["accessLevel"] = $"Unknown access level: {request.AccessLevel}" });
        }

        var grants = new List<MeetingAccessGrant>();

        if (level == MeetingAccessLevel.Restricted)
        {
            if (request.RestrictedUserEmails.Count == 0)
            {
                return new UpdateMeetingAccessResponse(ErrorCodes.ValidationError,
                    new Dictionary<string, string> { ["restrictedUserEmails"] = "At least one email is required for Restricted access" });
            }

            var allUsers = _userDirectory.GetAll();
            var grantedByUser = _currentUserService.GetCurrentUser().Email ?? string.Empty;

            foreach (var rawEmail in request.RestrictedUserEmails)
            {
                var normalizedEmail = rawEmail.Trim().ToLowerInvariant();
                var knownUser = allUsers.FirstOrDefault(u =>
                    u.Email.Equals(normalizedEmail, StringComparison.OrdinalIgnoreCase));

                if (knownUser is null)
                {
                    return new UpdateMeetingAccessResponse(ErrorCodes.ValidationError,
                        new Dictionary<string, string> { ["email"] = normalizedEmail });
                }

                grants.Add(new MeetingAccessGrant
                {
                    Id = Guid.NewGuid(),
                    MeetingTranscriptId = transcript.Id,
                    UserEmail = normalizedEmail,
                    UserDisplayName = knownUser.DisplayName,
                    GrantedAt = DateTime.UtcNow,
                    GrantedByUser = grantedByUser
                });
            }
        }

        await _repository.SetAccessAsync(transcript, level, grants, cancellationToken);

        _logger.LogInformation(
            "Meeting access updated — TranscriptId: {TranscriptId}, AccessLevel: {AccessLevel}, Grants: {GrantCount}",
            request.TranscriptId, level, grants.Count);

        return new UpdateMeetingAccessResponse
        {
            AccessLevel = level.ToString(),
            Grants = grants.Select(g => new MeetingAccessGrantDto
            {
                UserEmail = g.UserEmail,
                UserDisplayName = g.UserDisplayName
            }).ToList()
        };
    }
}
```

- [ ] **Step 6: Run tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~UpdateMeetingAccessHandlerTests" -v minimal
```

Expected: 6 tests PASS.

- [ ] **Step 7: Build solution**

```bash
dotnet build backend/src/Anela.Heblo.sln
```

Expected: 0 errors.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateMeetingAccess/ \
        backend/test/Anela.Heblo.Tests/Application/MeetingTasks/UpdateMeetingAccessHandlerTests.cs
git commit -m "feat: add UpdateMeetingAccess handler (manager-only, replaces access grants)"
```

---

## Task 10: Controller Endpoint

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/MeetingTasksController.cs`

- [ ] **Step 1: Add import and action to controller**

Open `backend/src/Anela.Heblo.API/Controllers/MeetingTasksController.cs`.

Add import at top:
```csharp
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateMeetingAccess;
```

Add action after the `ExplainSummary` method:

```csharp
    [HttpPut("{transcriptId:guid}/access")]
    public async Task<ActionResult<UpdateMeetingAccessResponse>> UpdateAccess(
        Guid transcriptId,
        [FromBody] UpdateMeetingAccessRequest request,
        CancellationToken ct = default)
    {
        request.TranscriptId = transcriptId;
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }
```

- [ ] **Step 2: Build**

```bash
dotnet build backend/src/Anela.Heblo.sln
```

Expected: 0 errors.

- [ ] **Step 3: Run all tests to verify nothing regressed**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v minimal
```

Expected: All tests PASS.

- [ ] **Step 4: Smoke-test the API**

Start the API (or use existing running instance). As a mock-auth user:

- `GET /api/meeting-tasks` — should return filtered list (no Private meetings since mock user lacks `meeting_manager` yet — we'll fix that in Task 11)
- Confirm the server starts without errors.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/MeetingTasksController.cs
git commit -m "feat: add PUT /api/meeting-tasks/{transcriptId}/access endpoint"
```

---

## Task 11: Mock Auth (Backend + Frontend)

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Infrastructure/Authentication/MockAuthenticationHandler.cs`
- Modify: `frontend/src/auth/mockAuth.ts`

- [ ] **Step 1: Add `meeting_manager` role to `MockAuthenticationHandler.cs`**

Open `backend/src/Anela.Heblo.API/Infrastructure/Authentication/MockAuthenticationHandler.cs`.

In the `claims` array inside `HandleAuthenticateAsync`, add after the `SuperUser` role claim:

```csharp
            new Claim(ClaimTypes.Role, AuthorizationConstants.Roles.MeetingManager),
```

The relevant block becomes:
```csharp
            new Claim(ClaimTypes.Role, AuthorizationConstants.Roles.FinanceReader),
            new Claim(ClaimTypes.Role, AuthorizationConstants.Roles.HebloUser),
            new Claim(ClaimTypes.Role, AuthorizationConstants.Roles.SuperUser),
            new Claim(ClaimTypes.Role, AuthorizationConstants.Roles.MeetingManager),
```

- [ ] **Step 2: Add `meeting_manager` to frontend mock user**

Open `frontend/src/auth/mockAuth.ts`.

In `createMockUser()`, update the `roles` array:

```typescript
    roles: ["finance_reader", "super_user", "marketing_reader", "meeting_manager"],
```

- [ ] **Step 3: Build both layers**

```bash
dotnet build backend/src/Anela.Heblo.sln
cd frontend && npm run build
```

Expected: Both builds succeed, 0 errors.

- [ ] **Step 4: Smoke-test access gating end-to-end**

Start the API with mock auth. Create a meeting transcript in the DB with `AccessLevel = 'Private'`. Call `GET /api/meeting-tasks` — the mock user is now a manager, so Private meetings appear in the list. Call `GET /api/meeting-tasks/{id}` for the Private meeting — should return 200.

Call `PUT /api/meeting-tasks/{id}/access` with `{ "accessLevel": "Public", "restrictedUserEmails": [] }` — should return 200.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/Infrastructure/Authentication/MockAuthenticationHandler.cs \
        frontend/src/auth/mockAuth.ts
git commit -m "feat: add meeting_manager role to mock auth (backend + frontend)"
```

---

## Task 12: Frontend — Permission Hook, Mutation, Modal, Detail Page

**Files:**
- Create: `frontend/src/api/hooks/useMeetingManagerPermission.ts`
- Modify: `frontend/src/api/hooks/useMeetingTasks.ts`
- Create: `frontend/src/components/pages/automation/access/ManageAccessModal.tsx`
- Modify: `frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx`

- [ ] **Step 1: Create `useMeetingManagerPermission.ts`**

Create `frontend/src/api/hooks/useMeetingManagerPermission.ts`:

```typescript
import { useMsal } from '@azure/msal-react';
import { shouldUseMockAuth } from '../../config/runtimeConfig';
import { mockAuthService } from '../../auth/mockAuth';

export const useMeetingManagerPermission = (): boolean => {
  const { accounts } = useMsal();

  if (shouldUseMockAuth()) {
    const user = mockAuthService.getUser();
    return !!(Array.isArray(user?.roles) && user?.roles.includes('meeting_manager'));
  }

  const account = accounts[0];
  if (!account) return false;
  const claims = account.idTokenClaims as Record<string, unknown> | undefined;
  const roles = claims?.['roles'];
  return Array.isArray(roles) && roles.includes('meeting_manager');
};
```

- [ ] **Step 2: Add DTO types and `useUpdateMeetingAccess` mutation to `useMeetingTasks.ts`**

Open `frontend/src/api/hooks/useMeetingTasks.ts`.

**Add these type definitions** after the existing DTO interfaces (after `MeetingUsersResponse`):

```typescript
export interface MeetingAccessGrantDto {
  userEmail: string;
  userDisplayName: string | null;
}

export interface UpdateMeetingAccessRequest {
  transcriptId: string;
  accessLevel: 'Private' | 'Public' | 'Restricted';
  restrictedUserEmails: string[];
}

export interface UpdateMeetingAccessResponse {
  success: boolean;
  accessLevel: string;
  grants: MeetingAccessGrantDto[];
}
```

**Also update `MeetingTranscriptDto`** to include the new fields:
```typescript
export interface MeetingTranscriptDto {
  // ... existing fields ...
  accessLevel: 'Private' | 'Public' | 'Restricted';
  accessGrants: MeetingAccessGrantDto[];
}
```

**Add the mutation hook** after the existing `useExplainMeetingSummary` export:

```typescript
export const useUpdateMeetingAccess = () => {
  const queryClient = useQueryClient();
  const apiClient = getAuthenticatedApiClient();

  return useMutation({
    mutationFn: async (request: UpdateMeetingAccessRequest): Promise<UpdateMeetingAccessResponse> => {
      const { transcriptId, ...body } = request;
      const url = `${apiClient.baseUrl}/api/meeting-tasks/${transcriptId}/access`;
      const response = await fetch(url, {
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${await apiClient.getToken()}`,
        },
        body: JSON.stringify(body),
      });
      if (!response.ok) throw new Error(`Failed to update access: ${response.status}`);
      return response.json();
    },
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: MEETING_TASKS_KEYS.detail(variables.transcriptId) });
      queryClient.invalidateQueries({ queryKey: MEETING_TASKS_KEYS.list });
    },
  });
};
```

- [ ] **Step 3: Create `ManageAccessModal.tsx`**

Create `frontend/src/components/pages/automation/access/ManageAccessModal.tsx`:

```tsx
import React, { useState, useEffect } from 'react';
import Select from 'react-select';
import { MeetingTranscriptDto, MeetingUserDto, useUpdateMeetingAccess } from '../../../../api/hooks/useMeetingTasks';

interface ManageAccessModalProps {
  isOpen: boolean;
  onClose: () => void;
  transcript: MeetingTranscriptDto;
  users: MeetingUserDto[];
}

type AccessLevel = 'Private' | 'Public' | 'Restricted';

interface UserOption {
  value: string;
  label: string;
}

export const ManageAccessModal: React.FC<ManageAccessModalProps> = ({
  isOpen,
  onClose,
  transcript,
  users,
}) => {
  const [accessLevel, setAccessLevel] = useState<AccessLevel>(
    (transcript.accessLevel as AccessLevel) ?? 'Private'
  );
  const [selectedEmails, setSelectedEmails] = useState<string[]>(
    transcript.accessGrants?.map((g) => g.userEmail) ?? []
  );

  const { mutate: updateAccess, isPending, error } = useUpdateMeetingAccess();

  useEffect(() => {
    if (isOpen) {
      setAccessLevel((transcript.accessLevel as AccessLevel) ?? 'Private');
      setSelectedEmails(transcript.accessGrants?.map((g) => g.userEmail) ?? []);
    }
  }, [isOpen, transcript]);

  const userOptions: UserOption[] = users.map((u) => ({
    value: u.email,
    label: `${u.displayName} (${u.email})`,
  }));

  const selectedOptions = userOptions.filter((o) => selectedEmails.includes(o.value));

  const handleSave = () => {
    updateAccess(
      {
        transcriptId: transcript.id,
        accessLevel,
        restrictedUserEmails: accessLevel === 'Restricted' ? selectedEmails : [],
      },
      { onSuccess: onClose }
    );
  };

  if (!isOpen) return null;

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40"
      onClick={onClose}
    >
      <div
        className="bg-white rounded-xl shadow-lg p-6 w-full max-w-md"
        onClick={(e) => e.stopPropagation()}
      >
        <h2 className="text-lg font-semibold mb-4">Spravovat přístup ke schůzce</h2>

        <fieldset className="mb-4 space-y-2">
          {(['Private', 'Public', 'Restricted'] as AccessLevel[]).map((level) => (
            <label key={level} className="flex items-center gap-2 cursor-pointer">
              <input
                type="radio"
                name="accessLevel"
                value={level}
                checked={accessLevel === level}
                onChange={() => setAccessLevel(level)}
              />
              <span className="text-sm">
                {level === 'Private' && 'Soukromé — vidí pouze správci schůzek'}
                {level === 'Public' && 'Veřejné — vidí všichni přihlášení uživatelé'}
                {level === 'Restricted' && 'Omezené — vidí pouze vybraní uživatelé'}
              </span>
            </label>
          ))}
        </fieldset>

        {accessLevel === 'Restricted' && (
          <div className="mb-4">
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Oprávnění uživatelé
            </label>
            <Select
              isMulti
              options={userOptions}
              value={selectedOptions}
              onChange={(opts) => setSelectedEmails(opts.map((o) => o.value))}
              placeholder="Vyberte uživatele..."
              menuPortalTarget={document.body}
              styles={{ menuPortal: (base) => ({ ...base, zIndex: 9999 }) }}
              classNamePrefix="react-select"
            />
          </div>
        )}

        {error && (
          <p className="text-sm text-red-600 mb-3">Nepodařilo se uložit přístup. Zkuste to znovu.</p>
        )}

        <div className="flex justify-end gap-2 mt-4">
          <button
            onClick={onClose}
            className="px-4 py-2 text-sm rounded-lg border border-gray-300 hover:bg-gray-50"
          >
            Zrušit
          </button>
          <button
            onClick={handleSave}
            disabled={isPending || (accessLevel === 'Restricted' && selectedEmails.length === 0)}
            className="px-4 py-2 text-sm rounded-lg bg-blue-600 text-white hover:bg-blue-700 disabled:opacity-50"
          >
            {isPending ? 'Ukládám...' : 'Uložit'}
          </button>
        </div>
      </div>
    </div>
  );
};
```

- [ ] **Step 4: Update `MeetingTaskDetailPage.tsx` to show badge and "Spravovat přístup" button**

Open `frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx`.

**Add imports** near the top:
```typescript
import { useMeetingManagerPermission } from '../../../api/hooks/useMeetingManagerPermission';
import { useMeetingUsers } from '../../../api/hooks/useMeetingTasks';
import { ManageAccessModal } from './access/ManageAccessModal';
```

**Add state** inside the component (alongside existing `useState` declarations):
```typescript
const [accessModalOpen, setAccessModalOpen] = useState(false);
const isMeetingManager = useMeetingManagerPermission();
const { data: meetingUsers } = useMeetingUsers();
```

**Add access-level badge and button** in the header section, alongside the existing status badge. Find the element rendering the status badge (it will contain `transcript.status` or similar) and add after it:

```tsx
{/* Access level badge */}
<span className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${
  transcript.accessLevel === 'Public'
    ? 'bg-green-100 text-green-800'
    : transcript.accessLevel === 'Restricted'
    ? 'bg-yellow-100 text-yellow-800'
    : 'bg-gray-100 text-gray-600'
}`}>
  {transcript.accessLevel === 'Private' && 'Soukromé'}
  {transcript.accessLevel === 'Public' && 'Veřejné'}
  {transcript.accessLevel === 'Restricted' && 'Omezené'}
</span>

{/* Manager-only access button */}
{isMeetingManager && (
  <button
    onClick={() => setAccessModalOpen(true)}
    className="ml-2 px-3 py-1 text-sm rounded-lg border border-gray-300 hover:bg-gray-50"
  >
    Spravovat přístup
  </button>
)}
```

**Add the modal** at the bottom of the JSX return (before the closing `</div>`):
```tsx
{isMeetingManager && accessModalOpen && transcript && (
  <ManageAccessModal
    isOpen={accessModalOpen}
    onClose={() => setAccessModalOpen(false)}
    transcript={transcript}
    users={meetingUsers ?? []}
  />
)}
```

- [ ] **Step 5: Build frontend**

```bash
cd frontend && npm run build
```

Expected: 0 errors.

- [ ] **Step 6: Run frontend linter**

```bash
cd frontend && npm run lint
```

Expected: 0 errors.

- [ ] **Step 7: Start dev server and test manually**

```bash
cd frontend && npm start
```

Open the meeting tasks page. Verify:
1. The access-level badge appears in each transcript's header.
2. "Spravovat přístup" button is visible (mock user has `meeting_manager` role).
3. Clicking the button opens the modal.
4. Selecting "Restricted" shows the user picker.
5. Save calls the API and closes the modal.
6. The badge updates after save (query invalidated).

- [ ] **Step 8: Commit**

```bash
git add frontend/src/api/hooks/useMeetingManagerPermission.ts \
        frontend/src/api/hooks/useMeetingTasks.ts \
        frontend/src/components/pages/automation/access/ManageAccessModal.tsx \
        frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx
git commit -m "feat: add meeting access UI — permission hook, mutation, ManageAccessModal, detail badge"
```

---

## Task 13: Frontend Tests

**Files:**
- Create: `frontend/src/api/hooks/useMeetingManagerPermission.test.ts`
- Create: `frontend/src/components/pages/automation/access/ManageAccessModal.test.tsx`

- [ ] **Step 1: Write `useMeetingManagerPermission` hook test**

Create `frontend/src/api/hooks/useMeetingManagerPermission.test.ts`:

```typescript
import { renderHook } from '@testing-library/react';
import { useMeetingManagerPermission } from './useMeetingManagerPermission';

jest.mock('../../config/runtimeConfig', () => ({
  shouldUseMockAuth: jest.fn(),
}));
jest.mock('../../auth/mockAuth', () => ({
  mockAuthService: { getUser: jest.fn() },
}));
jest.mock('@azure/msal-react', () => ({
  useMsal: jest.fn(),
}));

import { shouldUseMockAuth } from '../../config/runtimeConfig';
import { mockAuthService } from '../../auth/mockAuth';
import { useMsal } from '@azure/msal-react';

describe('useMeetingManagerPermission', () => {
  it('returns true when mock user has meeting_manager role', () => {
    (shouldUseMockAuth as jest.Mock).mockReturnValue(true);
    (mockAuthService.getUser as jest.Mock).mockReturnValue({ roles: ['meeting_manager'] });

    const { result } = renderHook(() => useMeetingManagerPermission());

    expect(result.current).toBe(true);
  });

  it('returns false when mock user does not have meeting_manager role', () => {
    (shouldUseMockAuth as jest.Mock).mockReturnValue(true);
    (mockAuthService.getUser as jest.Mock).mockReturnValue({ roles: ['heblo_user'] });

    const { result } = renderHook(() => useMeetingManagerPermission());

    expect(result.current).toBe(false);
  });

  it('returns false when mock user is null', () => {
    (shouldUseMockAuth as jest.Mock).mockReturnValue(true);
    (mockAuthService.getUser as jest.Mock).mockReturnValue(null);

    const { result } = renderHook(() => useMeetingManagerPermission());

    expect(result.current).toBe(false);
  });

  it('returns true when real MSAL account has meeting_manager role', () => {
    (shouldUseMockAuth as jest.Mock).mockReturnValue(false);
    (useMsal as jest.Mock).mockReturnValue({
      accounts: [{ idTokenClaims: { roles: ['meeting_manager'] } }],
    });

    const { result } = renderHook(() => useMeetingManagerPermission());

    expect(result.current).toBe(true);
  });

  it('returns false when real MSAL account lacks meeting_manager role', () => {
    (shouldUseMockAuth as jest.Mock).mockReturnValue(false);
    (useMsal as jest.Mock).mockReturnValue({
      accounts: [{ idTokenClaims: { roles: ['heblo_user'] } }],
    });

    const { result } = renderHook(() => useMeetingManagerPermission());

    expect(result.current).toBe(false);
  });
});
```

- [ ] **Step 2: Write `ManageAccessModal` render test**

Create `frontend/src/components/pages/automation/access/ManageAccessModal.test.tsx`:

```tsx
import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { ManageAccessModal } from './ManageAccessModal';
import { MeetingTranscriptDto, MeetingUserDto } from '../../../../api/hooks/useMeetingTasks';

jest.mock('../../../../api/hooks/useMeetingTasks', () => ({
  ...jest.requireActual('../../../../api/hooks/useMeetingTasks'),
  useUpdateMeetingAccess: jest.fn(),
}));

import { useUpdateMeetingAccess } from '../../../../api/hooks/useMeetingTasks';

const mockTranscript: Partial<MeetingTranscriptDto> = {
  id: 'test-id',
  subject: 'Test Meeting',
  accessLevel: 'Private',
  accessGrants: [],
};

const mockUsers: MeetingUserDto[] = [
  { email: 'alice@test.com', displayName: 'Alice', aliases: [] },
  { email: 'bob@test.com', displayName: 'Bob', aliases: [] },
];

describe('ManageAccessModal', () => {
  const mockMutate = jest.fn();

  beforeEach(() => {
    (useUpdateMeetingAccess as jest.Mock).mockReturnValue({
      mutate: mockMutate,
      isPending: false,
      error: null,
    });
  });

  it('renders nothing when isOpen is false', () => {
    render(
      <ManageAccessModal
        isOpen={false}
        onClose={jest.fn()}
        transcript={mockTranscript as MeetingTranscriptDto}
        users={mockUsers}
      />
    );
    expect(screen.queryByText('Spravovat přístup ke schůzce')).not.toBeInTheDocument();
  });

  it('renders modal with access level radios when open', () => {
    render(
      <ManageAccessModal
        isOpen={true}
        onClose={jest.fn()}
        transcript={mockTranscript as MeetingTranscriptDto}
        users={mockUsers}
      />
    );
    expect(screen.getByText('Spravovat přístup ke schůzce')).toBeInTheDocument();
    expect(screen.getByLabelText(/Soukromé/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Veřejné/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Omezené/)).toBeInTheDocument();
  });

  it('calls mutate with Public access on save', () => {
    render(
      <ManageAccessModal
        isOpen={true}
        onClose={jest.fn()}
        transcript={mockTranscript as MeetingTranscriptDto}
        users={mockUsers}
      />
    );

    fireEvent.click(screen.getByLabelText(/Veřejné/));
    fireEvent.click(screen.getByText('Uložit'));

    expect(mockMutate).toHaveBeenCalledWith(
      expect.objectContaining({ accessLevel: 'Public', restrictedUserEmails: [] }),
      expect.anything()
    );
  });

  it('disables Save button when Restricted with no users selected', () => {
    render(
      <ManageAccessModal
        isOpen={true}
        onClose={jest.fn()}
        transcript={mockTranscript as MeetingTranscriptDto}
        users={mockUsers}
      />
    );

    fireEvent.click(screen.getByLabelText(/Omezené/));

    expect(screen.getByText('Uložit')).toBeDisabled();
  });
});
```

- [ ] **Step 3: Run frontend tests**

```bash
cd frontend && npm test -- --testPathPattern="useMeetingManagerPermission|ManageAccessModal" --watchAll=false
```

Expected: All tests PASS.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/api/hooks/useMeetingManagerPermission.test.ts \
        frontend/src/components/pages/automation/access/ManageAccessModal.test.tsx
git commit -m "test: add useMeetingManagerPermission and ManageAccessModal tests"
```

---

## Final Verification

- [ ] **Run all backend tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v minimal
```

Expected: All tests pass.

- [ ] **Run dotnet build + format**

```bash
dotnet build backend/src/Anela.Heblo.sln && dotnet format backend/src/Anela.Heblo.sln --verify-no-changes
```

- [ ] **Run frontend build + lint**

```bash
cd frontend && npm run build && npm run lint
```

- [ ] **Run all frontend tests**

```bash
cd frontend && npm test -- --watchAll=false
```

- [ ] **Manual E2E smoke-test**

1. Start API + frontend with mock auth.
2. As mock manager: navigate to a meeting, verify Private access-level badge, click "Spravovat přístup", switch to Public, save.
3. Verify the badge updates to "Veřejné".
4. Switch to Restricted, select a user, save, verify badge updates to "Omezené".
5. Verify `GET /api/meeting-tasks` returns the correct filtered set.

- [ ] **Document Entra app role step**

Verify `docs/integrations/` or `docs/architecture/environments.md` notes that the `meeting_manager` app role must be created in the Entra app registration — this is an Azure config step, not a code change.

---

## Self-Review

**Spec coverage:**
- ✅ Three access levels (Private / Public / Restricted) — Tasks 1, 3
- ✅ `meeting_manager` gatekeeper role — Tasks 1, 2, 3, 11
- ✅ Existing meetings default to Private — Task 5 (migration column default)
- ✅ List filtering with correct pagination counts — Task 4
- ✅ 6 transcript handlers gated — Task 7
- ✅ No existence leak (access denied = same 404 as missing id) — Tasks 7, 9
- ✅ `UpdateMeetingAccess` handler (manager-only, replaces grants) — Task 9
- ✅ `PUT api/meeting-tasks/{id}/access` endpoint — Task 10
- ✅ `AccessLevel` + `AccessGrants` in DTO — Task 6
- ✅ Mock auth (BE + FE) — Task 11
- ✅ Frontend badge + manager button + ManageAccessModal — Task 12
- ✅ Tests: guard, handler access-denied, UpdateMeetingAccess, repository, permission hook, modal — Tasks 2, 3, 4, 7, 8, 9, 13

**Placeholder scan:** None found — all steps contain actual code.

**Type consistency:** `MeetingAccessGrantDto` (backend class, FE interface) used consistently. `MeetingAccessLevel` enum used by guard, repository, handler, config. `GetListAsync` new signature applied identically in interface, implementation, and handler.
