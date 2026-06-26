# Packing User Selection & Attribution Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a packing operator explicitly choose *who is packing* (from a curated, admin-editable list that may include non-Entra people), record that operator on packed shipments, and switch operator at any time on the Balení station.

**Architecture:** Extend the existing `AppUser` table with `Source` (Entra/Local) and `CanPack` flag, and make `EntraObjectId` nullable so login-less local operators can coexist. The Balení frontend keeps the selected operator in `localStorage` (shared station), exposes it via a React context, prompts for it when entering packing, and shows a switch chip in the Balení header. The scan call sends the selected `packingUserId`; the backend resolves it to a name snapshot and stamps `Package.PackedByUserId` + `Package.PackedBy`. Admin management lives in the existing `/admin/access` Users tab.

**Tech Stack:** .NET 8, MediatR vertical slices, EF Core (PostgreSQL, manual migrations), FluentValidation (manual per-module registration), React + TypeScript + React Query + Tailwind, NSwag-generated API client.

---

## Conventions for this codebase (read before starting)

- **DTOs are classes, never `record`s** (OpenAPI generator). Internal domain types may be records.
- **Every `*Response` must inherit `BaseResponse`** (a reflection contract test fails in CI otherwise). Use `new XResponse()` for success and `new XResponse(ErrorCodes.X)` for failure.
- **Validators are registered manually per-module** (no `AddValidatorsFromAssembly`). Register the `IValidator<T>` *and* the `ValidationBehavior<TReq,TResp>` pipeline in the module's `Add*Module` method — see `PackagingModule.cs` / `AuthorizationModule.cs`.
- **Frontend packaging hooks use hand-written fetch with absolute URLs** (`${apiClient.baseUrl}${relativeUrl}`) — see `usePackages.ts`. Frontend authorization/admin hooks use the **generated client** (`getAuthenticatedApiClient()`) — see `useAccessManagement.ts`. Match the slice you are editing.
- **The TypeScript client is auto-generated on Debug build** of `Anela.Heblo.API`. After adding/changing backend endpoints or DTOs, rebuild the API (Debug) so `frontend/src/api/generated/api-client.ts` regenerates, then the admin hooks/types become available. Generated client enums are **strings** (e.g. `source: "Entra" | "Local"`).
- **`npm run build` is stricter than `npx tsc --noEmit`** — always gate the frontend with `npm run build`, not just `tsc`.
- Migrations are **manual**: generate with the EF tooling, review, and apply by hand to the target DB. They live in `backend/src/Anela.Heblo.Persistence/Migrations/`.

---

## File Structure (what gets created / modified)

**Backend — Domain**
- Modify `backend/src/Anela.Heblo.Domain/Features/Authorization/Entities/AppUser.cs` — add `Source`, `CanPack`, make `EntraObjectId` nullable.
- Create `backend/src/Anela.Heblo.Domain/Features/Authorization/Entities/AppUserSource.cs` — new enum.
- Modify `backend/src/Anela.Heblo.Domain/Features/Authorization/IAuthorizationRepository.cs` — add `GetActivePackingUsersAsync`.
- Modify `backend/src/Anela.Heblo.Domain/Features/Packaging/Package.cs` — add `PackedByUserId`.

**Backend — Persistence**
- Modify `backend/src/Anela.Heblo.Persistence/Features/Authorization/AppUserConfiguration.cs` — map new columns, filtered unique index.
- Modify `backend/src/Anela.Heblo.Persistence/Features/Authorization/AuthorizationRepository.cs` — implement `GetActivePackingUsersAsync`.
- Modify `backend/src/Anela.Heblo.Persistence/Features/Packaging/PackageConfiguration.cs` — map `PackedByUserId`.
- Create migration `..._AddPackingUserSupport.cs` and `..._AddPackedByUserIdToPackage.cs` (or one combined migration).

**Backend — Application (Authorization slice)**
- Modify `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/UserDtos.cs` — extend `AppUserDto`.
- Modify `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetUsers/GetUsersHandler.cs` — map new fields.
- Create `.../UseCases/CreateLocalUser/{CreateLocalUserRequest,Response,Handler,Validator}.cs`.
- Create `.../UseCases/SetUserCanPack/{SetUserCanPackRequest,Response,Handler}.cs`.
- Create `.../UseCases/GetPackingUsers/{GetPackingUsersRequest,Response,Handler}.cs`.
- Modify `.../Features/Authorization/AuthorizationModule.cs` — register the CreateLocalUser validator + behavior.
- Modify `.../UseCases/AssignUserGroups/AssignUserGroupsHandler.cs` and `.../UseCases/SetUserActive/SetUserActiveHandler.cs` — null-guard `InvalidateCache`.

**Backend — Application (Packaging slice)**
- Modify `.../Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderRequest.cs` — add `PackingUserId`.
- Modify `.../ScanPackingOrder/ScanPackingOrderHandler.cs` — resolve packer, stamp `PackedByUserId` + name snapshot.
- Modify `.../Features/Packaging/UseCases/GetPackages/GetPackagesResponse.cs` and `GetPackagesHandler.cs` — surface `PackedByUserId`.

**Backend — API**
- Modify `backend/src/Anela.Heblo.API/Controllers/AuthorizationController.cs` — `POST users/local`, `PUT users/{id}/can-pack`.
- Modify `backend/src/Anela.Heblo.API/Controllers/PackagingController.cs` — `GET packing-users`, accept scan body with `PackingUserId`.

**Frontend**
- Create `frontend/src/components/baleni/packingUser/PackingUserContext.tsx` — context + localStorage + picker state.
- Create `frontend/src/components/baleni/packingUser/usePackingUsers.ts` — fetch active packers.
- Create `frontend/src/components/baleni/packingUser/PackingUserPicker.tsx` — modal picker.
- Create `frontend/src/components/baleni/packingUser/PackingUserChip.tsx` — header chip.
- Modify `frontend/src/components/baleni/BaleniLayout.tsx` — wrap in provider, render chip + picker.
- Modify `frontend/src/components/baleni/BaleniPacking.tsx` — prompt when no operator, block scanning.
- Modify `frontend/src/api/hooks/useScanPackingOrder.ts` — send `packingUserId` in body.
- Modify `frontend/src/api/hooks/usePackages.ts` — add `packedByUserId` to `PackageDto`.
- Modify `frontend/src/components/baleni/zasilky/ZasilkyTable.tsx` — add "Zabalil" column.
- Modify `frontend/src/api/hooks/useAccessManagement.ts` — `useCreateLocalUser`, `useSetUserCanPack`.
- Modify `frontend/src/pages/AccessManagementPage.tsx` — Can-pack toggle, Create-local-operator action, Local badge.

---

## Phase A — Extend the AppUser model

### Task A1: Add the `AppUserSource` enum

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Authorization/Entities/AppUserSource.cs`

- [ ] **Step 1: Create the enum**

```csharp
namespace Anela.Heblo.Domain.Features.Authorization.Entities;

/// <summary>Where an AppUser originates. Entra users have an EntraObjectId and can log in;
/// Local users are login-less packing operators created by an administrator.</summary>
public enum AppUserSource
{
    Entra,
    Local,
}
```

- [ ] **Step 2: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Authorization/Entities/AppUserSource.cs
git commit -m "feat(authz): add AppUserSource enum"
```

### Task A2: Extend the `AppUser` entity

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Authorization/Entities/AppUser.cs`

- [ ] **Step 1: Add columns and make EntraObjectId nullable**

Replace the body of `AppUser` with:

```csharp
namespace Anela.Heblo.Domain.Features.Authorization.Entities;

/// <summary>An application user. Entra users are materialized from claims on first login;
/// Local users are login-less packing operators created via administration.</summary>
public class AppUser
{
    public Guid Id { get; set; }
    public string? EntraObjectId { get; set; }
    public string Email { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public AppUserSource Source { get; set; } = AppUserSource.Entra;
    public bool CanPack { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }

    public ICollection<UserGroup> UserGroups { get; set; } = new List<UserGroup>();
}
```

- [ ] **Step 2: Build (expect new nullable warnings/errors at call sites — fixed in A3/B-phase)**

Run: `dotnet build backend/src/Anela.Heblo.Domain`
Expected: PASS (the Domain project alone compiles; nullable consumers live in other projects).

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Authorization/Entities/AppUser.cs
git commit -m "feat(authz): add Source/CanPack and nullable EntraObjectId to AppUser"
```

### Task A3: Null-guard the two `InvalidateCache(user.EntraObjectId)` call sites

`IPermissionResolver.InvalidateCache(string)` takes a non-null string. Local users have a null `EntraObjectId`, so guard both callers. (Local users never have groups, but guard defensively so the code compiles and never passes null.)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/AssignUserGroups/AssignUserGroupsHandler.cs:34`
- Modify: `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/SetUserActive/SetUserActiveHandler.cs:26`

- [ ] **Step 1: Guard in AssignUserGroupsHandler**

Replace `_resolver.InvalidateCache(user.EntraObjectId);` with:

```csharp
if (user.EntraObjectId is not null)
    _resolver.InvalidateCache(user.EntraObjectId);
```

- [ ] **Step 2: Guard in SetUserActiveHandler**

Replace `_resolver.InvalidateCache(user.EntraObjectId);` with the same guarded block.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/AssignUserGroups/AssignUserGroupsHandler.cs \
        backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/SetUserActive/SetUserActiveHandler.cs
git commit -m "fix(authz): null-guard InvalidateCache for login-less users"
```

### Task A4: Update EF configuration for `AppUser`

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Features/Authorization/AppUserConfiguration.cs`

- [ ] **Step 1: Map new columns and switch to a filtered unique index**

Replace the `Configure` body with:

```csharp
public void Configure(EntityTypeBuilder<AppUser> builder)
{
    builder.ToTable("AppUsers", "public");
    builder.HasKey(u => u.Id);
    builder.Property(u => u.EntraObjectId).HasMaxLength(100);
    builder.Property(u => u.Email).IsRequired().HasMaxLength(255);
    builder.Property(u => u.DisplayName).IsRequired().HasMaxLength(255);
    builder.Property(u => u.IsActive).IsRequired();
    builder.Property(u => u.Source).IsRequired().HasMaxLength(20).HasConversion<string>();
    builder.Property(u => u.CanPack).IsRequired();
    builder.Property(u => u.CreatedAt).IsRequired();
    // Partial unique index: only Entra users (non-null EntraObjectId) must be unique;
    // multiple Local users carry NULL and must not collide.
    builder.HasIndex(u => u.EntraObjectId).IsUnique().HasFilter("\"EntraObjectId\" IS NOT NULL");
    builder.HasMany(u => u.UserGroups).WithOne(ug => ug.User)
        .HasForeignKey(ug => ug.UserId).OnDelete(DeleteBehavior.Cascade);
}
```

- [ ] **Step 2: Build the persistence project**

Run: `dotnet build backend/src/Anela.Heblo.Persistence`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Features/Authorization/AppUserConfiguration.cs
git commit -m "feat(authz): map Source/CanPack and filtered unique index on AppUsers"
```

### Task A5: Generate & review the AppUser migration

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddPackingUserSupport.cs`

- [ ] **Step 1: Generate the migration**

Run from repo root (adjust the startup/project paths to match how this repo runs EF — see `docs/development/setup.md`):

```bash
dotnet ef migrations add AddPackingUserSupport \
  --project backend/src/Anela.Heblo.Persistence \
  --startup-project backend/src/Anela.Heblo.API
```

- [ ] **Step 2: Review the generated `Up`/`Down`**

Confirm `Up` does all of:
- `AddColumn<string>("Source", ... defaultValue: "Entra", nullable: false)` on `AppUsers` (public schema).
- `AddColumn<bool>("CanPack", ... defaultValue: false, nullable: false)`.
- `AlterColumn<string>("EntraObjectId", ... nullable: true)`.
- Drops the old unique index on `EntraObjectId` and recreates it with `filter: "\"EntraObjectId\" IS NOT NULL"`.

If the generator did not emit the filtered index, edit the migration to drop and recreate it manually:

```csharp
migrationBuilder.DropIndex(name: "IX_AppUsers_EntraObjectId", schema: "public", table: "AppUsers");
migrationBuilder.CreateIndex(
    name: "IX_AppUsers_EntraObjectId", schema: "public", table: "AppUsers",
    column: "EntraObjectId", unique: true, filter: "\"EntraObjectId\" IS NOT NULL");
```

- [ ] **Step 3: Build**

Run: `dotnet build backend/src/Anela.Heblo.Persistence`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Migrations/
git commit -m "feat(authz): migration for AppUser packing-user support"
```

---

## Phase B — Admin endpoints: manage packers

### Task B1: Extend `AppUserDto`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/UserDtos.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetUsers/GetUsersHandler.cs`

- [ ] **Step 1: Add fields to the DTO and make EntraObjectId nullable**

In `UserDtos.cs`, change `AppUserDto`:

```csharp
public class AppUserDto
{
    public Guid Id { get; set; }
    public string? EntraObjectId { get; set; }
    public string Email { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public bool IsActive { get; set; }
    public string Source { get; set; } = null!;
    public bool CanPack { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public List<Guid> GroupIds { get; set; } = new();
}
```

- [ ] **Step 2: Map the new fields in `GetUsersHandler`**

In the `Select(u => new AppUserDto { ... })`, add:

```csharp
Source = u.Source.ToString(),
CanPack = u.CanPack,
```

(`EntraObjectId = u.EntraObjectId` now compiles since the DTO field is nullable.)

- [ ] **Step 3: Build**

Run: `dotnet build backend/src/Anela.Heblo.Application`
Expected: PASS (also fixes the `AddGroupMemberHandler` DTO mapping, which assigns a non-null value into the now-nullable field — still valid).

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/UserDtos.cs \
        backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetUsers/GetUsersHandler.cs
git commit -m "feat(authz): expose Source/CanPack on AppUserDto"
```

### Task B2: `GetActivePackingUsersAsync` on the repository

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Authorization/IAuthorizationRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Features/Authorization/AuthorizationRepository.cs`
- Test: `backend/test/Anela.Heblo.Tests/Authorization/AuthorizationRepositoryPackingUsersTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Authorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class AuthorizationRepositoryPackingUsersTests
{
    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task GetActivePackingUsersAsync_ReturnsOnlyActiveCanPackUsers_OrderedByName()
    {
        await using var db = NewDb();
        db.AppUsers.AddRange(
            new AppUser { Id = Guid.NewGuid(), Email = "z@x.cz", DisplayName = "Zoe", IsActive = true, CanPack = true, Source = AppUserSource.Local, CreatedAt = DateTimeOffset.UtcNow },
            new AppUser { Id = Guid.NewGuid(), Email = "a@x.cz", DisplayName = "Ada", IsActive = true, CanPack = true, Source = AppUserSource.Entra, EntraObjectId = "oid-a", CreatedAt = DateTimeOffset.UtcNow },
            new AppUser { Id = Guid.NewGuid(), Email = "n@x.cz", DisplayName = "NonPacker", IsActive = true, CanPack = false, Source = AppUserSource.Entra, EntraObjectId = "oid-n", CreatedAt = DateTimeOffset.UtcNow },
            new AppUser { Id = Guid.NewGuid(), Email = "i@x.cz", DisplayName = "Inactive", IsActive = false, CanPack = true, Source = AppUserSource.Local, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var repo = new AuthorizationRepository(db);
        var result = await repo.GetActivePackingUsersAsync();

        result.Select(u => u.DisplayName).Should().Equal("Ada", "Zoe");
    }
}
```

- [ ] **Step 2: Run it — expect failure (method missing)**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter GetActivePackingUsersAsync_ReturnsOnlyActiveCanPackUsers_OrderedByName`
Expected: FAIL to compile — `GetActivePackingUsersAsync` does not exist.

- [ ] **Step 3: Add to the interface**

In `IAuthorizationRepository.cs`, under `// Users`, add:

```csharp
Task<List<AppUser>> GetActivePackingUsersAsync(CancellationToken ct = default);
```

- [ ] **Step 4: Implement in the repository**

In `AuthorizationRepository.cs`, add:

```csharp
public async Task<List<AppUser>> GetActivePackingUsersAsync(CancellationToken ct = default) =>
    await _db.AppUsers.AsNoTracking()
        .Where(u => u.IsActive && u.CanPack)
        .OrderBy(u => u.DisplayName)
        .ToListAsync(ct);
```

- [ ] **Step 5: Run the test — expect PASS**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter GetActivePackingUsersAsync_ReturnsOnlyActiveCanPackUsers_OrderedByName`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Authorization/IAuthorizationRepository.cs \
        backend/src/Anela.Heblo.Persistence/Features/Authorization/AuthorizationRepository.cs \
        backend/test/Anela.Heblo.Tests/Authorization/AuthorizationRepositoryPackingUsersTests.cs
git commit -m "feat(authz): repository query for active packing users"
```

### Task B3: `SetUserCanPack` use case

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/SetUserCanPack/SetUserCanPackRequest.cs`
- Create: `.../SetUserCanPack/SetUserCanPackResponse.cs`
- Create: `.../SetUserCanPack/SetUserCanPackHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Authorization/SetUserCanPackHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Anela.Heblo.Application.Features.Authorization.UseCases.SetUserCanPack;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Authorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class SetUserCanPackHandlerTests
{
    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task Handle_SetsCanPack_WhenUserExists()
    {
        await using var db = NewDb();
        var id = Guid.NewGuid();
        db.AppUsers.Add(new AppUser { Id = id, Email = "u@x.cz", DisplayName = "U", IsActive = true, CanPack = false, Source = AppUserSource.Entra, EntraObjectId = "oid", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var handler = new SetUserCanPackHandler(new AuthorizationRepository(db));
        var result = await handler.Handle(new SetUserCanPackRequest { UserId = id, CanPack = true }, default);

        result.Success.Should().BeTrue();
        (await db.AppUsers.FindAsync(id))!.CanPack.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenUserMissing()
    {
        await using var db = NewDb();
        var handler = new SetUserCanPackHandler(new AuthorizationRepository(db));
        var result = await handler.Handle(new SetUserCanPackRequest { UserId = Guid.NewGuid(), CanPack = true }, default);
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.AuthorizationUserNotFound);
    }
}
```

- [ ] **Step 2: Run it — expect failure (types missing)**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter SetUserCanPackHandlerTests`
Expected: FAIL to compile.

- [ ] **Step 3: Create the request**

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.SetUserCanPack;

public class SetUserCanPackRequest : IRequest<SetUserCanPackResponse>
{
    public Guid UserId { get; set; }
    public bool CanPack { get; set; }
}
```

- [ ] **Step 4: Create the response**

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.SetUserCanPack;

public class SetUserCanPackResponse : BaseResponse
{
    public SetUserCanPackResponse() { }
    public SetUserCanPackResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

- [ ] **Step 5: Create the handler**

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.SetUserCanPack;

public class SetUserCanPackHandler : IRequestHandler<SetUserCanPackRequest, SetUserCanPackResponse>
{
    private readonly IAuthorizationRepository _repo;
    public SetUserCanPackHandler(IAuthorizationRepository repo) => _repo = repo;

    public async Task<SetUserCanPackResponse> Handle(SetUserCanPackRequest request, CancellationToken ct)
    {
        var user = await _repo.GetUserByIdAsync(request.UserId, ct);
        if (user is null)
            return new SetUserCanPackResponse(ErrorCodes.AuthorizationUserNotFound);

        user.CanPack = request.CanPack;
        await _repo.SaveChangesAsync(ct);
        return new SetUserCanPackResponse();
    }
}
```

- [ ] **Step 6: Run the tests — expect PASS**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter SetUserCanPackHandlerTests`
Expected: PASS. (If `ErrorCodes.AuthorizationUserNotFound` is not the actual member name, grep `ErrorCodes` and use the same code `SetUserActiveHandler` returns.)

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/SetUserCanPack/ \
        backend/test/Anela.Heblo.Tests/Authorization/SetUserCanPackHandlerTests.cs
git commit -m "feat(authz): SetUserCanPack use case"
```

### Task B4: `CreateLocalUser` use case (with validation)

**Files:**
- Create: `.../UseCases/CreateLocalUser/CreateLocalUserRequest.cs`
- Create: `.../UseCases/CreateLocalUser/CreateLocalUserResponse.cs`
- Create: `.../UseCases/CreateLocalUser/CreateLocalUserHandler.cs`
- Create: `.../UseCases/CreateLocalUser/CreateLocalUserValidator.cs`
- Modify: `.../Features/Authorization/AuthorizationModule.cs`
- Test: `backend/test/Anela.Heblo.Tests/Authorization/CreateLocalUserHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Anela.Heblo.Application.Features.Authorization.UseCases.CreateLocalUser;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Authorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class CreateLocalUserHandlerTests
{
    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task Handle_CreatesLocalPacker_WithNullEntraId()
    {
        await using var db = NewDb();
        var handler = new CreateLocalUserHandler(new AuthorizationRepository(db));

        var result = await handler.Handle(new CreateLocalUserRequest { DisplayName = "  Pepa  " }, default);

        result.Success.Should().BeTrue();
        result.User!.Source.Should().Be(nameof(AppUserSource.Local));
        result.User.CanPack.Should().BeTrue();
        var saved = await db.AppUsers.SingleAsync();
        saved.DisplayName.Should().Be("Pepa");          // trimmed
        saved.EntraObjectId.Should().BeNull();
        saved.IsActive.Should().BeTrue();
        saved.Source.Should().Be(AppUserSource.Local);
    }
}
```

- [ ] **Step 2: Run it — expect failure (types missing)**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter CreateLocalUserHandlerTests`
Expected: FAIL to compile.

- [ ] **Step 3: Create the request**

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.CreateLocalUser;

public class CreateLocalUserRequest : IRequest<CreateLocalUserResponse>
{
    public string DisplayName { get; set; } = null!;
}
```

- [ ] **Step 4: Create the response (reuse `AppUserDto`)**

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.CreateLocalUser;

public class CreateLocalUserResponse : BaseResponse
{
    public UseCases.AppUserDto? User { get; set; }

    public CreateLocalUserResponse() { }
    public CreateLocalUserResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

- [ ] **Step 5: Create the handler**

```csharp
using Anela.Heblo.Application.Features.Authorization.UseCases;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.CreateLocalUser;

public class CreateLocalUserHandler : IRequestHandler<CreateLocalUserRequest, CreateLocalUserResponse>
{
    private readonly IAuthorizationRepository _repo;
    public CreateLocalUserHandler(IAuthorizationRepository repo) => _repo = repo;

    public async Task<CreateLocalUserResponse> Handle(CreateLocalUserRequest request, CancellationToken ct)
    {
        var name = request.DisplayName.Trim();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            EntraObjectId = null,
            Email = string.Empty,
            DisplayName = name,
            IsActive = true,
            Source = AppUserSource.Local,
            CanPack = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await _repo.AddUserAsync(user, ct);
        await _repo.SaveChangesAsync(ct);

        return new CreateLocalUserResponse
        {
            User = new AppUserDto
            {
                Id = user.Id,
                EntraObjectId = null,
                Email = user.Email,
                DisplayName = user.DisplayName,
                IsActive = user.IsActive,
                Source = user.Source.ToString(),
                CanPack = user.CanPack,
                GroupIds = new List<Guid>(),
            },
        };
    }
}
```

> Note: `Email` is non-nullable in the DB (`IsRequired`). Local operators have none, so we store `string.Empty`. Keep that as-is unless the schema is relaxed.

- [ ] **Step 6: Create the validator**

```csharp
using FluentValidation;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.CreateLocalUser;

public class CreateLocalUserValidator : AbstractValidator<CreateLocalUserRequest>
{
    public CreateLocalUserValidator()
    {
        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Display name is required.")
            .MaximumLength(255);
    }
}
```

- [ ] **Step 7: Register the validator + behavior in `AuthorizationModule`**

Add to `AddAuthorizationModule` (mirror the existing `AddGroupMember` registration):

```csharp
services.AddScoped<IValidator<CreateLocalUserRequest>, CreateLocalUserValidator>();
services.AddTransient<IPipelineBehavior<CreateLocalUserRequest, CreateLocalUserResponse>,
    ValidationBehavior<CreateLocalUserRequest, CreateLocalUserResponse>>();
```

Add the `using Anela.Heblo.Application.Features.Authorization.UseCases.CreateLocalUser;` at the top.

- [ ] **Step 8: Run the test — expect PASS**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter CreateLocalUserHandlerTests`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/CreateLocalUser/ \
        backend/src/Anela.Heblo.Application/Features/Authorization/AuthorizationModule.cs \
        backend/test/Anela.Heblo.Tests/Authorization/CreateLocalUserHandlerTests.cs
git commit -m "feat(authz): CreateLocalUser use case with validation"
```

### Task B5: Expose the admin endpoints on `AuthorizationController`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/AuthorizationController.cs`

- [ ] **Step 1: Add endpoints**

Add `using` lines for the two new use case namespaces, then add inside the controller:

```csharp
[HttpPost("users/local")]
[Authorize(Roles = AccessRoles.AdministrationWrite)]
public async Task<ActionResult<CreateLocalUserResponse>> CreateLocalUser([FromBody] CreateLocalUserRequest request, CancellationToken ct)
    => HandleResponse(await _mediator.Send(request, ct));

[HttpPut("users/{id:guid}/can-pack")]
[Authorize(Roles = AccessRoles.AdministrationWrite)]
public async Task<ActionResult<SetUserCanPackResponse>> SetCanPack([FromRoute] Guid id, [FromBody] SetUserCanPackRequest request, CancellationToken ct)
{
    request.UserId = id;
    return HandleResponse(await _mediator.Send(request, ct));
}
```

- [ ] **Step 2: Build the API (Debug) — this also regenerates the TS client**

Run: `dotnet build backend/src/Anela.Heblo.API`
Expected: PASS, and `frontend/src/api/generated/api-client.ts` now contains `authorization_CreateLocalUser` and `authorization_SetCanPack` plus `CreateLocalUserRequest`/`SetUserCanPackRequest`/updated `AppUserDto`.

- [ ] **Step 3: Commit (include regenerated client)**

```bash
git add backend/src/Anela.Heblo.API/Controllers/AuthorizationController.cs frontend/src/api/generated/
git commit -m "feat(authz): admin endpoints for local users and can-pack toggle"
```

---

## Phase C — Packing-users picker endpoint

### Task C1: `GetPackingUsers` use case (Authorization slice)

The packer list reads `AppUser` (authorization domain), but the endpoint is authorized with `PackagingRead` (so packers without admin rights can load it). Define the use case in the Authorization slice; expose it on `PackagingController`.

**Files:**
- Create: `.../Features/Authorization/UseCases/GetPackingUsers/GetPackingUsersRequest.cs`
- Create: `.../GetPackingUsers/GetPackingUsersResponse.cs`
- Create: `.../GetPackingUsers/GetPackingUsersHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Authorization/GetPackingUsersHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Anela.Heblo.Application.Features.Authorization.UseCases.GetPackingUsers;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Authorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class GetPackingUsersHandlerTests
{
    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task Handle_ReturnsActivePackersOnly()
    {
        await using var db = NewDb();
        db.AppUsers.AddRange(
            new AppUser { Id = Guid.NewGuid(), Email = "a@x.cz", DisplayName = "Ada", IsActive = true, CanPack = true, Source = AppUserSource.Entra, EntraObjectId = "oid-a", CreatedAt = DateTimeOffset.UtcNow },
            new AppUser { Id = Guid.NewGuid(), Email = "n@x.cz", DisplayName = "No", IsActive = true, CanPack = false, Source = AppUserSource.Entra, EntraObjectId = "oid-n", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var handler = new GetPackingUsersHandler(new AuthorizationRepository(db));
        var result = await handler.Handle(new GetPackingUsersRequest(), default);

        result.Users.Should().ContainSingle();
        result.Users[0].DisplayName.Should().Be("Ada");
    }
}
```

- [ ] **Step 2: Run — expect compile failure**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter GetPackingUsersHandlerTests`
Expected: FAIL to compile.

- [ ] **Step 3: Create request/response**

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetPackingUsers;

public class GetPackingUsersRequest : IRequest<GetPackingUsersResponse> { }
```

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetPackingUsers;

public class GetPackingUsersResponse : BaseResponse
{
    public List<PackingUserDto> Users { get; set; } = new();

    public GetPackingUsersResponse() { }
    public GetPackingUsersResponse(ErrorCodes errorCode) : base(errorCode) { }
}

public class PackingUserDto
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = null!;
}
```

- [ ] **Step 4: Create the handler**

```csharp
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetPackingUsers;

public class GetPackingUsersHandler : IRequestHandler<GetPackingUsersRequest, GetPackingUsersResponse>
{
    private readonly IAuthorizationRepository _repo;
    public GetPackingUsersHandler(IAuthorizationRepository repo) => _repo = repo;

    public async Task<GetPackingUsersResponse> Handle(GetPackingUsersRequest request, CancellationToken ct)
    {
        var users = await _repo.GetActivePackingUsersAsync(ct);
        return new GetPackingUsersResponse
        {
            Users = users.Select(u => new PackingUserDto { Id = u.Id, DisplayName = u.DisplayName }).ToList(),
        };
    }
}
```

- [ ] **Step 5: Run — expect PASS**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter GetPackingUsersHandlerTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetPackingUsers/ \
        backend/test/Anela.Heblo.Tests/Authorization/GetPackingUsersHandlerTests.cs
git commit -m "feat(packaging): GetPackingUsers use case"
```

### Task C2: Expose `GET /api/packaging/packing-users`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/PackagingController.cs`

- [ ] **Step 1: Add the endpoint**

Add `using Anela.Heblo.Application.Features.Authorization.UseCases.GetPackingUsers;` and:

```csharp
/// <summary>Active operators eligible for packing (admin-curated, CanPack = true).</summary>
[HttpGet("packing-users")]
public async Task<ActionResult<GetPackingUsersResponse>> GetPackingUsers(CancellationToken cancellationToken)
{
    var response = await _mediator.Send(new GetPackingUsersRequest(), cancellationToken);
    return HandleResponse(response);
}
```

(The controller's class-level `[Authorize(Roles = AccessRoles.PackagingRead)]` already gates this.)

- [ ] **Step 2: Build the API**

Run: `dotnet build backend/src/Anela.Heblo.API`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/PackagingController.cs frontend/src/api/generated/
git commit -m "feat(packaging): expose packing-users endpoint"
```

---

## Phase D — Attribute the packer on packed shipments

### Task D1: Add `PackedByUserId` to `Package` + config + migration

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Packaging/Package.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Features/Packaging/PackageConfiguration.cs`
- Create: migration `..._AddPackedByUserIdToPackage.cs`

- [ ] **Step 1: Add the property**

In `Package.cs`, after `PackedBy`, add:

```csharp
public Guid? PackedByUserId { get; set; }
```

- [ ] **Step 2: Map it (loose reference, indexed; no FK constraint so user deletes never block history)**

In `PackageConfiguration.cs`, after the `PackedBy` line, add:

```csharp
builder.Property(p => p.PackedByUserId);
builder.HasIndex(p => p.PackedByUserId);
```

- [ ] **Step 3: Generate the migration**

```bash
dotnet ef migrations add AddPackedByUserIdToPackage \
  --project backend/src/Anela.Heblo.Persistence \
  --startup-project backend/src/Anela.Heblo.API
```

Confirm `Up` adds a nullable `uuid` column `PackedByUserId` to `Packages` and an index. Build: `dotnet build backend/src/Anela.Heblo.Persistence` → PASS.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Packaging/Package.cs \
        backend/src/Anela.Heblo.Persistence/Features/Packaging/PackageConfiguration.cs \
        backend/src/Anela.Heblo.Persistence/Migrations/
git commit -m "feat(packaging): add PackedByUserId to Package"
```

### Task D2: Accept `PackingUserId` on scan + stamp the packer

**Files:**
- Modify: `.../Packaging/UseCases/ScanPackingOrder/ScanPackingOrderRequest.cs`
- Modify: `.../ScanPackingOrder/ScanPackingOrderHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/...` (add to the existing ScanPackingOrder handler test file if present; otherwise create `ScanPackingOrderPackerTests.cs`)

- [ ] **Step 1: Add the field to the request**

```csharp
public class ScanPackingOrderRequest : IRequest<ScanPackingOrderResponse>
{
    public string OrderCode { get; set; } = null!;
    public Guid? PackingUserId { get; set; }
}
```

- [ ] **Step 2: Inject the authorization repository and resolve the packer**

In `ScanPackingOrderHandler.cs`:
- Add field `private readonly IAuthorizationRepository _authRepo;` and constructor param `IAuthorizationRepository authRepo` (assign it). Add `using Anela.Heblo.Domain.Features.Authorization;`.
- Change `PersistPackagesAsync` to accept the resolved packer. Replace the `packedBy` resolution (currently `var packedBy = _currentUserService.GetCurrentUser().Email;`) by resolving from the request:

```csharp
private async Task<(Guid? userId, string? name)> ResolvePackerAsync(Guid? packingUserId, CancellationToken ct)
{
    if (packingUserId is { } id)
    {
        var user = await _authRepo.GetUserByIdAsync(id, ct);
        if (user is not null)
            return (user.Id, user.DisplayName);
    }
    // Fallback: attribute to the logged-in user (legacy behavior).
    return (null, _currentUserService.GetCurrentUser().Email);
}
```

Thread the request's `PackingUserId` into `PersistPackagesAsync` (pass `request.PackingUserId`), call `ResolvePackerAsync` once before the loop, and set both fields on each `Package`:

```csharp
var (packedByUserId, packedBy) = await ResolvePackerAsync(packingUserId, cancellationToken);
// ... inside new Package { ... }:
PackedByUserId = packedByUserId,
PackedBy = packedBy,
```

- [ ] **Step 3: Write the failing test (packer resolution)**

Create `backend/test/Anela.Heblo.Tests/Packaging/ScanPackingOrderPackerTests.cs` with two cases using mocked `IShipmentClient`/`IPackingOrderClient`/`IEshopOrderClient`/`IPackageRepository`/`ICurrentUserService` and a real or mocked `IAuthorizationRepository`:
  - When `PackingUserId` matches an active packer → persisted `Package.PackedByUserId == id` and `PackedBy == displayName`.
  - When `PackingUserId` is null → `PackedByUserId == null` and `PackedBy == currentUser.Email`.

(Mirror the mocking style already used in the existing packaging handler tests; capture the `Package` passed to `IPackageRepository.AddAsync` via `Callback`/`It.IsAny`. If an existing ScanPackingOrder test fixture exists, extend it instead of duplicating setup.)

- [ ] **Step 4: Run — expect PASS after implementation**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter ScanPackingOrderPacker`
Expected: PASS.

- [ ] **Step 5: Build full backend**

Run: `dotnet build backend` && `dotnet format backend --verify-no-changes` (or `dotnet format backend`)
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ \
        backend/test/Anela.Heblo.Tests/Packaging/
git commit -m "feat(packaging): stamp selected packing user on scanned packages"
```

### Task D3: Surface `PackedByUserId` from GetPackages + pass body on the scan endpoint

**Files:**
- Modify: `.../Packaging/UseCases/GetPackages/GetPackagesResponse.cs`
- Modify: `.../GetPackages/GetPackagesHandler.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/PackagingController.cs`

- [ ] **Step 1: Add `PackedByUserId` to `PackageDto` (response)**

In `GetPackagesResponse.cs`, add to `PackageDto`:

```csharp
public Guid? PackedByUserId { get; set; }
```

- [ ] **Step 2: Map it in `GetPackagesHandler`**

In the `Select(p => new PackageDto { ... })`, add: `PackedByUserId = p.PackedByUserId,`.

- [ ] **Step 3: Accept the packing user on the scan endpoint**

In `PackagingController.cs`, define a small body type and update `ScanOrder`:

```csharp
public class ScanOrderBody
{
    public Guid? PackingUserId { get; set; }
}

[HttpPost("orders/{orderCode}/scan")]
[Authorize(Roles = AccessRoles.PackagingWrite)]
public async Task<ActionResult<ScanPackingOrderResponse>> ScanOrder(
    [FromRoute] string orderCode,
    [FromBody] ScanOrderBody? body,
    CancellationToken cancellationToken)
{
    var response = await _mediator.Send(
        new ScanPackingOrderRequest { OrderCode = orderCode, PackingUserId = body?.PackingUserId },
        cancellationToken);
    return HandleResponse(response);
}
```

> The frontend will always send a JSON body (`{ packingUserId }`), so binding is reliable. `body` is nullable to tolerate an empty body defensively.

- [ ] **Step 4: Build the API (regenerates client)**

Run: `dotnet build backend/src/Anela.Heblo.API`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetPackages/ \
        backend/src/Anela.Heblo.API/Controllers/PackagingController.cs frontend/src/api/generated/
git commit -m "feat(packaging): expose packer id and accept packing user on scan"
```

---

## Phase E — Frontend: select & switch the packing operator

### Task E1: `usePackingUsers` hook (hand-written fetch, absolute URL)

**Files:**
- Create: `frontend/src/components/baleni/packingUser/usePackingUsers.ts`

- [ ] **Step 1: Implement the hook**

```typescript
import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../../../api/client";

export interface PackingUser {
  id: string;
  displayName: string;
}

interface ApiClientWithInternals {
  baseUrl: string;
  http: { fetch(url: RequestInfo, init?: RequestInit): Promise<Response> };
}

export const packingUsersKey = ["packing-users"] as const;

export function usePackingUsers() {
  return useQuery({
    queryKey: packingUsersKey,
    queryFn: async (): Promise<PackingUser[]> => {
      const apiClient = getAuthenticatedApiClient() as unknown as ApiClientWithInternals;
      const url = `${apiClient.baseUrl}/api/packaging/packing-users`;
      const response = await apiClient.http.fetch(url, {
        method: "GET",
        headers: { Accept: "application/json" },
      });
      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }
      const data = (await response.json()) as { users?: PackingUser[] };
      return data.users ?? [];
    },
    staleTime: 60 * 1000,
  });
}
```

- [ ] **Step 2: Commit**

```bash
git add frontend/src/components/baleni/packingUser/usePackingUsers.ts
git commit -m "feat(baleni): hook to load active packing users"
```

### Task E2: `PackingUserContext` with localStorage persistence

**Files:**
- Create: `frontend/src/components/baleni/packingUser/PackingUserContext.tsx`
- Test: `frontend/src/components/baleni/packingUser/__tests__/PackingUserContext.test.tsx`

- [ ] **Step 1: Write the failing test**

```typescript
import { act, renderHook } from "@testing-library/react";
import type { ReactNode } from "react";
import { PackingUserProvider, usePackingUser } from "../PackingUserContext";

const wrapper = ({ children }: { children: ReactNode }) => (
  <PackingUserProvider>{children}</PackingUserProvider>
);

describe("PackingUserContext", () => {
  beforeEach(() => localStorage.clear());

  test("persists the selected operator to localStorage", () => {
    const { result } = renderHook(() => usePackingUser(), { wrapper });
    act(() => result.current.setCurrent({ id: "u1", displayName: "Pepa" }));
    expect(result.current.current).toEqual({ id: "u1", displayName: "Pepa" });
    expect(localStorage.getItem("heblo.baleni.packingUser")).toContain("Pepa");
  });

  test("restores the operator from localStorage on mount", () => {
    localStorage.setItem(
      "heblo.baleni.packingUser",
      JSON.stringify({ id: "u2", displayName: "Jana" }),
    );
    const { result } = renderHook(() => usePackingUser(), { wrapper });
    expect(result.current.current).toEqual({ id: "u2", displayName: "Jana" });
  });

  test("clear removes the operator", () => {
    const { result } = renderHook(() => usePackingUser(), { wrapper });
    act(() => result.current.setCurrent({ id: "u1", displayName: "Pepa" }));
    act(() => result.current.clear());
    expect(result.current.current).toBeNull();
    expect(localStorage.getItem("heblo.baleni.packingUser")).toBeNull();
  });
});
```

- [ ] **Step 2: Run — expect failure (module missing)**

Run: `cd frontend && npm test -- PackingUserContext`
Expected: FAIL (cannot find module).

- [ ] **Step 3: Implement the context**

```typescript
import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useState,
  type ReactNode,
} from "react";

export interface SelectedPackingUser {
  id: string;
  displayName: string;
}

const STORAGE_KEY = "heblo.baleni.packingUser";

interface PackingUserContextValue {
  current: SelectedPackingUser | null;
  setCurrent: (user: SelectedPackingUser) => void;
  clear: () => void;
  isPickerOpen: boolean;
  openPicker: () => void;
  closePicker: () => void;
}

const PackingUserContext = createContext<PackingUserContextValue | null>(null);

function readStored(): SelectedPackingUser | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    return raw ? (JSON.parse(raw) as SelectedPackingUser) : null;
  } catch {
    return null;
  }
}

export function PackingUserProvider({ children }: { children: ReactNode }) {
  const [current, setCurrentState] = useState<SelectedPackingUser | null>(readStored);
  const [isPickerOpen, setPickerOpen] = useState(false);

  const setCurrent = useCallback((user: SelectedPackingUser) => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(user));
    setCurrentState(user);
    setPickerOpen(false);
  }, []);

  const clear = useCallback(() => {
    localStorage.removeItem(STORAGE_KEY);
    setCurrentState(null);
  }, []);

  const value = useMemo<PackingUserContextValue>(
    () => ({
      current,
      setCurrent,
      clear,
      isPickerOpen,
      openPicker: () => setPickerOpen(true),
      closePicker: () => setPickerOpen(false),
    }),
    [current, setCurrent, clear, isPickerOpen],
  );

  return <PackingUserContext.Provider value={value}>{children}</PackingUserContext.Provider>;
}

export function usePackingUser(): PackingUserContextValue {
  const ctx = useContext(PackingUserContext);
  if (!ctx) {
    throw new Error("usePackingUser must be used within a PackingUserProvider");
  }
  return ctx;
}
```

> Note the import casing: React's hook is `useCallback` (lower-c). Use `useCallback`, not `useCallback`/`useCallback` typos — verify against the editor.

- [ ] **Step 4: Run — expect PASS**

Run: `cd frontend && npm test -- PackingUserContext`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/baleni/packingUser/PackingUserContext.tsx \
        frontend/src/components/baleni/packingUser/__tests__/PackingUserContext.test.tsx
git commit -m "feat(baleni): packing-user context with device persistence"
```

### Task E3: `PackingUserPicker` modal

**Files:**
- Create: `frontend/src/components/baleni/packingUser/PackingUserPicker.tsx`

- [ ] **Step 1: Implement the picker**

A modal overlay listing active packers as large quick-select buttons, with a search box that filters when the list is long. On select, calls `setCurrent`. Closeable only when an operator is already set (so the first-time prompt is mandatory).

```typescript
import { useMemo, useState } from "react";
import { Loader2, Search, X } from "lucide-react";
import { usePackingUsers } from "./usePackingUsers";
import { usePackingUser } from "./PackingUserContext";

export function PackingUserPicker() {
  const { isPickerOpen, current, setCurrent, closePicker } = usePackingUser();
  const { data: users, isLoading, isError } = usePackingUsers();
  const [query, setQuery] = useState("");

  const filtered = useMemo(() => {
    const list = users ?? [];
    const q = query.trim().toLowerCase();
    return q ? list.filter((u) => u.displayName.toLowerCase().includes(q)) : list;
  }, [users, query]);

  if (!isPickerOpen) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4">
      <div className="w-full max-w-2xl rounded-xl bg-white p-6 shadow-xl">
        <div className="mb-4 flex items-center justify-between">
          <h2 className="text-lg font-semibold text-neutral-slate">Kdo balí?</h2>
          {current && (
            <button onClick={closePicker} aria-label="Zavřít" className="p-2 text-neutral-gray hover:text-neutral-slate">
              <X className="h-5 w-5" />
            </button>
          )}
        </div>

        {(users?.length ?? 0) > 8 && (
          <div className="mb-4 flex items-center gap-2 rounded-md border px-3 py-2">
            <Search className="h-4 w-4 text-neutral-gray" />
            <input
              autoFocus
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Hledat…"
              className="w-full outline-none"
            />
          </div>
        )}

        {isLoading && (
          <div className="flex items-center gap-2 py-8 text-neutral-gray">
            <Loader2 className="h-5 w-5 animate-spin" /> Načítám…
          </div>
        )}
        {isError && <p className="py-8 text-red-600">Nepodařilo se načíst seznam baličů.</p>}

        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
          {filtered.map((u) => (
            <button
              key={u.id}
              onClick={() => setCurrent({ id: u.id, displayName: u.displayName })}
              className={`rounded-lg border px-4 py-4 text-base font-medium transition-colors ${
                current?.id === u.id
                  ? "border-primary-blue bg-secondary-blue-pale text-primary-blue"
                  : "border-border-light hover:bg-secondary-blue-pale"
              }`}
            >
              {u.displayName}
            </button>
          ))}
        </div>
        {!isLoading && filtered.length === 0 && (
          <p className="py-8 text-center text-neutral-gray">Žádní baliči nejsou k dispozici.</p>
        )}
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Build the frontend**

Run: `cd frontend && npm run build`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/baleni/packingUser/PackingUserPicker.tsx
git commit -m "feat(baleni): packing-user picker modal"
```

### Task E4: `PackingUserChip` header control

**Files:**
- Create: `frontend/src/components/baleni/packingUser/PackingUserChip.tsx`

- [ ] **Step 1: Implement the chip**

```typescript
import { UserRound } from "lucide-react";
import { usePackingUser } from "./PackingUserContext";

export function PackingUserChip() {
  const { current, openPicker } = usePackingUser();
  return (
    <button
      onClick={openPicker}
      className="inline-flex items-center gap-2 rounded-full border border-border-light px-3 py-1.5 text-sm font-medium text-neutral-slate hover:bg-secondary-blue-pale"
      aria-label="Změnit balícího uživatele"
    >
      <UserRound className="h-4 w-4 text-primary-blue" />
      <span>{current ? current.displayName : "Vybrat baliče"}</span>
    </button>
  );
}
```

- [ ] **Step 2: Commit**

```bash
git add frontend/src/components/baleni/packingUser/PackingUserChip.tsx
git commit -m "feat(baleni): packing-user header chip"
```

### Task E5: Wire provider, chip, and picker into `BaleniLayout`

**Files:**
- Modify: `frontend/src/components/baleni/BaleniLayout.tsx`

- [ ] **Step 1: Wrap the layout and render the chip + picker**

Wrap the returned tree in `<PackingUserProvider>`, add `<PackingUserChip />` in the header (left of `<UserProfile />`), and render `<PackingUserPicker />` once inside the provider. Add imports:

```typescript
import { PackingUserProvider } from './packingUser/PackingUserContext';
import { PackingUserChip } from './packingUser/PackingUserChip';
import { PackingUserPicker } from './packingUser/PackingUserPicker';
```

In the header, between the title `<span>` and `<UserProfile compact={true} />`, insert `<PackingUserChip />`. Wrap the whole `<div className="min-h-screen …">` in `<PackingUserProvider> … </PackingUserProvider>` and add `<PackingUserPicker />` just before the closing provider tag.

- [ ] **Step 2: Build**

Run: `cd frontend && npm run build`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/baleni/BaleniLayout.tsx
git commit -m "feat(baleni): mount packing-user provider, chip, and picker"
```

### Task E6: Prompt for operator on the packing screen + block scanning

**Files:**
- Modify: `frontend/src/components/baleni/BaleniPacking.tsx`

- [ ] **Step 1: Open the picker on mount when no operator is set, and gate scanning**

In `BaleniPacking`, read the context and force selection:

```typescript
import { useEffect } from 'react';
import { usePackingUser } from './packingUser/PackingUserContext';
// ...
function BaleniPacking() {
  useScreenView('Baleni', 'BaleniPacking');
  const { current, openPicker } = usePackingUser();
  const scanMutation = useScanPackingOrder();
  const [isShowingDoneView, setIsShowingDoneView] = useState(false);

  useEffect(() => {
    if (!current) openPicker();
  }, [current, openPicker]);

  const handleScan = (value: string) => {
    if (!current) {
      openPicker();
      return;
    }
    scanMutation.mutate(value);
  };
  // ... unchanged render, but pass disabled state to ScanInput when !current:
}
```

Disable the `ScanInput` (or show a hint "Nejprve vyberte baliče") while `current` is null. Use the existing `loading`/disabled affordance on `ScanInput`; if it has no `disabled` prop, gate via `handleScan` early-return (already done) and render a small note above the input when `!current`.

- [ ] **Step 2: Build**

Run: `cd frontend && npm run build`
Expected: PASS. Also run the existing `BaleniPacking.test.tsx` and update it to wrap the component in `PackingUserProvider` (and seed `localStorage` with an operator) so it renders:

Run: `cd frontend && npm test -- BaleniPacking`
Expected: PASS (after wrapping the render in `<PackingUserProvider>` and pre-seeding `localStorage`).

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/baleni/BaleniPacking.tsx frontend/src/components/baleni/__tests__/BaleniPacking.test.tsx
git commit -m "feat(baleni): require operator selection before scanning"
```

### Task E7: Send `packingUserId` with the scan request

**Files:**
- Modify: `frontend/src/api/hooks/useScanPackingOrder.ts`

- [ ] **Step 1: Thread the selected operator into the POST body**

Change `scanPackingOrder` to accept the operator id and send it as the body; update the mutation to read it from context. Two options — implement the cleaner one: change the mutation variable from `string` to `{ orderCode: string; packingUserId: string | null }`.

```typescript
const scanPackingOrder = async (
  orderCode: string,
  packingUserId: string | null,
): Promise<ScanPackingOrderResult> => {
  const apiClient = getAuthenticatedApiClient(false) as unknown as ApiClientWithInternals;
  const response = await apiClient.http.fetch(
    `${apiClient.baseUrl}/api/packaging/orders/${encodeURIComponent(orderCode)}/scan`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ packingUserId }),
    },
  );
  // ... unchanged parsing
};

export const useScanPackingOrder = () =>
  useMutation<ScanPackingOrderResult, Error, { orderCode: string; packingUserId: string | null }>({
    mutationFn: ({ orderCode, packingUserId }) => scanPackingOrder(orderCode, packingUserId),
  });
```

In `BaleniPacking.tsx`, update the call: `scanMutation.mutate({ orderCode: value, packingUserId: current.id })` (guarded by the `!current` early return so `current` is non-null here).

- [ ] **Step 2: Build + tests**

Run: `cd frontend && npm run build && npm test -- BaleniPacking`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/api/hooks/useScanPackingOrder.ts frontend/src/components/baleni/BaleniPacking.tsx
git commit -m "feat(baleni): send selected packing user on scan"
```

---

## Phase F — Frontend: surface packer + admin management

### Task F1: Show the packer in the shipments table

**Files:**
- Modify: `frontend/src/api/hooks/usePackages.ts`
- Modify: `frontend/src/components/baleni/zasilky/ZasilkyTable.tsx`

- [ ] **Step 1: Add `packedByUserId` to `PackageDto`**

In `usePackages.ts`, add to `PackageDto`: `packedByUserId?: string;` (`packedBy?: string` already exists).

- [ ] **Step 2: Add the "Zabalil" column**

In `ZasilkyTable.tsx`, add a header `<th className="px-4 py-3 text-left">Zabalil</th>` before the "Akce" column, a body cell `<td className="px-4 py-3">{p.packedBy ?? "—"}</td>` in the matching position, and bump the empty-state `colSpan` from `7` to `8`.

- [ ] **Step 3: Build + existing tests**

Run: `cd frontend && npm run build && npm test -- Zasilky`
Expected: PASS (update any ZasilkyTable snapshot/column-count assertions under `zasilky/__tests__` to include the new column).

- [ ] **Step 4: Commit**

```bash
git add frontend/src/api/hooks/usePackages.ts frontend/src/components/baleni/zasilky/
git commit -m "feat(baleni): show packer column in shipments table"
```

### Task F2: Admin hooks for can-pack toggle & local user creation

**Files:**
- Modify: `frontend/src/api/hooks/useAccessManagement.ts`

- [ ] **Step 1: Add the two mutations (generated client)**

After `useSetUserActive`, add (names assume the generated client methods `authorization_SetCanPack` and `authorization_CreateLocalUser`; confirm exact generated names after the Debug build and adjust):

```typescript
export const useSetUserCanPack = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, canPack }: { id: string; canPack: boolean }) => {
      const client = getAuthenticatedApiClient();
      return client.authorization_SetCanPack(
        id,
        new SetUserCanPackRequest({ userId: id, canPack }),
      );
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: keys.users });
    },
  });
};

export const useCreateLocalUser = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (displayName: string) => {
      const client = getAuthenticatedApiClient();
      return client.authorization_CreateLocalUser(
        new CreateLocalUserRequest({ displayName }),
      );
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: keys.users });
    },
  });
};
```

Add `SetUserCanPackRequest` and `CreateLocalUserRequest` to the generated-client imports at the top of the file and to the re-export list at the bottom.

- [ ] **Step 2: Build**

Run: `cd frontend && npm run build`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/api/hooks/useAccessManagement.ts
git commit -m "feat(authz): admin hooks for can-pack and local user creation"
```

### Task F3: Admin UI — toggle, create local operator, Local badge

**Files:**
- Modify: `frontend/src/pages/AccessManagementPage.tsx`

- [ ] **Step 1: Wire the new hooks and a create-local form**

Import `useSetUserCanPack` and `useCreateLocalUser`. In the Users tab:
- Add a "Create local operator" affordance (an input + button) above the user list that calls `createLocalUser.mutate(name)`.
- For each user row, render a **Local** badge when `u.source === "Local"`, and a **Can pack** toggle button that calls `setCanPack.mutate({ id: u.id, canPack: !u.canPack })` with label `Can pack` / `Packer` reflecting `u.canPack`.

Example row controls (add inside the existing `<div className="flex items-center gap-2 ml-4">`):

```tsx
{u.source === "Local" && (
  <span className="rounded bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-700">Local</span>
)}
<button
  onClick={() => u.id && setCanPack.mutate({ id: u.id, canPack: !u.canPack })}
  disabled={setCanPack.isPending}
  className={`text-sm ${u.canPack ? "text-indigo-600" : "text-gray-500"} hover:underline`}
  aria-label={`Toggle can pack ${u.displayName}`}
>
  {u.canPack ? "Packer ✓" : "Make packer"}
</button>
```

Create-local control (above the list):

```tsx
<div className="mb-4 flex items-center gap-2">
  <input
    value={newLocalName}
    onChange={(e) => setNewLocalName(e.target.value)}
    placeholder="New local operator name"
    className="flex-1 rounded border border-gray-300 px-3 py-2 text-sm"
  />
  <button
    onClick={() => {
      const name = newLocalName.trim();
      if (name) {
        createLocalUser.mutate(name);
        setNewLocalName("");
      }
    }}
    disabled={createLocalUser.isPending}
    className="rounded bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700"
  >
    Create local operator
  </button>
</div>
```

Add `const [newLocalName, setNewLocalName] = useState("");`, `const setCanPack = useSetUserCanPack();`, `const createLocalUser = useCreateLocalUser();`.

- [ ] **Step 2: Build + lint**

Run: `cd frontend && npm run build && npm run lint`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/pages/AccessManagementPage.tsx
git commit -m "feat(authz): admin UI for packers and local operators"
```

---

## Phase G — Full validation

### Task G1: Backend gate

- [ ] **Step 1:** `dotnet build backend` → PASS
- [ ] **Step 2:** `dotnet format backend` → no diffs (or commit the formatting)
- [ ] **Step 3:** `dotnet test backend/test/Anela.Heblo.Tests` → all green (especially Authorization + Packaging). Confirm the existing `AssignUserGroupsHandlerTests` / `AddGroupMemberHandlerTests` / `PermissionResolverTests` still pass with nullable `EntraObjectId`.

### Task G2: Frontend gate

- [ ] **Step 1:** `cd frontend && npm run build` → PASS
- [ ] **Step 2:** `npm run lint` → PASS
- [ ] **Step 3:** `npm test` → PASS

### Task G3: Migration apply

- [ ] **Step 1:** Apply the two migrations to the target DB (manual; see `docs/development/setup.md` and `memory/` for the connection-string swap). Verify `AppUsers` has `Source`, `CanPack`, nullable `EntraObjectId`, and the filtered unique index; `Packages` has `PackedByUserId`.

### Task G4: End-to-end manual / Playwright (staging)

- [ ] **Admin:** at `/admin/access` → Users tab, create a local operator "Pepa"; confirm it appears with a **Local** badge and `Make packer` → `Packer ✓`. Toggle `Can pack` on an existing Entra user.
- [ ] **Pick:** open `/baleni/baleni` → the picker appears (no operator yet). Pick "Pepa". The header chip shows "Pepa". Scanning is blocked until an operator is chosen.
- [ ] **Pack:** scan an eligible order → shipment created. Open `/baleni/zasilky` → the new "Zabalil" column shows "Pepa" for that row.
- [ ] **Switch:** click the chip → pick a different operator → scan another order → its row shows the new name.
- [ ] **Persist:** reload the page → the chip still shows the last operator (localStorage).
- [ ] **DB:** `SELECT "PackedBy","PackedByUserId" FROM public."Packages" ORDER BY "PackedAt" DESC LIMIT 5;` → `PackedByUserId` is the operator's `AppUsers.Id` and `PackedBy` is the snapshot display name.
- [ ] **E2E suite:** `./scripts/run-playwright-tests.sh` against staging (Balení + access-management modules), adding/adjusting an E2E spec under `frontend/test/e2e/<module>/` if one is warranted (see `docs/testing/e2e-module-guide.md`). E2E suite runs nightly, not in PR CI.

---

## Self-Review notes (resolved)

- **Spec coverage:** selectable curated list (C/E), admin-editable incl. non-Entra (A/B/F), attribution on statistics (D/F1), switch anytime via header chip + device persistence (E2/E4/E5) — all covered.
- **Type consistency:** `AppUserSource` (BE enum) ⇄ `source: string` on `AppUserDto`/generated client; `PackingUser { id, displayName }` (FE) ⇄ `PackingUserDto { Id, DisplayName }` (BE); `SelectedPackingUser { id, displayName }` stored in context; scan body `{ packingUserId }` ⇄ `ScanOrderBody.PackingUserId` ⇄ `ScanPackingOrderRequest.PackingUserId`.
- **Nullable EntraObjectId fallout:** DTO made nullable, two `InvalidateCache` call sites guarded; JIT login path (`AddGroupMemberHandler`, `PermissionResolver`) always sets a non-null id for Entra users, so partial unique index + null locals never collide.
- **YAGNI / out of scope:** per-operator filtering/aggregation dashboards, operator avatars/colors, any global (non-Balení) operator concept.
```
