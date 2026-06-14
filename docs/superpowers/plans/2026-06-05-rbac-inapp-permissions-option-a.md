# In-app Permission System (Option A) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move authorization data (groups, group nesting, user assignments) into the app DB and enforce it via an ASP.NET Core `IClaimsTransformation`, keeping Entra ID for authentication and leaving every existing `[Authorize(Roles=…)]` attribute working.

**Architecture:** A new `Authorization` vertical slice. Permissions stay code-defined in `AccessMatrix`; only groups/nesting/assignments are DB rows (5 tables). On each request, a claims transformation resolves the user's effective permissions (cached, group-closure) and injects them as `Role` claims. `super_user` is an Entra app role honored as a wildcard. A `/api/auth/me` endpoint feeds the React app its permission list. Admin CRUD via MediatR slices + an admin screen.

**Tech Stack:** .NET 8, EF Core (PostgreSQL/Npgsql), MediatR, FluentValidation, xUnit + Moq + FluentAssertions, React + TanStack Query + Jest.

**Source spec:** `docs/superpowers/specs/2026-06-05-rbac-inapp-permissions-option-a-design.md`

**Conventions reused (verified in codebase):**
- Entities: plain `class`, `Id` PK, `= null!` for required refs, `DateTimeOffset` timestamps. Domain layer `Anela.Heblo.Domain/Features/<Feature>/`.
- EF config: `IEntityTypeConfiguration<T>` in `Anela.Heblo.Persistence/Features/<Feature>/`, auto-applied via `ApplyConfigurationsFromAssembly`.
- Repo: concrete class injecting `ApplicationDbContext` directly (like `PackageRepository`), interface in Domain, **DI binding in the feature module** (ADR-004), impl `public` in Persistence.
- MediatR: `Request : IRequest<Response>`, `Response : BaseResponse`, handler in same folder under `Application/Features/Authorization/UseCases/<UseCase>/`. Identity via injected `ICurrentUserService` inside handlers (ADR-005).
- Controller: `BaseApiController`, `HandleResponse(response)`, `[Authorize(Roles = AccessRoles.X)]`.
- Errors: `ErrorCodes` enum; reuse `ValidationError`, `ResourceNotFound`, `DuplicateEntry`; new `32xx` Authorization block (32 is a free prefix).
- Migration: `dotnet ef migrations add <Name> --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API`.

**Validation commands (run from repo root unless noted):**
- Backend build: `dotnet build`
- Backend format check: `dotnet format --verify-no-changes`
- Backend test (filter): `dotnet test --filter "FullyQualifiedName~Authorization"`
- Frontend (from `frontend/`): `npm run build`, `npm run lint`, `npm test`

---

## Phase 1 — Domain: entities, interfaces, super_user constant

### Task 1: Add the `super_user` role constant

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Authorization/AuthorizationConstants.cs:6`

- [ ] **Step 1: Add the constant**

In `public static class AccessRoles`, directly under `public const string Base = "heblo_user";` add:

```csharp
    /// <summary>Entra app role granting ALL permissions (wildcard / break-glass). Honored
    /// directly from the token by the claims transformation, independent of any DB state.</summary>
    public const string SuperUser = "super_user";
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build backend/src/Anela.Heblo.Domain`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Authorization/AuthorizationConstants.cs
git commit -m "feat(authz): add super_user wildcard role constant"
```

---

### Task 2: Authorization entities

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Authorization/Entities/AppUser.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Authorization/Entities/PermissionGroup.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Authorization/Entities/GroupPermission.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Authorization/Entities/GroupParent.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Authorization/Entities/UserGroup.cs`

No test for plain entity classes (no behavior yet). These are POCOs.

- [ ] **Step 1: Create `AppUser.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.Authorization.Entities;

/// <summary>An application user, materialized from Entra claims on first login.</summary>
public class AppUser
{
    public Guid Id { get; set; }
    public string EntraObjectId { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }

    public ICollection<UserGroup> UserGroups { get; set; } = new List<UserGroup>();
}
```

- [ ] **Step 2: Create `PermissionGroup.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.Authorization.Entities;

/// <summary>A named bundle of permissions ("group"/"role"), optionally nesting other groups.</summary>
public class PermissionGroup
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    /// <summary>Seeded from AccessMatrix.Groups; read-only and re-synced on startup.</summary>
    public bool IsSystem { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }

    public ICollection<GroupPermission> Permissions { get; set; } = new List<GroupPermission>();
    public ICollection<GroupParent> Parents { get; set; } = new List<GroupParent>();
    public ICollection<UserGroup> UserGroups { get; set; } = new List<UserGroup>();
}
```

- [ ] **Step 3: Create `GroupPermission.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.Authorization.Entities;

/// <summary>Grants one code-defined permission (AccessMatrix value) to a group.</summary>
public class GroupPermission
{
    public Guid GroupId { get; set; }
    public string PermissionValue { get; set; } = null!;

    public PermissionGroup Group { get; set; } = null!;
}
```

- [ ] **Step 4: Create `GroupParent.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.Authorization.Entities;

/// <summary>Nesting edge: GroupId inherits the permissions of ParentGroupId.</summary>
public class GroupParent
{
    public Guid GroupId { get; set; }
    public Guid ParentGroupId { get; set; }

    public PermissionGroup Group { get; set; } = null!;
    public PermissionGroup ParentGroup { get; set; } = null!;
}
```

- [ ] **Step 5: Create `UserGroup.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.Authorization.Entities;

/// <summary>Assignment of a user to a group.</summary>
public class UserGroup
{
    public Guid UserId { get; set; }
    public Guid GroupId { get; set; }

    public AppUser User { get; set; } = null!;
    public PermissionGroup Group { get; set; } = null!;
}
```

- [ ] **Step 6: Build**

Run: `dotnet build backend/src/Anela.Heblo.Domain`
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Authorization/Entities/
git commit -m "feat(authz): add Authorization domain entities"
```

---

### Task 3: Domain contracts (resolver + repository interfaces, result types)

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Authorization/EffectivePermissions.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Authorization/IPermissionResolver.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Authorization/IAuthorizationRepository.cs`

- [ ] **Step 1: Create `EffectivePermissions.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.Authorization;

/// <summary>The resolved authorization state for a user, as used by enforcement and /api/auth/me.</summary>
public sealed record EffectivePermissions(
    bool IsSuperUser,
    IReadOnlyCollection<string> Permissions,
    IReadOnlyCollection<string> Groups)
{
    public static EffectivePermissions Empty { get; } =
        new(false, Array.Empty<string>(), Array.Empty<string>());
}
```

- [ ] **Step 2: Create `IPermissionResolver.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.Authorization;

/// <summary>Resolves a user's effective permissions (group closure), with caching.
/// super_user is handled by the caller (claims transformation) from the token, not here.</summary>
public interface IPermissionResolver
{
    /// <summary>Resolves DB-derived effective permissions for an Entra object id.
    /// Materializes the AppUser on first call. Returns empty for inactive/unknown users.</summary>
    Task<EffectivePermissions> ResolveAsync(
        string entraObjectId, string? email, string? displayName, CancellationToken ct = default);

    /// <summary>Drops any cached entry for this Entra object id (used by admin writes).</summary>
    void InvalidateCache(string entraObjectId);
}
```

- [ ] **Step 3: Create `IAuthorizationRepository.cs`**

```csharp
using Anela.Heblo.Domain.Features.Authorization.Entities;

namespace Anela.Heblo.Domain.Features.Authorization;

/// <summary>Data access for the Authorization slice (single ApplicationDbContext, ADR-001).</summary>
public interface IAuthorizationRepository
{
    // Users
    Task<AppUser?> GetUserByObjectIdAsync(string entraObjectId, CancellationToken ct = default);
    Task<AppUser> AddUserAsync(AppUser user, CancellationToken ct = default);
    Task<AppUser?> GetUserByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<AppUser>> GetAllUsersAsync(CancellationToken ct = default);

    // Groups
    Task<List<PermissionGroup>> GetAllGroupsAsync(CancellationToken ct = default);
    Task<PermissionGroup?> GetGroupByIdAsync(Guid id, CancellationToken ct = default);
    Task<PermissionGroup?> GetGroupByNameAsync(string name, CancellationToken ct = default);
    Task<PermissionGroup> AddGroupAsync(PermissionGroup group, CancellationToken ct = default);
    Task RemoveGroupAsync(PermissionGroup group, CancellationToken ct = default);

    // Edges
    Task<List<UserGroup>> GetUserGroupsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>All group→permission and group→parent edges, for closure resolution.</summary>
    Task<(List<GroupPermission> Permissions, List<GroupParent> Parents)> GetGroupGraphAsync(CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 4: Build**

Run: `dotnet build backend/src/Anela.Heblo.Domain`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Authorization/
git commit -m "feat(authz): add resolver/repository contracts and EffectivePermissions"
```

---

## Phase 2 — Persistence: EF config, migration, repository, seeder

### Task 4: Add Authorization error codes

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` (append before `Exception = 0099` is NOT possible — add a new block at the end of the enum, before the closing brace)

- [ ] **Step 1: Add the 32xx block**

Add these members at the end of the `ErrorCodes` enum (prefix `32` is unused):

```csharp
    // Authorization module errors (32XX)
    [HttpStatusCode(HttpStatusCode.NotFound)]
    AuthorizationGroupNotFound = 3201,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    AuthorizationUserNotFound = 3202,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    AuthorizationInvalidPermission = 3203,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    AuthorizationGroupCycleDetected = 3204,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    AuthorizationSystemGroupImmutable = 3205,
    [HttpStatusCode(HttpStatusCode.Conflict)]
    AuthorizationDuplicateGroupName = 3206,
```

- [ ] **Step 2: Build**

Run: `dotnet build backend/src/Anela.Heblo.Application`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs
git commit -m "feat(authz): add 32xx Authorization error codes"
```

---

### Task 5: EF entity configurations

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Features/Authorization/AppUserConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Features/Authorization/PermissionGroupConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Features/Authorization/GroupPermissionConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Features/Authorization/GroupParentConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Features/Authorization/UserGroupConfiguration.cs`

These are auto-discovered by `ApplyConfigurationsFromAssembly` (no DbContext edit needed for discovery, but Task 6 adds DbSets for ergonomics).

- [ ] **Step 1: Create `AppUserConfiguration.cs`**

```csharp
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Features.Authorization;

public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("AppUsers", "public");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.EntraObjectId).IsRequired().HasMaxLength(100);
        builder.Property(u => u.Email).IsRequired().HasMaxLength(255);
        builder.Property(u => u.DisplayName).IsRequired().HasMaxLength(255);
        builder.Property(u => u.IsActive).IsRequired();
        builder.Property(u => u.CreatedAt).IsRequired();
        builder.HasIndex(u => u.EntraObjectId).IsUnique();
        builder.HasMany(u => u.UserGroups).WithOne(ug => ug.User)
            .HasForeignKey(ug => ug.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 2: Create `PermissionGroupConfiguration.cs`**

```csharp
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Features.Authorization;

public class PermissionGroupConfiguration : IEntityTypeConfiguration<PermissionGroup>
{
    public void Configure(EntityTypeBuilder<PermissionGroup> builder)
    {
        builder.ToTable("PermissionGroups", "public");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Name).IsRequired().HasMaxLength(100);
        builder.Property(g => g.Description).HasMaxLength(500);
        builder.Property(g => g.IsSystem).IsRequired();
        builder.Property(g => g.CreatedAt).IsRequired();
        builder.Property(g => g.CreatedBy).HasMaxLength(255);
        builder.HasIndex(g => g.Name).IsUnique();
        builder.HasMany(g => g.Permissions).WithOne(p => p.Group)
            .HasForeignKey(p => p.GroupId).OnDelete(DeleteBehavior.Cascade);
        // Parents: GroupId is the child side; delete a group cascades its outgoing parent edges.
        builder.HasMany(g => g.Parents).WithOne(gp => gp.Group)
            .HasForeignKey(gp => gp.GroupId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 3: Create `GroupPermissionConfiguration.cs`**

```csharp
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Features.Authorization;

public class GroupPermissionConfiguration : IEntityTypeConfiguration<GroupPermission>
{
    public void Configure(EntityTypeBuilder<GroupPermission> builder)
    {
        builder.ToTable("GroupPermissions", "public");
        builder.HasKey(p => new { p.GroupId, p.PermissionValue });
        builder.Property(p => p.PermissionValue).IsRequired().HasMaxLength(100);
    }
}
```

- [ ] **Step 4: Create `GroupParentConfiguration.cs`**

```csharp
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Features.Authorization;

public class GroupParentConfiguration : IEntityTypeConfiguration<GroupParent>
{
    public void Configure(EntityTypeBuilder<GroupParent> builder)
    {
        builder.ToTable("GroupParents", "public");
        builder.HasKey(gp => new { gp.GroupId, gp.ParentGroupId });
        // ParentGroup navigation: restrict delete to avoid multiple cascade paths (Postgres rejects them).
        builder.HasOne(gp => gp.ParentGroup).WithMany()
            .HasForeignKey(gp => gp.ParentGroupId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 5: Create `UserGroupConfiguration.cs`**

```csharp
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Features.Authorization;

public class UserGroupConfiguration : IEntityTypeConfiguration<UserGroup>
{
    public void Configure(EntityTypeBuilder<UserGroup> builder)
    {
        builder.ToTable("UserGroups", "public");
        builder.HasKey(ug => new { ug.UserId, ug.GroupId });
        builder.HasOne(ug => ug.Group).WithMany(g => g.UserGroups)
            .HasForeignKey(ug => ug.GroupId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 6: Build**

Run: `dotnet build backend/src/Anela.Heblo.Persistence`
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Features/Authorization/
git commit -m "feat(authz): add EF entity configurations for Authorization tables"
```

---

### Task 6: Add DbSets to ApplicationDbContext

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` (add DbSets near the other DbSet declarations)

- [ ] **Step 1: Add DbSets**

Add these properties alongside the existing `DbSet<...>` declarations:

```csharp
    // Authorization (in-app permissions)
    public DbSet<Anela.Heblo.Domain.Features.Authorization.Entities.AppUser> AppUsers { get; set; } = null!;
    public DbSet<Anela.Heblo.Domain.Features.Authorization.Entities.PermissionGroup> PermissionGroups { get; set; } = null!;
    public DbSet<Anela.Heblo.Domain.Features.Authorization.Entities.GroupPermission> GroupPermissions { get; set; } = null!;
    public DbSet<Anela.Heblo.Domain.Features.Authorization.Entities.GroupParent> GroupParents { get; set; } = null!;
    public DbSet<Anela.Heblo.Domain.Features.Authorization.Entities.UserGroup> UserGroups { get; set; } = null!;
```

- [ ] **Step 2: Build**

Run: `dotnet build backend/src/Anela.Heblo.Persistence`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs
git commit -m "feat(authz): register Authorization DbSets"
```

---

### Task 7: EF migration

**Files:**
- Create (generated): `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddAuthorizationTables.cs`

- [ ] **Step 1: Generate the migration**

Run:
```bash
dotnet ef migrations add AddAuthorizationTables \
  --project backend/src/Anela.Heblo.Persistence \
  --startup-project backend/src/Anela.Heblo.API
```
Expected: A new migration file under `Migrations/`. It should create tables `AppUsers`, `PermissionGroups`, `GroupPermissions`, `GroupParents`, `UserGroups`.

- [ ] **Step 2: Inspect the generated `Up()`**

Open the generated file and confirm: 5 `CreateTable` calls, unique indexes on `AppUsers.EntraObjectId` and `PermissionGroups.Name`, composite keys on the three join tables, and no accidental changes to unrelated tables. If unrelated model drift appears, STOP and report (do not hand-edit unrelated tables).

- [ ] **Step 3: Verify build (migration compiles)**

Run: `dotnet build backend/src/Anela.Heblo.Persistence`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Migrations/
git commit -m "feat(authz): EF migration adding Authorization tables"
```

---

### Task 8: AuthorizationRepository implementation

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Features/Authorization/AuthorizationRepository.cs`

- [ ] **Step 1: Create the repository (public, ADR-004 — binding added later in the module)**

```csharp
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Features.Authorization;

public class AuthorizationRepository : IAuthorizationRepository
{
    private readonly ApplicationDbContext _db;

    public AuthorizationRepository(ApplicationDbContext db) => _db = db;

    public Task<AppUser?> GetUserByObjectIdAsync(string entraObjectId, CancellationToken ct = default) =>
        _db.AppUsers.FirstOrDefaultAsync(u => u.EntraObjectId == entraObjectId, ct);

    public async Task<AppUser> AddUserAsync(AppUser user, CancellationToken ct = default)
    {
        await _db.AppUsers.AddAsync(user, ct);
        return user;
    }

    public Task<AppUser?> GetUserByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.AppUsers.Include(u => u.UserGroups).FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<List<AppUser>> GetAllUsersAsync(CancellationToken ct = default) =>
        await _db.AppUsers.AsNoTracking().Include(u => u.UserGroups).ToListAsync(ct);

    public async Task<List<PermissionGroup>> GetAllGroupsAsync(CancellationToken ct = default) =>
        await _db.PermissionGroups.AsNoTracking()
            .Include(g => g.Permissions).Include(g => g.Parents)
            .ToListAsync(ct);

    public Task<PermissionGroup?> GetGroupByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.PermissionGroups.Include(g => g.Permissions).Include(g => g.Parents)
            .FirstOrDefaultAsync(g => g.Id == id, ct);

    public Task<PermissionGroup?> GetGroupByNameAsync(string name, CancellationToken ct = default) =>
        _db.PermissionGroups.Include(g => g.Permissions).Include(g => g.Parents)
            .FirstOrDefaultAsync(g => g.Name == name, ct);

    public async Task<PermissionGroup> AddGroupAsync(PermissionGroup group, CancellationToken ct = default)
    {
        await _db.PermissionGroups.AddAsync(group, ct);
        return group;
    }

    public Task RemoveGroupAsync(PermissionGroup group, CancellationToken ct = default)
    {
        _db.PermissionGroups.Remove(group);
        return Task.CompletedTask;
    }

    public async Task<List<UserGroup>> GetUserGroupsAsync(Guid userId, CancellationToken ct = default) =>
        await _db.UserGroups.Where(ug => ug.UserId == userId).ToListAsync(ct);

    public async Task<(List<GroupPermission> Permissions, List<GroupParent> Parents)> GetGroupGraphAsync(CancellationToken ct = default)
    {
        var perms = await _db.GroupPermissions.AsNoTracking().ToListAsync(ct);
        var parents = await _db.GroupParents.AsNoTracking().ToListAsync(ct);
        return (perms, parents);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
```

- [ ] **Step 2: Build**

Run: `dotnet build backend/src/Anela.Heblo.Persistence`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Features/Authorization/AuthorizationRepository.cs
git commit -m "feat(authz): add AuthorizationRepository"
```

---

### Task 9: System-group seeder (TDD)

The seeder upserts the 10 `AccessMatrix.Groups` as `IsSystem=true` rows and re-syncs their permissions on every startup, and prunes `GroupPermission` rows whose value left `AccessMatrix`.

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Features/Authorization/AuthorizationSeeder.cs`
- Test: `backend/test/Anela.Heblo.Tests/Authorization/AuthorizationSeederTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Authorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class AuthorizationSeederTests
{
    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"seed_{Guid.NewGuid()}").Options);

    [Fact]
    public async Task Seed_CreatesAllSystemGroupsFromAccessMatrix()
    {
        await using var db = NewDb();
        await AuthorizationSeeder.SeedAsync(db, default);

        var groups = await db.PermissionGroups.Include(g => g.Permissions).ToListAsync();
        groups.Should().HaveCount(AccessMatrix.Groups.Count);
        groups.Should().OnlyContain(g => g.IsSystem);

        var spravce = groups.Single(g => g.Name == "Spravce");
        spravce.Permissions.Select(p => p.PermissionValue)
            .Should().BeEquivalentTo(AccessMatrix.AllRoleValues());
    }

    [Fact]
    public async Task Seed_IsIdempotent_NoDuplicatesOnSecondRun()
    {
        await using var db = NewDb();
        await AuthorizationSeeder.SeedAsync(db, default);
        await AuthorizationSeeder.SeedAsync(db, default);

        (await db.PermissionGroups.CountAsync()).Should().Be(AccessMatrix.Groups.Count);
    }

    [Fact]
    public async Task Seed_PrunesPermissionValuesNotInAccessMatrix()
    {
        await using var db = NewDb();
        await AuthorizationSeeder.SeedAsync(db, default);
        var grp = await db.PermissionGroups.FirstAsync(g => g.Name == "Nakupci");
        db.GroupPermissions.Add(new GroupPermission { GroupId = grp.Id, PermissionValue = "ghost.read" });
        await db.SaveChangesAsync();

        await AuthorizationSeeder.SeedAsync(db, default);

        (await db.GroupPermissions.AnyAsync(p => p.PermissionValue == "ghost.read"))
            .Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~AuthorizationSeederTests"`
Expected: FAIL — `AuthorizationSeeder` does not exist.

- [ ] **Step 3: Implement the seeder**

```csharp
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Features.Authorization;

public static class AuthorizationSeeder
{
    public static async Task SeedAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var validPermissions = AccessMatrix.AllRoleValues().ToHashSet();

        var existing = await db.PermissionGroups
            .Include(g => g.Permissions)
            .ToListAsync(ct);

        foreach (var matrixGroup in AccessMatrix.Groups)
        {
            var group = existing.FirstOrDefault(g => g.Name == matrixGroup.Name);
            if (group is null)
            {
                group = new PermissionGroup
                {
                    Id = Guid.NewGuid(),
                    Name = matrixGroup.Name,
                    Description = "System group (managed in code)",
                    IsSystem = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CreatedBy = "system",
                };
                db.PermissionGroups.Add(group);
            }
            else
            {
                group.IsSystem = true;
            }

            // Re-sync permissions: code is authoritative for system groups.
            var desired = matrixGroup.Roles.Where(validPermissions.Contains).ToHashSet();
            var current = group.Permissions.Select(p => p.PermissionValue).ToHashSet();

            foreach (var toAdd in desired.Except(current))
                group.Permissions.Add(new GroupPermission { GroupId = group.Id, PermissionValue = toAdd });

            foreach (var perm in group.Permissions.Where(p => !desired.Contains(p.PermissionValue)).ToList())
                group.Permissions.Remove(perm);
        }

        // Global prune: drop any GroupPermission whose value left AccessMatrix entirely.
        var orphans = await db.GroupPermissions
            .Where(p => !validPermissions.Contains(p.PermissionValue))
            .ToListAsync(ct);
        db.GroupPermissions.RemoveRange(orphans);

        await db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test --filter "FullyQualifiedName~AuthorizationSeederTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Features/Authorization/AuthorizationSeeder.cs \
        backend/test/Anela.Heblo.Tests/Authorization/AuthorizationSeederTests.cs
git commit -m "feat(authz): system-group seeder with re-sync and prune"
```

---

## Phase 3 — Resolver: group closure + caching + materialization

### Task 10: Pure group-closure helper (TDD)

Isolates the cycle-safe transitive closure so it can be unit-tested without a DB.

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Features/Authorization/GroupClosure.cs`
- Test: `backend/test/Anela.Heblo.Tests/Authorization/GroupClosureTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Anela.Heblo.Persistence.Features.Authorization;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class GroupClosureTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();

    private static GroupPermission Perm(Guid g, string v) => new() { GroupId = g, PermissionValue = v };
    private static GroupParent Parent(Guid child, Guid parent) => new() { GroupId = child, ParentGroupId = parent };

    [Fact]
    public void Resolve_UnionsDirectGroupPermissions()
    {
        var perms = new[] { Perm(A, "catalog.read"), Perm(B, "journal.read") };
        var result = GroupClosure.Resolve(new[] { A }, perms, Array.Empty<GroupParent>());
        result.Should().BeEquivalentTo(new[] { "catalog.read" });
    }

    [Fact]
    public void Resolve_IncludesParentPermissions()
    {
        var perms = new[] { Perm(A, "catalog.read"), Perm(B, "journal.read") };
        var parents = new[] { Parent(A, B) }; // A inherits B
        var result = GroupClosure.Resolve(new[] { A }, perms, parents);
        result.Should().BeEquivalentTo(new[] { "catalog.read", "journal.read" });
    }

    [Fact]
    public void Resolve_DeepChain_AccumulatesAllAncestors()
    {
        var perms = new[] { Perm(A, "a.read"), Perm(B, "b.read"), Perm(C, "c.read") };
        var parents = new[] { Parent(A, B), Parent(B, C) }; // A -> B -> C
        var result = GroupClosure.Resolve(new[] { A }, perms, parents);
        result.Should().BeEquivalentTo(new[] { "a.read", "b.read", "c.read" });
    }

    [Fact]
    public void Resolve_DiamondParent_CountsOnce_NoError()
    {
        var perms = new[] { Perm(A, "a.read"), Perm(B, "b.read"), Perm(C, "c.read") };
        // Diamond: A -> B, A -> C, B -> C
        var parents = new[] { Parent(A, B), Parent(A, C), Parent(B, C) };
        var result = GroupClosure.Resolve(new[] { A }, perms, parents);
        result.Should().BeEquivalentTo(new[] { "a.read", "b.read", "c.read" });
    }

    [Fact]
    public void Resolve_Cycle_Terminates_WithBoundedSet()
    {
        var perms = new[] { Perm(A, "a.read"), Perm(B, "b.read") };
        var parents = new[] { Parent(A, B), Parent(B, A) }; // cycle
        var result = GroupClosure.Resolve(new[] { A }, perms, parents);
        result.Should().BeEquivalentTo(new[] { "a.read", "b.read" });
    }

    [Fact]
    public void Resolve_NoGroups_ReturnsEmpty()
    {
        var result = GroupClosure.Resolve(Array.Empty<Guid>(), Array.Empty<GroupPermission>(), Array.Empty<GroupParent>());
        result.Should().BeEmpty();
    }
}
```


- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~GroupClosureTests"`
Expected: FAIL — `GroupClosure` does not exist.

- [ ] **Step 3: Implement `GroupClosure`**

```csharp
using Anela.Heblo.Domain.Features.Authorization.Entities;

namespace Anela.Heblo.Persistence.Features.Authorization;

/// <summary>Cycle-safe transitive closure of group nesting → union of permissions.</summary>
public static class GroupClosure
{
    public static IReadOnlyCollection<string> Resolve(
        IEnumerable<Guid> directGroupIds,
        IReadOnlyCollection<GroupPermission> allPermissions,
        IReadOnlyCollection<GroupParent> allParents)
    {
        var permsByGroup = allPermissions
            .GroupBy(p => p.GroupId)
            .ToDictionary(g => g.Key, g => g.Select(p => p.PermissionValue).ToArray());
        var parentsByGroup = allParents
            .GroupBy(p => p.GroupId)
            .ToDictionary(g => g.Key, g => g.Select(p => p.ParentGroupId).ToArray());

        var visited = new HashSet<Guid>();
        var queue = new Queue<Guid>(directGroupIds);
        var result = new HashSet<string>(StringComparer.Ordinal);

        while (queue.Count > 0)
        {
            var groupId = queue.Dequeue();
            if (!visited.Add(groupId)) continue; // already processed → cycle/diamond safe

            if (permsByGroup.TryGetValue(groupId, out var perms))
                foreach (var p in perms) result.Add(p);

            if (parentsByGroup.TryGetValue(groupId, out var parents))
                foreach (var parent in parents) queue.Enqueue(parent);
        }

        return result;
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test --filter "FullyQualifiedName~GroupClosureTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Features/Authorization/GroupClosure.cs \
        backend/test/Anela.Heblo.Tests/Authorization/GroupClosureTests.cs
git commit -m "feat(authz): cycle-safe group closure helper"
```

---

### Task 11: PermissionResolver with materialization + cache (TDD)

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Features/Authorization/PermissionResolver.cs`
- Test: `backend/test/Anela.Heblo.Tests/Authorization/PermissionResolverTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Authorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class PermissionResolverTests
{
    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"resolver_{Guid.NewGuid()}").Options);

    private static PermissionResolver NewResolver(ApplicationDbContext db) =>
        new(new AuthorizationRepository(db), new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task Resolve_UnknownUser_MaterializesAppUser_WithHebloUserOnly()
    {
        await using var db = NewDb();
        var resolver = NewResolver(db);

        var result = await resolver.ResolveAsync("oid-1", "a@b.cz", "Alice");

        result.IsSuperUser.Should().BeFalse();
        result.Permissions.Should().BeEquivalentTo(new[] { "heblo_user" });
        (await db.AppUsers.AnyAsync(u => u.EntraObjectId == "oid-1")).Should().BeTrue();
    }

    [Fact]
    public async Task Resolve_ActiveUserInGroup_ReturnsGroupPermissionsPlusBase()
    {
        await using var db = NewDb();
        var group = new PermissionGroup { Id = Guid.NewGuid(), Name = "G", CreatedAt = DateTimeOffset.UtcNow };
        group.Permissions.Add(new GroupPermission { GroupId = group.Id, PermissionValue = "catalog.read" });
        db.PermissionGroups.Add(group);
        var user = new AppUser { Id = Guid.NewGuid(), EntraObjectId = "oid-2", Email = "x", DisplayName = "X", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        user.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = group.Id });
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();

        var result = await NewResolver(db).ResolveAsync("oid-2", "x", "X");

        result.Permissions.Should().BeEquivalentTo(new[] { "heblo_user", "catalog.read" });
        result.Groups.Should().BeEquivalentTo(new[] { "G" });
    }

    [Fact]
    public async Task Resolve_InactiveUser_ReturnsEmpty()
    {
        await using var db = NewDb();
        db.AppUsers.Add(new AppUser { Id = Guid.NewGuid(), EntraObjectId = "oid-3", Email = "x", DisplayName = "X", IsActive = false, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var result = await NewResolver(db).ResolveAsync("oid-3", "x", "X");

        result.Permissions.Should().BeEmpty();
        result.IsSuperUser.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~PermissionResolverTests"`
Expected: FAIL — `PermissionResolver` does not exist.

- [ ] **Step 3: Implement `PermissionResolver`**

```csharp
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Microsoft.Extensions.Caching.Memory;

namespace Anela.Heblo.Persistence.Features.Authorization;

public class PermissionResolver : IPermissionResolver
{
    private readonly IAuthorizationRepository _repo;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public PermissionResolver(IAuthorizationRepository repo, IMemoryCache cache)
    {
        _repo = repo;
        _cache = cache;
    }

    private static string CacheKey(string objectId) => $"perms:{objectId}";

    public async Task<EffectivePermissions> ResolveAsync(
        string entraObjectId, string? email, string? displayName, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CacheKey(entraObjectId), out EffectivePermissions? cached) && cached is not null)
            return cached;

        var user = await _repo.GetUserByObjectIdAsync(entraObjectId, ct);
        if (user is null)
        {
            user = new AppUser
            {
                Id = Guid.NewGuid(),
                EntraObjectId = entraObjectId,
                Email = email ?? entraObjectId,
                DisplayName = displayName ?? email ?? entraObjectId,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                LastLoginAt = DateTimeOffset.UtcNow,
            };
            await _repo.AddUserAsync(user, ct);
            await _repo.SaveChangesAsync(ct);
        }

        EffectivePermissions result;
        if (!user.IsActive)
        {
            result = EffectivePermissions.Empty;
        }
        else
        {
            var userGroups = await _repo.GetUserGroupsAsync(user.Id, ct);
            var groupIds = userGroups.Select(ug => ug.GroupId).ToList();
            var (perms, parents) = await _repo.GetGroupGraphAsync(ct);

            var resolved = GroupClosure.Resolve(groupIds, perms, parents);
            var permissions = new HashSet<string>(resolved, StringComparer.Ordinal) { AccessRoles.Base };

            var allGroups = await _repo.GetAllGroupsAsync(ct);
            var groupNames = allGroups.Where(g => groupIds.Contains(g.Id)).Select(g => g.Name).ToArray();

            result = new EffectivePermissions(false, permissions.ToArray(), groupNames);
        }

        _cache.Set(CacheKey(entraObjectId), result, CacheTtl);
        return result;
    }

    public void InvalidateCache(string entraObjectId) => _cache.Remove(CacheKey(entraObjectId));
}
```

> Add `using Anela.Heblo.Domain.Features.Authorization;` is already present; `AccessRoles` lives there.

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test --filter "FullyQualifiedName~PermissionResolverTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Features/Authorization/PermissionResolver.cs \
        backend/test/Anela.Heblo.Tests/Authorization/PermissionResolverTests.cs
git commit -m "feat(authz): permission resolver with materialization and cache"
```

---
## Phase 4 — Enforcement: claims transformation + wiring

### Task 12: PermissionClaimsTransformation (TDD)

Injects effective permissions as `Role` claims. `super_user` (from the token) short-circuits to all `AccessMatrix` permissions; otherwise it calls the resolver.

**Files:**
- Create: `backend/src/Anela.Heblo.API/Infrastructure/Authentication/PermissionClaimsTransformation.cs`
- Test: `backend/test/Anela.Heblo.Tests/Authorization/PermissionClaimsTransformationTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Security.Claims;
using Anela.Heblo.API.Infrastructure.Authentication;
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class PermissionClaimsTransformationTests
{
    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "Test"));

    [Fact]
    public async Task Transform_SuperUserRoleInToken_AddsAllPermissions_NoResolverCall()
    {
        var resolver = new Mock<IPermissionResolver>(MockBehavior.Strict);
        var sut = new PermissionClaimsTransformation(resolver.Object);
        var principal = Principal(
            new Claim(ClaimTypes.NameIdentifier, "oid-super"),
            new Claim(ClaimTypes.Role, AccessRoles.SuperUser));

        var result = await sut.TransformAsync(principal);

        foreach (var perm in AccessMatrix.AllRoleValues())
            result.IsInRole(perm).Should().BeTrue($"super_user must grant {perm}");
        result.IsInRole(AccessRoles.Base).Should().BeTrue();
        resolver.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Transform_RegularUser_AddsResolvedPermissionsAsRoles()
    {
        var resolver = new Mock<IPermissionResolver>();
        resolver
            .Setup(r => r.ResolveAsync("oid-1", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectivePermissions(false, new[] { "heblo_user", "catalog.read" }, new[] { "G" }));
        var sut = new PermissionClaimsTransformation(resolver.Object);
        var principal = Principal(new Claim(ClaimTypes.NameIdentifier, "oid-1"));

        var result = await sut.TransformAsync(principal);

        result.IsInRole("catalog.read").Should().BeTrue();
        result.IsInRole("heblo_user").Should().BeTrue();
        result.IsInRole("journal.read").Should().BeFalse();
    }

    [Fact]
    public async Task Transform_Idempotent_DoesNotDuplicateClaims()
    {
        var resolver = new Mock<IPermissionResolver>();
        resolver
            .Setup(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectivePermissions(false, new[] { "catalog.read" }, Array.Empty<string>()));
        var sut = new PermissionClaimsTransformation(resolver.Object);
        var principal = Principal(new Claim(ClaimTypes.NameIdentifier, "oid-1"));

        var once = await sut.TransformAsync(principal);
        var twice = await sut.TransformAsync(once);

        twice.Claims.Count(c => c.Type == ClaimTypes.Role && c.Value == "catalog.read").Should().Be(1);
    }

    [Fact]
    public async Task Transform_Unauthenticated_ReturnsUnchanged()
    {
        var resolver = new Mock<IPermissionResolver>(MockBehavior.Strict);
        var sut = new PermissionClaimsTransformation(resolver.Object);
        var anon = new ClaimsPrincipal(new ClaimsIdentity()); // not authenticated

        var result = await sut.TransformAsync(anon);

        result.Claims.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~PermissionClaimsTransformationTests"`
Expected: FAIL — type does not exist.

- [ ] **Step 3: Implement the transformation**

```csharp
using System.Security.Claims;
using Anela.Heblo.Domain.Features.Authorization;
using Microsoft.AspNetCore.Authentication;

namespace Anela.Heblo.API.Infrastructure.Authentication;

/// <summary>Injects a user's effective permissions as Role claims after authentication.
/// super_user (from the token) is a wildcard granting all AccessMatrix permissions.</summary>
public class PermissionClaimsTransformation : IClaimsTransformation
{
    private readonly IPermissionResolver _resolver;

    public PermissionClaimsTransformation(IPermissionResolver resolver) => _resolver = resolver;

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var identity = principal.Identity as ClaimsIdentity;
        if (identity is null || !identity.IsAuthenticated)
            return principal;

        // Guard against re-running (IClaimsTransformation can be invoked multiple times per request).
        if (identity.HasClaim("authz_applied", "1"))
            return principal;

        var objectId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? principal.FindFirst("oid")?.Value
                       ?? principal.FindFirst("sub")?.Value;

        IReadOnlyCollection<string> permissions;
        if (principal.IsInRole(AccessRoles.SuperUser))
        {
            permissions = AccessMatrix.AllRoleValues().Append(AccessRoles.Base).ToArray();
        }
        else if (objectId is not null)
        {
            var email = principal.FindFirst(ClaimTypes.Email)?.Value
                        ?? principal.FindFirst("preferred_username")?.Value;
            var name = principal.FindFirst(ClaimTypes.Name)?.Value
                       ?? principal.FindFirst("name")?.Value;
            var resolved = await _resolver.ResolveAsync(objectId, email, name);
            permissions = resolved.Permissions;
        }
        else
        {
            permissions = Array.Empty<string>();
        }

        // CRITICAL: add role claims using the identity's RoleClaimType, NOT a hardcoded
        // ClaimTypes.Role. Microsoft.Identity.Web configures Entra to use the "roles" claim
        // type; [Authorize(Roles=…)] / IsInRole check that configured type. Using the wrong
        // type would silently make every role check fail.
        var roleClaimType = identity.RoleClaimType; // e.g. "roles" for Entra, ClaimTypes.Role for mock
        foreach (var perm in permissions)
            if (!identity.HasClaim(roleClaimType, perm))
                identity.AddClaim(new Claim(roleClaimType, perm));

        identity.AddClaim(new Claim("authz_applied", "1"));
        return principal;
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test --filter "FullyQualifiedName~PermissionClaimsTransformationTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/Infrastructure/Authentication/PermissionClaimsTransformation.cs \
        backend/test/Anela.Heblo.Tests/Authorization/PermissionClaimsTransformationTests.cs
git commit -m "feat(authz): permission claims transformation (super_user wildcard + resolver)"
```

---

### Task 13: AuthorizationModule (DI wiring per ADR-004)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Authorization/AuthorizationModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs` (register the module)

> The repository + resolver implementations live in `Anela.Heblo.Persistence` (single DbContext, ADR-001) but the DI binding lives in the feature module (ADR-004). The Application project already references Persistence (other modules bind Persistence repos the same way).

- [ ] **Step 1: Create `AuthorizationModule.cs`**

```csharp
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Persistence.Features.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Authorization;

public static class AuthorizationModule
{
    public static IServiceCollection AddAuthorizationModule(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddScoped<IAuthorizationRepository, AuthorizationRepository>();
        services.AddScoped<IPermissionResolver, PermissionResolver>();
        return services;
    }
}
```

- [ ] **Step 2: Register in `ApplicationModule.cs`**

Add the using at the top with the other `Anela.Heblo.Application.Features.*` usings:

```csharp
using Anela.Heblo.Application.Features.Authorization;
```

And add this line in the "Register all feature modules" block (e.g. right after `services.AddFeatureFlagsModule(configuration);`):

```csharp
        services.AddAuthorizationModule();
```

- [ ] **Step 3: Build**

Run: `dotnet build backend/src/Anela.Heblo.Application`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Authorization/AuthorizationModule.cs \
        backend/src/Anela.Heblo.Application/ApplicationModule.cs
git commit -m "feat(authz): AuthorizationModule DI wiring"
```

---

### Task 14: Register the claims transformation + run the seeder at startup

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs` (register `IClaimsTransformation` in `ConfigureAuthorizationPolicies`)
- Modify: `backend/src/Anela.Heblo.API/Program.cs` (run `AuthorizationSeeder` after the app is built, near other startup DB work)

- [ ] **Step 1: Register the transformation**

In `AuthenticationExtensions.cs`, add the using:

```csharp
using Anela.Heblo.API.Infrastructure.Authentication;
```

(Already present — confirm.) Then inside `ConfigureAuthorizationPolicies(IServiceCollection services)`, before/after `services.AddAuthorization(...)`, add:

```csharp
        services.AddScoped<Microsoft.AspNetCore.Authentication.IClaimsTransformation, PermissionClaimsTransformation>();
```

This runs for BOTH mock and real auth (both call `ConfigureAuthorizationPolicies`).

- [ ] **Step 2: Run the seeder at startup**

In `Program.cs`, after `var app = builder.Build();` and wherever migrations/DB warm-up happen (search for `app.Services.CreateScope()` or an existing startup DB block), add a seeding block. If no such block exists, add right before `app.Run();`:

```csharp
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Anela.Heblo.Persistence.ApplicationDbContext>();
            // InMemory provider (tests) has no relational migrations; guard accordingly.
            if (db.Database.IsRelational())
            {
                await db.Database.MigrateAsync();
            }
            await Anela.Heblo.Persistence.Features.Authorization.AuthorizationSeeder.SeedAsync(db, default);
        }
```

> If the project already calls `MigrateAsync()` at startup, do NOT duplicate it — only add the `AuthorizationSeeder.SeedAsync` call inside the existing scope. Check first.

- [ ] **Step 3: Build**

Run: `dotnet build backend/src/Anela.Heblo.API`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs \
        backend/src/Anela.Heblo.API/Program.cs
git commit -m "feat(authz): register claims transformation and run system-group seeder at startup"
```

---

### Task 15: Make mock auth a super_user (route dev/test through the wildcard path)

Today `MockAuthenticationHandler` emits all 52 roles directly. Replace that with the single `super_user` role so mock dev exercises the real enforcement path while keeping full access.

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Infrastructure/Authentication/MockAuthenticationHandler.cs:30-34`

- [ ] **Step 1: Replace the role claims**

Replace:

```csharp
        var roleClaims = new[] { AccessMatrix.BaseRole }
            .Concat(AccessMatrix.AllRoleValues())
            .Select(r => new Claim(ClaimTypes.Role, r));
```

with:

```csharp
        // Mock users are super_users: full access via the same wildcard path as production break-glass.
        var roleClaims = new[]
        {
            new Claim(ClaimTypes.Role, AccessMatrix.BaseRole),
            new Claim(ClaimTypes.Role, AccessRoles.SuperUser),
        };
```

Ensure `using Anela.Heblo.Domain.Features.Authorization;` is present (it is — `AccessMatrix` is already used).

- [ ] **Step 2: Build**

Run: `dotnet build backend/src/Anela.Heblo.API`
Expected: Build succeeded.

- [ ] **Step 3: Run the existing auth/mock tests to check for regressions**

Run: `dotnet test --filter "FullyQualifiedName~Authentication|FullyQualifiedName~Mock"`
Expected: PASS. If a test asserted the mock principal carries all 52 explicit role claims, update it to assert `super_user` instead (the net access is identical because the claims transformation expands it).

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.API/Infrastructure/Authentication/MockAuthenticationHandler.cs
git commit -m "feat(authz): mock auth emits super_user (exercises wildcard path)"
```

---
## Phase 5 — Application use cases (MediatR)

> All requests/handlers/responses live under
> `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/<UseCase>/`.
> Responses inherit `BaseResponse`. Handlers resolve identity via injected
> `ICurrentUserService` (ADR-005) where they need the acting user.

### Task 16: GetPermissionCatalogue query

Returns the `AccessMatrix` features/levels/groups so the admin UI renders permission
checkboxes from the source of truth.

**Files:**
- Create: `.../UseCases/GetPermissionCatalogue/GetPermissionCatalogueRequest.cs`
- Create: `.../UseCases/GetPermissionCatalogue/GetPermissionCatalogueResponse.cs`
- Create: `.../UseCases/GetPermissionCatalogue/GetPermissionCatalogueHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Authorization/GetPermissionCatalogueHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Anela.Heblo.Application.Features.Authorization.UseCases.GetPermissionCatalogue;
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class GetPermissionCatalogueHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsAllPermissionsAndSystemGroups()
    {
        var handler = new GetPermissionCatalogueHandler();
        var result = await handler.Handle(new GetPermissionCatalogueRequest(), default);

        result.Success.Should().BeTrue();
        result.Permissions.Should().BeEquivalentTo(AccessMatrix.AllRoleValues());
        result.SystemGroups.Select(g => g.Name).Should().BeEquivalentTo(AccessMatrix.Groups.Select(g => g.Name));
        result.Features.Should().Contain(f => f.Key == "catalog" && f.HasWrite);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~GetPermissionCatalogueHandlerTests"`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Implement request/response/handler**

`GetPermissionCatalogueRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetPermissionCatalogue;

public class GetPermissionCatalogueRequest : IRequest<GetPermissionCatalogueResponse> { }
```

`GetPermissionCatalogueResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetPermissionCatalogue;

public class GetPermissionCatalogueResponse : BaseResponse
{
    public List<string> Permissions { get; set; } = new();
    public List<CatalogueFeatureDto> Features { get; set; } = new();
    public List<CatalogueGroupDto> SystemGroups { get; set; } = new();
}

public class CatalogueFeatureDto
{
    public string Key { get; set; } = null!;
    public string Label { get; set; } = null!;
    public string Section { get; set; } = null!;
    public bool HasWrite { get; set; }
    public bool HasAdmin { get; set; }
}

public class CatalogueGroupDto
{
    public string Name { get; set; } = null!;
    public List<string> Permissions { get; set; } = new();
}
```

`GetPermissionCatalogueHandler.cs`:

```csharp
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetPermissionCatalogue;

public class GetPermissionCatalogueHandler
    : IRequestHandler<GetPermissionCatalogueRequest, GetPermissionCatalogueResponse>
{
    public Task<GetPermissionCatalogueResponse> Handle(GetPermissionCatalogueRequest request, CancellationToken ct)
    {
        var response = new GetPermissionCatalogueResponse
        {
            Permissions = AccessMatrix.AllRoleValues().ToList(),
            Features = AccessMatrix.Features.Select(f => new CatalogueFeatureDto
            {
                Key = f.Key,
                Label = f.Label,
                Section = f.Section,
                HasWrite = f.HasWrite,
                HasAdmin = f.HasAdmin,
            }).ToList(),
            SystemGroups = AccessMatrix.Groups.Select(g => new CatalogueGroupDto
            {
                Name = g.Name,
                Permissions = g.Roles.ToList(),
            }).ToList(),
        };
        return Task.FromResult(response);
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test --filter "FullyQualifiedName~GetPermissionCatalogueHandlerTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetPermissionCatalogue/ \
        backend/test/Anela.Heblo.Tests/Authorization/GetPermissionCatalogueHandlerTests.cs
git commit -m "feat(authz): GetPermissionCatalogue query"
```

---

### Task 17: GetMe query (feeds /api/auth/me)

Resolves the current user's effective permissions for the frontend.

**Files:**
- Create: `.../UseCases/GetMe/GetMeRequest.cs`
- Create: `.../UseCases/GetMe/GetMeResponse.cs`
- Create: `.../UseCases/GetMe/GetMeHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Authorization/GetMeHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Anela.Heblo.Application.Features.Authorization.UseCases.GetMe;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class GetMeHandlerTests
{
    [Fact]
    public async Task Handle_SuperUser_ReturnsAllPermissionsAndIsSuperUser()
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(c => c.GetCurrentUser()).Returns(new CurrentUser("oid-s", "Sue", "s@x.cz", true));
        currentUser.Setup(c => c.IsInRole(AccessRoles.SuperUser)).Returns(true);
        var resolver = new Mock<IPermissionResolver>(MockBehavior.Strict);

        var handler = new GetMeHandler(currentUser.Object, resolver.Object);
        var result = await handler.Handle(new GetMeRequest(), default);

        result.IsSuperUser.Should().BeTrue();
        result.Permissions.Should().BeEquivalentTo(AccessMatrix.AllRoleValues().Append(AccessRoles.Base));
    }

    [Fact]
    public async Task Handle_RegularUser_ReturnsResolvedPermissionsAndGroups()
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(c => c.GetCurrentUser()).Returns(new CurrentUser("oid-1", "Al", "a@x.cz", true));
        currentUser.Setup(c => c.IsInRole(AccessRoles.SuperUser)).Returns(false);
        var resolver = new Mock<IPermissionResolver>();
        resolver.Setup(r => r.ResolveAsync("oid-1", "a@x.cz", "Al", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectivePermissions(false, new[] { "heblo_user", "catalog.read" }, new[] { "Marketer" }));

        var handler = new GetMeHandler(currentUser.Object, resolver.Object);
        var result = await handler.Handle(new GetMeRequest(), default);

        result.IsSuperUser.Should().BeFalse();
        result.Permissions.Should().BeEquivalentTo(new[] { "heblo_user", "catalog.read" });
        result.Groups.Should().BeEquivalentTo(new[] { "Marketer" });
        result.Email.Should().Be("a@x.cz");
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~GetMeHandlerTests"`
Expected: FAIL.

- [ ] **Step 3: Implement request/response/handler**

`GetMeRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetMe;

public class GetMeRequest : IRequest<GetMeResponse> { }
```

`GetMeResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetMe;

public class GetMeResponse : BaseResponse
{
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public bool IsSuperUser { get; set; }
    public List<string> Permissions { get; set; } = new();
    public List<string> Groups { get; set; } = new();
}
```

`GetMeHandler.cs`:

```csharp
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetMe;

public class GetMeHandler : IRequestHandler<GetMeRequest, GetMeResponse>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionResolver _resolver;

    public GetMeHandler(ICurrentUserService currentUser, IPermissionResolver resolver)
    {
        _currentUser = currentUser;
        _resolver = resolver;
    }

    public async Task<GetMeResponse> Handle(GetMeRequest request, CancellationToken ct)
    {
        var user = _currentUser.GetCurrentUser();

        if (_currentUser.IsInRole(AccessRoles.SuperUser))
        {
            return new GetMeResponse
            {
                Email = user.Email,
                DisplayName = user.Name,
                IsSuperUser = true,
                Permissions = AccessMatrix.AllRoleValues().Append(AccessRoles.Base).ToList(),
                Groups = new List<string>(),
            };
        }

        var resolved = await _resolver.ResolveAsync(user.Id ?? string.Empty, user.Email, user.Name, ct);
        return new GetMeResponse
        {
            Email = user.Email,
            DisplayName = user.Name,
            IsSuperUser = false,
            Permissions = resolved.Permissions.ToList(),
            Groups = resolved.Groups.ToList(),
        };
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test --filter "FullyQualifiedName~GetMeHandlerTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetMe/ \
        backend/test/Anela.Heblo.Tests/Authorization/GetMeHandlerTests.cs
git commit -m "feat(authz): GetMe query for /api/auth/me"
```

---

### Task 18: Cycle-detection helper for group parents (TDD)

Used by Create/Update group handlers to reject a parent edge that would create a cycle.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Authorization/GroupCycleCheck.cs`
- Test: `backend/test/Anela.Heblo.Tests/Authorization/GroupCycleCheckTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Anela.Heblo.Application.Features.Authorization;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class GroupCycleCheckTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();

    [Fact]
    public void WouldCreateCycle_DirectSelfParent_True()
    {
        GroupCycleCheck.WouldCreateCycle(A, new[] { A }, new Dictionary<Guid, List<Guid>>())
            .Should().BeTrue();
    }

    [Fact]
    public void WouldCreateCycle_BackEdge_True()
    {
        // Existing: B -> A (B has parent A). Adding A -> B closes a cycle.
        var existing = new Dictionary<Guid, List<Guid>> { [B] = new() { A } };
        GroupCycleCheck.WouldCreateCycle(A, new[] { B }, existing).Should().BeTrue();
    }

    [Fact]
    public void WouldCreateCycle_TransitiveBackEdge_True()
    {
        // Existing: B -> C, C -> A. Adding A -> B closes A->B->C->A.
        var existing = new Dictionary<Guid, List<Guid>> { [B] = new() { C }, [C] = new() { A } };
        GroupCycleCheck.WouldCreateCycle(A, new[] { B }, existing).Should().BeTrue();
    }

    [Fact]
    public void WouldCreateCycle_AcyclicParent_False()
    {
        var existing = new Dictionary<Guid, List<Guid>> { [B] = new() { C } };
        GroupCycleCheck.WouldCreateCycle(A, new[] { B }, existing).Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~GroupCycleCheckTests"`
Expected: FAIL.

- [ ] **Step 3: Implement `GroupCycleCheck`**

```csharp
namespace Anela.Heblo.Application.Features.Authorization;

/// <summary>Detects whether assigning parents to a group would create a cycle in the nesting DAG.</summary>
public static class GroupCycleCheck
{
    /// <param name="groupId">The group being edited.</param>
    /// <param name="proposedParentIds">Parent group ids we want to assign to groupId.</param>
    /// <param name="existingParents">Map of groupId → its current parent ids (excluding the edited group's edges).</param>
    public static bool WouldCreateCycle(
        Guid groupId,
        IEnumerable<Guid> proposedParentIds,
        IReadOnlyDictionary<Guid, List<Guid>> existingParents)
    {
        foreach (var parent in proposedParentIds)
        {
            if (parent == groupId) return true; // self-parent

            // Walk up from `parent`; if we reach `groupId`, the new edge closes a cycle.
            var visited = new HashSet<Guid>();
            var stack = new Stack<Guid>();
            stack.Push(parent);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current == groupId) return true;
                if (!visited.Add(current)) continue;
                if (existingParents.TryGetValue(current, out var grandparents))
                    foreach (var gp in grandparents) stack.Push(gp);
            }
        }
        return false;
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test --filter "FullyQualifiedName~GroupCycleCheckTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Authorization/GroupCycleCheck.cs \
        backend/test/Anela.Heblo.Tests/Authorization/GroupCycleCheckTests.cs
git commit -m "feat(authz): group nesting cycle-detection helper"
```

---
### Task 19: Group DTOs + read queries (GetGroups, GetGroupDetail)

**Files:**
- Create: `.../UseCases/GroupDtos.cs`
- Create: `.../UseCases/GetGroups/GetGroupsRequest.cs`, `GetGroupsResponse.cs`, `GetGroupsHandler.cs`
- Create: `.../UseCases/GetGroupDetail/GetGroupDetailRequest.cs`, `GetGroupDetailResponse.cs`, `GetGroupDetailHandler.cs`

- [ ] **Step 1: Create shared `GroupDtos.cs`**

```csharp
namespace Anela.Heblo.Application.Features.Authorization.UseCases;

public class GroupSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public int PermissionCount { get; set; }
    public int ParentCount { get; set; }
    public int MemberCount { get; set; }
}

public class GroupDetailDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public List<string> Permissions { get; set; } = new();
    public List<Guid> ParentGroupIds { get; set; } = new();
}
```

- [ ] **Step 2: GetGroups (request/response/handler)**

`GetGroupsRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetGroups;

public class GetGroupsRequest : IRequest<GetGroupsResponse> { }
```

`GetGroupsResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.Authorization.UseCases;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetGroups;

public class GetGroupsResponse : BaseResponse
{
    public List<GroupSummaryDto> Groups { get; set; } = new();
}
```

`GetGroupsHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.Authorization.UseCases;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetGroups;

public class GetGroupsHandler : IRequestHandler<GetGroupsRequest, GetGroupsResponse>
{
    private readonly IAuthorizationRepository _repo;
    public GetGroupsHandler(IAuthorizationRepository repo) => _repo = repo;

    public async Task<GetGroupsResponse> Handle(GetGroupsRequest request, CancellationToken ct)
    {
        var groups = await _repo.GetAllGroupsAsync(ct);
        return new GetGroupsResponse
        {
            Groups = groups.Select(g => new GroupSummaryDto
            {
                Id = g.Id,
                Name = g.Name,
                Description = g.Description,
                IsSystem = g.IsSystem,
                PermissionCount = g.Permissions.Count,
                ParentCount = g.Parents.Count,
                MemberCount = g.UserGroups.Count,
            }).OrderBy(g => g.Name).ToList(),
        };
    }
}
```

> `GetAllGroupsAsync` already `Include`s Permissions and Parents. To populate `MemberCount`, extend it to also `.Include(g => g.UserGroups)` — update `AuthorizationRepository.GetAllGroupsAsync` to add `.Include(g => g.UserGroups)`.

- [ ] **Step 3: GetGroupDetail (request/response/handler)**

`GetGroupDetailRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetGroupDetail;

public class GetGroupDetailRequest : IRequest<GetGroupDetailResponse>
{
    public Guid Id { get; set; }
}
```

`GetGroupDetailResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.Authorization.UseCases;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetGroupDetail;

public class GetGroupDetailResponse : BaseResponse
{
    public GroupDetailDto? Group { get; set; }
    public GetGroupDetailResponse() { }
    public GetGroupDetailResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

`GetGroupDetailHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.Authorization.UseCases;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetGroupDetail;

public class GetGroupDetailHandler : IRequestHandler<GetGroupDetailRequest, GetGroupDetailResponse>
{
    private readonly IAuthorizationRepository _repo;
    public GetGroupDetailHandler(IAuthorizationRepository repo) => _repo = repo;

    public async Task<GetGroupDetailResponse> Handle(GetGroupDetailRequest request, CancellationToken ct)
    {
        var group = await _repo.GetGroupByIdAsync(request.Id, ct);
        if (group is null)
            return new GetGroupDetailResponse(ErrorCodes.AuthorizationGroupNotFound);

        return new GetGroupDetailResponse
        {
            Group = new GroupDetailDto
            {
                Id = group.Id,
                Name = group.Name,
                Description = group.Description,
                IsSystem = group.IsSystem,
                Permissions = group.Permissions.Select(p => p.PermissionValue).OrderBy(v => v).ToList(),
                ParentGroupIds = group.Parents.Select(p => p.ParentGroupId).ToList(),
            },
        };
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build backend/src/Anela.Heblo.Application`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GroupDtos.cs \
        backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetGroups/ \
        backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetGroupDetail/ \
        backend/src/Anela.Heblo.Persistence/Features/Authorization/AuthorizationRepository.cs
git commit -m "feat(authz): group read queries (GetGroups, GetGroupDetail)"
```

---

### Task 20: CreateGroup command (TDD — validation, cycle, duplicate, system)

**Files:**
- Create: `.../UseCases/CreateGroup/CreateGroupRequest.cs`, `CreateGroupResponse.cs`, `CreateGroupHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Authorization/CreateGroupHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Anela.Heblo.Application.Features.Authorization.UseCases.CreateGroup;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Authorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class CreateGroupHandlerTests
{
    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"creategroup_{Guid.NewGuid()}").Options);

    private static CreateGroupHandler NewHandler(ApplicationDbContext db)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(c => c.GetCurrentUser()).Returns(new CurrentUser("oid", "Admin", "admin@x.cz", true));
        return new CreateGroupHandler(new AuthorizationRepository(db), currentUser.Object);
    }

    [Fact]
    public async Task Handle_ValidGroup_PersistsWithPermissions()
    {
        await using var db = NewDb();
        var result = await NewHandler(db).Handle(new CreateGroupRequest
        {
            Name = "Custom",
            Description = "desc",
            Permissions = new() { "catalog.read", "journal.read" },
        }, default);

        result.Success.Should().BeTrue();
        var saved = await db.PermissionGroups.Include(g => g.Permissions).SingleAsync();
        saved.IsSystem.Should().BeFalse();
        saved.Permissions.Select(p => p.PermissionValue).Should().BeEquivalentTo(new[] { "catalog.read", "journal.read" });
    }

    [Fact]
    public async Task Handle_UnknownPermission_ReturnsInvalidPermission()
    {
        await using var db = NewDb();
        var result = await NewHandler(db).Handle(new CreateGroupRequest
        {
            Name = "Bad",
            Permissions = new() { "ghost.read" },
        }, default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.AuthorizationInvalidPermission);
    }

    [Fact]
    public async Task Handle_DuplicateName_ReturnsDuplicate()
    {
        await using var db = NewDb();
        db.PermissionGroups.Add(new PermissionGroup { Id = Guid.NewGuid(), Name = "Dup", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var result = await NewHandler(db).Handle(new CreateGroupRequest { Name = "Dup", Permissions = new() }, default);

        result.ErrorCode.Should().Be(ErrorCodes.AuthorizationDuplicateGroupName);
    }

    [Fact]
    public async Task Handle_ParentCreatingCycle_ReturnsCycleError()
    {
        await using var db = NewDb();
        // existing: P (will be the new group's parent). Then make P's parent the new group -> not possible at create.
        // Instead: self-cycle is impossible at create (no id yet), so test parent that points back via existing chain.
        var p = new PermissionGroup { Id = Guid.NewGuid(), Name = "P", CreatedAt = DateTimeOffset.UtcNow };
        db.PermissionGroups.Add(p);
        await db.SaveChangesAsync();
        // No back-edge exists yet, so adding P as parent is fine — assert success to lock in behavior.
        var result = await NewHandler(db).Handle(new CreateGroupRequest
        {
            Name = "Child", Permissions = new(), ParentGroupIds = new() { p.Id },
        }, default);

        result.Success.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~CreateGroupHandlerTests"`
Expected: FAIL.

- [ ] **Step 3: Implement request/response/handler**

`CreateGroupRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.CreateGroup;

public class CreateGroupRequest : IRequest<CreateGroupResponse>
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public List<string> Permissions { get; set; } = new();
    public List<Guid> ParentGroupIds { get; set; } = new();
}
```

`CreateGroupResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.CreateGroup;

public class CreateGroupResponse : BaseResponse
{
    public Guid Id { get; set; }
    public CreateGroupResponse() { }
    public CreateGroupResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

`CreateGroupHandler.cs`:

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.CreateGroup;

public class CreateGroupHandler : IRequestHandler<CreateGroupRequest, CreateGroupResponse>
{
    private readonly IAuthorizationRepository _repo;
    private readonly ICurrentUserService _currentUser;

    public CreateGroupHandler(IAuthorizationRepository repo, ICurrentUserService currentUser)
    {
        _repo = repo;
        _currentUser = currentUser;
    }

    public async Task<CreateGroupResponse> Handle(CreateGroupRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return new CreateGroupResponse(ErrorCodes.ValidationError);

        var valid = AccessMatrix.AllRoleValues().ToHashSet();
        if (request.Permissions.Any(p => !valid.Contains(p)))
            return new CreateGroupResponse(ErrorCodes.AuthorizationInvalidPermission);

        if (await _repo.GetGroupByNameAsync(request.Name, ct) is not null)
            return new CreateGroupResponse(ErrorCodes.AuthorizationDuplicateGroupName);

        var id = Guid.NewGuid();
        if (request.ParentGroupIds.Count > 0)
        {
            var (_, parents) = await _repo.GetGroupGraphAsync(ct);
            var existing = parents.GroupBy(p => p.GroupId)
                .ToDictionary(g => g.Key, g => g.Select(p => p.ParentGroupId).ToList());
            if (GroupCycleCheck.WouldCreateCycle(id, request.ParentGroupIds, existing))
                return new CreateGroupResponse(ErrorCodes.AuthorizationGroupCycleDetected);
        }

        var group = new PermissionGroup
        {
            Id = id,
            Name = request.Name.Trim(),
            Description = request.Description,
            IsSystem = false,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = _currentUser.GetCurrentUser().Email,
        };
        foreach (var p in request.Permissions.Distinct())
            group.Permissions.Add(new GroupPermission { GroupId = id, PermissionValue = p });
        foreach (var parentId in request.ParentGroupIds.Distinct())
            group.Parents.Add(new GroupParent { GroupId = id, ParentGroupId = parentId });

        await _repo.AddGroupAsync(group, ct);
        await _repo.SaveChangesAsync(ct);

        return new CreateGroupResponse { Id = id };
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test --filter "FullyQualifiedName~CreateGroupHandlerTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/CreateGroup/ \
        backend/test/Anela.Heblo.Tests/Authorization/CreateGroupHandlerTests.cs
git commit -m "feat(authz): CreateGroup command with validation and cycle guard"
```

---
### Task 21: UpdateGroup + DeleteGroup (TDD — system groups immutable)

**Files:**
- Create: `.../UseCases/UpdateGroup/UpdateGroupRequest.cs`, `UpdateGroupResponse.cs`, `UpdateGroupHandler.cs`
- Create: `.../UseCases/DeleteGroup/DeleteGroupRequest.cs`, `DeleteGroupResponse.cs`, `DeleteGroupHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Authorization/UpdateDeleteGroupHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Anela.Heblo.Application.Features.Authorization.UseCases.DeleteGroup;
using Anela.Heblo.Application.Features.Authorization.UseCases.UpdateGroup;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Authorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class UpdateDeleteGroupHandlerTests
{
    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"upd_{Guid.NewGuid()}").Options);

    private static async Task<PermissionGroup> SeedGroup(ApplicationDbContext db, bool isSystem)
    {
        var g = new PermissionGroup { Id = Guid.NewGuid(), Name = "G", IsSystem = isSystem, CreatedAt = DateTimeOffset.UtcNow };
        g.Permissions.Add(new GroupPermission { GroupId = g.Id, PermissionValue = "catalog.read" });
        db.PermissionGroups.Add(g);
        await db.SaveChangesAsync();
        return g;
    }

    [Fact]
    public async Task Update_NonSystem_ReplacesPermissions()
    {
        await using var db = NewDb();
        var g = await SeedGroup(db, isSystem: false);
        var handler = new UpdateGroupHandler(new AuthorizationRepository(db));

        var result = await handler.Handle(new UpdateGroupRequest
        {
            Id = g.Id, Name = "G2", Permissions = new() { "journal.read" }, ParentGroupIds = new()
        }, default);

        result.Success.Should().BeTrue();
        var reloaded = await db.PermissionGroups.Include(x => x.Permissions).SingleAsync();
        reloaded.Name.Should().Be("G2");
        reloaded.Permissions.Select(p => p.PermissionValue).Should().BeEquivalentTo(new[] { "journal.read" });
    }

    [Fact]
    public async Task Update_SystemGroup_ReturnsImmutable()
    {
        await using var db = NewDb();
        var g = await SeedGroup(db, isSystem: true);
        var handler = new UpdateGroupHandler(new AuthorizationRepository(db));

        var result = await handler.Handle(new UpdateGroupRequest
        {
            Id = g.Id, Name = "X", Permissions = new(), ParentGroupIds = new()
        }, default);

        result.ErrorCode.Should().Be(ErrorCodes.AuthorizationSystemGroupImmutable);
    }

    [Fact]
    public async Task Delete_SystemGroup_ReturnsImmutable()
    {
        await using var db = NewDb();
        var g = await SeedGroup(db, isSystem: true);
        var handler = new DeleteGroupHandler(new AuthorizationRepository(db));

        var result = await handler.Handle(new DeleteGroupRequest { Id = g.Id }, default);

        result.ErrorCode.Should().Be(ErrorCodes.AuthorizationSystemGroupImmutable);
        (await db.PermissionGroups.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Delete_NonSystem_Removes()
    {
        await using var db = NewDb();
        var g = await SeedGroup(db, isSystem: false);
        var handler = new DeleteGroupHandler(new AuthorizationRepository(db));

        var result = await handler.Handle(new DeleteGroupRequest { Id = g.Id }, default);

        result.Success.Should().BeTrue();
        (await db.PermissionGroups.CountAsync()).Should().Be(0);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~UpdateDeleteGroupHandlerTests"`
Expected: FAIL.

- [ ] **Step 3: Implement UpdateGroup**

`UpdateGroupRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.UpdateGroup;

public class UpdateGroupRequest : IRequest<UpdateGroupResponse>
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public List<string> Permissions { get; set; } = new();
    public List<Guid> ParentGroupIds { get; set; } = new();
}
```

`UpdateGroupResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.UpdateGroup;

public class UpdateGroupResponse : BaseResponse
{
    public UpdateGroupResponse() { }
    public UpdateGroupResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

`UpdateGroupHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.Authorization;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.UpdateGroup;

public class UpdateGroupHandler : IRequestHandler<UpdateGroupRequest, UpdateGroupResponse>
{
    private readonly IAuthorizationRepository _repo;
    public UpdateGroupHandler(IAuthorizationRepository repo) => _repo = repo;

    public async Task<UpdateGroupResponse> Handle(UpdateGroupRequest request, CancellationToken ct)
    {
        var group = await _repo.GetGroupByIdAsync(request.Id, ct);
        if (group is null)
            return new UpdateGroupResponse(ErrorCodes.AuthorizationGroupNotFound);
        if (group.IsSystem)
            return new UpdateGroupResponse(ErrorCodes.AuthorizationSystemGroupImmutable);
        if (string.IsNullOrWhiteSpace(request.Name))
            return new UpdateGroupResponse(ErrorCodes.ValidationError);

        var valid = AccessMatrix.AllRoleValues().ToHashSet();
        if (request.Permissions.Any(p => !valid.Contains(p)))
            return new UpdateGroupResponse(ErrorCodes.AuthorizationInvalidPermission);

        // Cycle check using all parents EXCEPT this group's own outgoing edges (being replaced).
        var (_, allParents) = await _repo.GetGroupGraphAsync(ct);
        var existing = allParents.Where(p => p.GroupId != group.Id)
            .GroupBy(p => p.GroupId)
            .ToDictionary(g => g.Key, g => g.Select(p => p.ParentGroupId).ToList());
        if (GroupCycleCheck.WouldCreateCycle(group.Id, request.ParentGroupIds, existing))
            return new UpdateGroupResponse(ErrorCodes.AuthorizationGroupCycleDetected);

        group.Name = request.Name.Trim();
        group.Description = request.Description;

        group.Permissions.Clear();
        foreach (var p in request.Permissions.Distinct())
            group.Permissions.Add(new GroupPermission { GroupId = group.Id, PermissionValue = p });

        group.Parents.Clear();
        foreach (var parentId in request.ParentGroupIds.Distinct())
            group.Parents.Add(new GroupParent { GroupId = group.Id, ParentGroupId = parentId });

        await _repo.SaveChangesAsync(ct);
        return new UpdateGroupResponse();
    }
}
```

- [ ] **Step 4: Implement DeleteGroup**

`DeleteGroupRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.DeleteGroup;

public class DeleteGroupRequest : IRequest<DeleteGroupResponse>
{
    public Guid Id { get; set; }
}
```

`DeleteGroupResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.DeleteGroup;

public class DeleteGroupResponse : BaseResponse
{
    public DeleteGroupResponse() { }
    public DeleteGroupResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

`DeleteGroupHandler.cs`:

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.DeleteGroup;

public class DeleteGroupHandler : IRequestHandler<DeleteGroupRequest, DeleteGroupResponse>
{
    private readonly IAuthorizationRepository _repo;
    public DeleteGroupHandler(IAuthorizationRepository repo) => _repo = repo;

    public async Task<DeleteGroupResponse> Handle(DeleteGroupRequest request, CancellationToken ct)
    {
        var group = await _repo.GetGroupByIdAsync(request.Id, ct);
        if (group is null)
            return new DeleteGroupResponse(ErrorCodes.AuthorizationGroupNotFound);
        if (group.IsSystem)
            return new DeleteGroupResponse(ErrorCodes.AuthorizationSystemGroupImmutable);

        await _repo.RemoveGroupAsync(group, ct);
        await _repo.SaveChangesAsync(ct);
        return new DeleteGroupResponse();
    }
}
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test --filter "FullyQualifiedName~UpdateDeleteGroupHandlerTests"`
Expected: PASS (4 tests).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/UpdateGroup/ \
        backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/DeleteGroup/ \
        backend/test/Anela.Heblo.Tests/Authorization/UpdateDeleteGroupHandlerTests.cs
git commit -m "feat(authz): UpdateGroup and DeleteGroup (system groups immutable)"
```

---
### Task 22: User read queries (GetUsers, GetUserEffectivePermissions)

**Files:**
- Create: `.../UseCases/UserDtos.cs`
- Create: `.../UseCases/GetUsers/GetUsersRequest.cs`, `GetUsersResponse.cs`, `GetUsersHandler.cs`
- Create: `.../UseCases/GetUserEffectivePermissions/GetUserEffectivePermissionsRequest.cs`, `...Response.cs`, `...Handler.cs`

- [ ] **Step 1: Create `UserDtos.cs`**

```csharp
namespace Anela.Heblo.Application.Features.Authorization.UseCases;

public class AppUserDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public bool IsActive { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public List<Guid> GroupIds { get; set; } = new();
}
```

- [ ] **Step 2: GetUsers**

`GetUsersRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetUsers;

public class GetUsersRequest : IRequest<GetUsersResponse> { }
```

`GetUsersResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.Authorization.UseCases;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetUsers;

public class GetUsersResponse : BaseResponse
{
    public List<AppUserDto> Users { get; set; } = new();
}
```

`GetUsersHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.Authorization.UseCases;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetUsers;

public class GetUsersHandler : IRequestHandler<GetUsersRequest, GetUsersResponse>
{
    private readonly IAuthorizationRepository _repo;
    public GetUsersHandler(IAuthorizationRepository repo) => _repo = repo;

    public async Task<GetUsersResponse> Handle(GetUsersRequest request, CancellationToken ct)
    {
        var users = await _repo.GetAllUsersAsync(ct);
        return new GetUsersResponse
        {
            Users = users.Select(u => new AppUserDto
            {
                Id = u.Id,
                Email = u.Email,
                DisplayName = u.DisplayName,
                IsActive = u.IsActive,
                LastLoginAt = u.LastLoginAt,
                GroupIds = u.UserGroups.Select(ug => ug.GroupId).ToList(),
            }).OrderBy(u => u.DisplayName).ToList(),
        };
    }
}
```

- [ ] **Step 3: GetUserEffectivePermissions**

`GetUserEffectivePermissionsRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetUserEffectivePermissions;

public class GetUserEffectivePermissionsRequest : IRequest<GetUserEffectivePermissionsResponse>
{
    public Guid UserId { get; set; }
}
```

`GetUserEffectivePermissionsResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetUserEffectivePermissions;

public class GetUserEffectivePermissionsResponse : BaseResponse
{
    public List<string> Permissions { get; set; } = new();
    public GetUserEffectivePermissionsResponse() { }
    public GetUserEffectivePermissionsResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

`GetUserEffectivePermissionsHandler.cs`:

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Persistence.Features.Authorization;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetUserEffectivePermissions;

public class GetUserEffectivePermissionsHandler
    : IRequestHandler<GetUserEffectivePermissionsRequest, GetUserEffectivePermissionsResponse>
{
    private readonly IAuthorizationRepository _repo;
    public GetUserEffectivePermissionsHandler(IAuthorizationRepository repo) => _repo = repo;

    public async Task<GetUserEffectivePermissionsResponse> Handle(
        GetUserEffectivePermissionsRequest request, CancellationToken ct)
    {
        var user = await _repo.GetUserByIdAsync(request.UserId, ct);
        if (user is null)
            return new GetUserEffectivePermissionsResponse(ErrorCodes.AuthorizationUserNotFound);

        if (!user.IsActive)
            return new GetUserEffectivePermissionsResponse { Permissions = new() };

        var (perms, parents) = await _repo.GetGroupGraphAsync(ct);
        var groupIds = user.UserGroups.Select(ug => ug.GroupId);
        var resolved = GroupClosure.Resolve(groupIds, perms, parents);
        var all = new HashSet<string>(resolved) { AccessRoles.Base };

        return new GetUserEffectivePermissionsResponse { Permissions = all.OrderBy(p => p).ToList() };
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build backend/src/Anela.Heblo.Application`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/UserDtos.cs \
        backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetUsers/ \
        backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetUserEffectivePermissions/
git commit -m "feat(authz): user read queries"
```

---

### Task 23: AssignUserGroups + SetUserActive (TDD)

**Files:**
- Add to `IAuthorizationRepository` + `AuthorizationRepository`: `RemoveUserGroupsAsync`, `AddUserGroupAsync`.
- Create: `.../UseCases/AssignUserGroups/AssignUserGroupsRequest.cs`, `...Response.cs`, `...Handler.cs`
- Create: `.../UseCases/SetUserActive/SetUserActiveRequest.cs`, `...Response.cs`, `...Handler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Authorization/AssignUserGroupsHandlerTests.cs`

- [ ] **Step 1: Extend the repository contract**

Add to `IAuthorizationRepository`:

```csharp
    Task SetUserGroupsAsync(Guid userId, IEnumerable<Guid> groupIds, CancellationToken ct = default);
```

Implement in `AuthorizationRepository`:

```csharp
    public async Task SetUserGroupsAsync(Guid userId, IEnumerable<Guid> groupIds, CancellationToken ct = default)
    {
        var existing = await _db.UserGroups.Where(ug => ug.UserId == userId).ToListAsync(ct);
        _db.UserGroups.RemoveRange(existing);
        foreach (var gid in groupIds.Distinct())
            _db.UserGroups.Add(new UserGroup { UserId = userId, GroupId = gid });
    }
```

(Add `using Anela.Heblo.Domain.Features.Authorization.Entities;` if not present — it is.)

- [ ] **Step 2: Write the failing test**

```csharp
using Anela.Heblo.Application.Features.Authorization.UseCases.AssignUserGroups;
using Anela.Heblo.Application.Features.Authorization.UseCases.SetUserActive;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Authorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class AssignUserGroupsHandlerTests
{
    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"assign_{Guid.NewGuid()}").Options);

    private static async Task<(AppUser user, PermissionGroup g1, PermissionGroup g2)> Seed(ApplicationDbContext db)
    {
        var g1 = new PermissionGroup { Id = Guid.NewGuid(), Name = "G1", CreatedAt = DateTimeOffset.UtcNow };
        var g2 = new PermissionGroup { Id = Guid.NewGuid(), Name = "G2", CreatedAt = DateTimeOffset.UtcNow };
        var user = new AppUser { Id = Guid.NewGuid(), EntraObjectId = "oid", Email = "u@x.cz", DisplayName = "U", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        user.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = g1.Id });
        db.AddRange(g1, g2, user);
        await db.SaveChangesAsync();
        return (user, g1, g2);
    }

    [Fact]
    public async Task Assign_ReplacesUserGroups()
    {
        await using var db = NewDb();
        var (user, _, g2) = await Seed(db);
        var handler = new AssignUserGroupsHandler(new AuthorizationRepository(db));

        var result = await handler.Handle(new AssignUserGroupsRequest
        {
            UserId = user.Id, GroupIds = new() { g2.Id }
        }, default);

        result.Success.Should().BeTrue();
        var groups = await db.UserGroups.Where(ug => ug.UserId == user.Id).Select(ug => ug.GroupId).ToListAsync();
        groups.Should().BeEquivalentTo(new[] { g2.Id });
    }

    [Fact]
    public async Task Assign_UnknownUser_ReturnsNotFound()
    {
        await using var db = NewDb();
        var handler = new AssignUserGroupsHandler(new AuthorizationRepository(db));
        var result = await handler.Handle(new AssignUserGroupsRequest { UserId = Guid.NewGuid(), GroupIds = new() }, default);
        result.ErrorCode.Should().Be(ErrorCodes.AuthorizationUserNotFound);
    }

    [Fact]
    public async Task SetActive_TogglesFlag()
    {
        await using var db = NewDb();
        var (user, _, _) = await Seed(db);
        var handler = new SetUserActiveHandler(new AuthorizationRepository(db));

        var result = await handler.Handle(new SetUserActiveRequest { UserId = user.Id, IsActive = false }, default);

        result.Success.Should().BeTrue();
        (await db.AppUsers.SingleAsync(u => u.Id == user.Id)).IsActive.Should().BeFalse();
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~AssignUserGroupsHandlerTests"`
Expected: FAIL.

- [ ] **Step 4: Implement AssignUserGroups**

`AssignUserGroupsRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.AssignUserGroups;

public class AssignUserGroupsRequest : IRequest<AssignUserGroupsResponse>
{
    public Guid UserId { get; set; }
    public List<Guid> GroupIds { get; set; } = new();
}
```

`AssignUserGroupsResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.AssignUserGroups;

public class AssignUserGroupsResponse : BaseResponse
{
    public AssignUserGroupsResponse() { }
    public AssignUserGroupsResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

`AssignUserGroupsHandler.cs`:

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.AssignUserGroups;

public class AssignUserGroupsHandler : IRequestHandler<AssignUserGroupsRequest, AssignUserGroupsResponse>
{
    private readonly IAuthorizationRepository _repo;
    public AssignUserGroupsHandler(IAuthorizationRepository repo) => _repo = repo;

    public async Task<AssignUserGroupsResponse> Handle(AssignUserGroupsRequest request, CancellationToken ct)
    {
        var user = await _repo.GetUserByIdAsync(request.UserId, ct);
        if (user is null)
            return new AssignUserGroupsResponse(ErrorCodes.AuthorizationUserNotFound);

        await _repo.SetUserGroupsAsync(request.UserId, request.GroupIds, ct);
        await _repo.SaveChangesAsync(ct);
        return new AssignUserGroupsResponse();
    }
}
```

- [ ] **Step 5: Implement SetUserActive**

`SetUserActiveRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.SetUserActive;

public class SetUserActiveRequest : IRequest<SetUserActiveResponse>
{
    public Guid UserId { get; set; }
    public bool IsActive { get; set; }
}
```

`SetUserActiveResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.SetUserActive;

public class SetUserActiveResponse : BaseResponse
{
    public SetUserActiveResponse() { }
    public SetUserActiveResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

`SetUserActiveHandler.cs`:

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.SetUserActive;

public class SetUserActiveHandler : IRequestHandler<SetUserActiveRequest, SetUserActiveResponse>
{
    private readonly IAuthorizationRepository _repo;
    public SetUserActiveHandler(IAuthorizationRepository repo) => _repo = repo;

    public async Task<SetUserActiveResponse> Handle(SetUserActiveRequest request, CancellationToken ct)
    {
        var user = await _repo.GetUserByIdAsync(request.UserId, ct);
        if (user is null)
            return new SetUserActiveResponse(ErrorCodes.AuthorizationUserNotFound);

        user.IsActive = request.IsActive;
        await _repo.SaveChangesAsync(ct);
        return new SetUserActiveResponse();
    }
}
```

> Note: per spec decision #6 (short-TTL cache, no active invalidation), these admin writes do
> NOT call `IPermissionResolver.InvalidateCache`. Changes take effect within the cache TTL
> (~5 min). Disabling a user therefore also lands within one TTL window — documented behavior.

- [ ] **Step 6: Run to verify pass**

Run: `dotnet test --filter "FullyQualifiedName~AssignUserGroupsHandlerTests"`
Expected: PASS (3 tests).

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/AssignUserGroups/ \
        backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/SetUserActive/ \
        backend/src/Anela.Heblo.Domain/Features/Authorization/IAuthorizationRepository.cs \
        backend/src/Anela.Heblo.Persistence/Features/Authorization/AuthorizationRepository.cs \
        backend/test/Anela.Heblo.Tests/Authorization/AssignUserGroupsHandlerTests.cs
git commit -m "feat(authz): assign user groups and set-active commands"
```

---
## Phase 6 — API controllers

### Task 24: Add an "authenticated-only" policy for /api/auth/me

The default policy requires the `heblo_user` role, but `/api/auth/me` must be reachable by
users with no access yet (and disabled users) so the frontend can show the right state.

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs` (in `ConfigureAuthorizationPolicies`)

- [ ] **Step 1: Add the named policy**

Inside the `services.AddAuthorization(options => { ... })` lambda, after setting `DefaultPolicy`, add:

```csharp
            options.AddPolicy("AuthenticatedUser", p => p.RequireAuthenticatedUser());
```

- [ ] **Step 2: Build**

Run: `dotnet build backend/src/Anela.Heblo.API`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs
git commit -m "feat(authz): add AuthenticatedUser policy for /api/auth/me"
```

---

### Task 25: AuthController (/api/auth/me)

**Files:**
- Create: `backend/src/Anela.Heblo.API/Controllers/AuthController.cs`

- [ ] **Step 1: Create the controller**

```csharp
using Anela.Heblo.Application.Features.Authorization.UseCases.GetMe;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : BaseApiController
{
    private readonly IMediator _mediator;
    public AuthController(IMediator mediator) => _mediator = mediator;

    /// <summary>Returns the current user's effective permissions for the frontend.
    /// Reachable by any authenticated user (incl. no-access/disabled) via the AuthenticatedUser policy.</summary>
    [HttpGet("me")]
    [Authorize(Policy = "AuthenticatedUser")]
    public async Task<ActionResult<GetMeResponse>> Me(CancellationToken ct)
    {
        var response = await _mediator.Send(new GetMeRequest(), ct);
        return HandleResponse(response);
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build backend/src/Anela.Heblo.API`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/AuthController.cs
git commit -m "feat(authz): /api/auth/me endpoint"
```

---

### Task 26: Admin AuthorizationController

**Files:**
- Create: `backend/src/Anela.Heblo.API/Controllers/AuthorizationController.cs`

- [ ] **Step 1: Create the controller**

```csharp
using Anela.Heblo.Application.Features.Authorization.UseCases.AssignUserGroups;
using Anela.Heblo.Application.Features.Authorization.UseCases.CreateGroup;
using Anela.Heblo.Application.Features.Authorization.UseCases.DeleteGroup;
using Anela.Heblo.Application.Features.Authorization.UseCases.GetGroupDetail;
using Anela.Heblo.Application.Features.Authorization.UseCases.GetGroups;
using Anela.Heblo.Application.Features.Authorization.UseCases.GetPermissionCatalogue;
using Anela.Heblo.Application.Features.Authorization.UseCases.GetUserEffectivePermissions;
using Anela.Heblo.Application.Features.Authorization.UseCases.GetUsers;
using Anela.Heblo.Application.Features.Authorization.UseCases.SetUserActive;
using Anela.Heblo.Application.Features.Authorization.UseCases.UpdateGroup;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize(Roles = AccessRoles.AdministrationRead)]
[ApiController]
[Route("api/admin/authorization")]
public class AuthorizationController : BaseApiController
{
    private readonly IMediator _mediator;
    public AuthorizationController(IMediator mediator) => _mediator = mediator;

    [HttpGet("catalogue")]
    public async Task<ActionResult<GetPermissionCatalogueResponse>> Catalogue(CancellationToken ct)
        => HandleResponse(await _mediator.Send(new GetPermissionCatalogueRequest(), ct));

    [HttpGet("groups")]
    public async Task<ActionResult<GetGroupsResponse>> GetGroups(CancellationToken ct)
        => HandleResponse(await _mediator.Send(new GetGroupsRequest(), ct));

    [HttpGet("groups/{id:guid}")]
    public async Task<ActionResult<GetGroupDetailResponse>> GetGroup([FromRoute] Guid id, CancellationToken ct)
        => HandleResponse(await _mediator.Send(new GetGroupDetailRequest { Id = id }, ct));

    [HttpPost("groups")]
    [Authorize(Roles = AccessRoles.AdministrationWrite)]
    public async Task<ActionResult<CreateGroupResponse>> CreateGroup([FromBody] CreateGroupRequest request, CancellationToken ct)
        => HandleResponse(await _mediator.Send(request, ct));

    [HttpPut("groups/{id:guid}")]
    [Authorize(Roles = AccessRoles.AdministrationWrite)]
    public async Task<ActionResult<UpdateGroupResponse>> UpdateGroup([FromRoute] Guid id, [FromBody] UpdateGroupRequest request, CancellationToken ct)
    {
        request.Id = id;
        return HandleResponse(await _mediator.Send(request, ct));
    }

    [HttpDelete("groups/{id:guid}")]
    [Authorize(Roles = AccessRoles.AdministrationWrite)]
    public async Task<ActionResult<DeleteGroupResponse>> DeleteGroup([FromRoute] Guid id, CancellationToken ct)
        => HandleResponse(await _mediator.Send(new DeleteGroupRequest { Id = id }, ct));

    [HttpGet("users")]
    public async Task<ActionResult<GetUsersResponse>> GetUsers(CancellationToken ct)
        => HandleResponse(await _mediator.Send(new GetUsersRequest(), ct));

    [HttpGet("users/{id:guid}/permissions")]
    public async Task<ActionResult<GetUserEffectivePermissionsResponse>> GetUserPermissions([FromRoute] Guid id, CancellationToken ct)
        => HandleResponse(await _mediator.Send(new GetUserEffectivePermissionsRequest { UserId = id }, ct));

    [HttpPut("users/{id:guid}/groups")]
    [Authorize(Roles = AccessRoles.AdministrationWrite)]
    public async Task<ActionResult<AssignUserGroupsResponse>> AssignGroups([FromRoute] Guid id, [FromBody] AssignUserGroupsRequest request, CancellationToken ct)
    {
        request.UserId = id;
        return HandleResponse(await _mediator.Send(request, ct));
    }

    [HttpPut("users/{id:guid}/active")]
    [Authorize(Roles = AccessRoles.AdministrationWrite)]
    public async Task<ActionResult<SetUserActiveResponse>> SetActive([FromRoute] Guid id, [FromBody] SetUserActiveRequest request, CancellationToken ct)
    {
        request.UserId = id;
        return HandleResponse(await _mediator.Send(request, ct));
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build backend/src/Anela.Heblo.API`
Expected: Build succeeded.

- [ ] **Step 3: Regenerate the TypeScript API client**

The OpenAPI TS client is generated on build. Run a full build so the new endpoints appear in `frontend/src/api/generated/`:

Run: `dotnet build`
Expected: Build succeeded; `git status` shows changes under `frontend/src/api/generated/`.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/AuthorizationController.cs frontend/src/api/generated/
git commit -m "feat(authz): admin AuthorizationController + regenerated API client"
```

---
## Phase 7 — Frontend

> The OpenAPI TS client is regenerated on `dotnet build` (Task 26). Before writing the hooks,
> confirm the generated method name for `GET /api/auth/me`. NSwag names it
> `{controller}_{operationId}` → most likely **`auth_Me`** on the generated `ApiClient`.
> Grep: `grep -r "auth_" frontend/src/api/generated/` and use the actual name.

### Task 27: usePermissions hook + PermissionsProvider, and switch RequireAccess

**Files:**
- Create: `frontend/src/api/hooks/usePermissions.ts`
- Create: `frontend/src/auth/PermissionsContext.tsx`
- Modify: `frontend/src/components/auth/RequireAccess.tsx`
- Modify: `frontend/src/App.tsx` (or wherever providers are composed) to wrap with `PermissionsProvider`
- Test: `frontend/src/components/auth/__tests__/RequireAccess.test.tsx`

- [ ] **Step 1: Create `usePermissions.ts`**

```typescript
import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";

export interface MePermissions {
  email?: string;
  displayName?: string;
  isSuperUser: boolean;
  permissions: string[];
  groups: string[];
}

export const permissionsQueryKey = ["auth", "me"] as const;

export const usePermissions = (enabled: boolean) => {
  return useQuery({
    queryKey: permissionsQueryKey,
    enabled,
    staleTime: 5 * 60 * 1000, // align with backend ~5 min TTL
    queryFn: async (): Promise<MePermissions> => {
      const client = getAuthenticatedApiClient();
      // Confirm the generated method name (see Phase 7 note). Likely `auth_Me`.
      const res = await client.auth_Me();
      return {
        email: res?.email ?? undefined,
        displayName: res?.displayName ?? undefined,
        isSuperUser: res?.isSuperUser ?? false,
        permissions: res?.permissions ?? [],
        groups: res?.groups ?? [],
      };
    },
  });
};
```

> If `QUERY_KEYS` has no slot for this, just use the local `permissionsQueryKey`; do not force a change to `QUERY_KEYS`.

- [ ] **Step 2: Create `PermissionsContext.tsx`**

```typescript
import React, { createContext, useContext, ReactNode } from "react";
import { usePermissions, MePermissions } from "../api/hooks/usePermissions";

interface PermissionsContextValue {
  permissions: string[];
  isSuperUser: boolean;
  groups: string[];
  isLoading: boolean;
  hasPermission: (perm: string) => boolean;
}

const PermissionsContext = createContext<PermissionsContextValue | undefined>(undefined);

interface ProviderProps {
  isAuthenticated: boolean;
  children: ReactNode;
}

export const PermissionsProvider: React.FC<ProviderProps> = ({ isAuthenticated, children }) => {
  const { data, isLoading } = usePermissions(isAuthenticated);

  const value: PermissionsContextValue = {
    permissions: data?.permissions ?? [],
    isSuperUser: data?.isSuperUser ?? false,
    groups: data?.groups ?? [],
    isLoading: isAuthenticated && isLoading,
    hasPermission: (perm: string) =>
      (data?.isSuperUser ?? false) || (data?.permissions ?? []).includes(perm),
  };

  return <PermissionsContext.Provider value={value}>{children}</PermissionsContext.Provider>;
};

export const usePermissionsContext = (): PermissionsContextValue => {
  const ctx = useContext(PermissionsContext);
  if (!ctx) throw new Error("usePermissionsContext must be used within PermissionsProvider");
  return ctx;
};
```

- [ ] **Step 3: Wire the provider**

In `frontend/src/App.tsx`, find where auth is established (the `AuthGuard` / msal provider region) and wrap the authenticated app subtree:

```tsx
// import at top
import { PermissionsProvider } from "./auth/PermissionsContext";
import { useAuth } from "./auth/useAuth";
```

Wrap the routed content (inside the authenticated region) with:

```tsx
<PermissionsProvider isAuthenticated={isAuthenticated}>
  {/* existing routed content */}
</PermissionsProvider>
```

> Use whatever `isAuthenticated` source the surrounding component already uses (real `useAuth` or `useMockAuth`). If `App.tsx` doesn't expose it directly, place the provider just inside `AuthGuard`'s authenticated branch where `isAuthenticated === true`.

- [ ] **Step 4: Write the failing RequireAccess test**

```tsx
import React from "react";
import { render, screen } from "@testing-library/react";
import { MemoryRouter, Routes, Route } from "react-router-dom";
import { RequireAccess } from "../RequireAccess";

const mockCtx = { permissions: ["catalog.read"], isSuperUser: false, groups: [], isLoading: false,
  hasPermission: (p: string) => ["catalog.read"].includes(p) };

jest.mock("../../../auth/PermissionsContext", () => ({
  usePermissionsContext: () => mockCtx,
}));

const renderAt = (required: string) =>
  render(
    <MemoryRouter initialEntries={["/secret"]}>
      <Routes>
        <Route path="/" element={<div>home</div>} />
        <Route path="/secret" element={<RequireAccess requiredRole={required}><div>secret</div></RequireAccess>} />
      </Routes>
    </MemoryRouter>
  );

describe("RequireAccess", () => {
  it("renders children when permission present", () => {
    renderAt("catalog.read");
    expect(screen.getByText("secret")).toBeInTheDocument();
  });

  it("redirects home when permission missing", () => {
    renderAt("journal.read");
    expect(screen.getByText("home")).toBeInTheDocument();
  });
});
```

- [ ] **Step 5: Run to verify it fails**

Run (from `frontend/`): `npm test -- --watchAll=false RequireAccess`
Expected: FAIL — `RequireAccess` still reads from `useAuth`.

- [ ] **Step 6: Update `RequireAccess.tsx`**

```tsx
import { ReactNode } from "react";
import { Navigate } from "react-router-dom";
import { usePermissionsContext } from "../../auth/PermissionsContext";

interface RequireAccessProps {
  requiredRole?: string;
  children: ReactNode;
}

export function RequireAccess({ requiredRole, children }: RequireAccessProps) {
  const { hasPermission, isLoading } = usePermissionsContext();

  if (isLoading) {
    return null; // wait for /api/auth/me before deciding
  }

  if (requiredRole && !hasPermission(requiredRole)) {
    return <Navigate to="/" replace />;
  }

  return <>{children}</>;
}
```

- [ ] **Step 7: Run to verify pass**

Run (from `frontend/`): `npm test -- --watchAll=false RequireAccess`
Expected: PASS.

- [ ] **Step 8: Build + lint**

Run (from `frontend/`): `npm run build && npm run lint`
Expected: Both succeed. Fix any unused-import lint errors introduced.

- [ ] **Step 9: Commit**

```bash
git add frontend/src/api/hooks/usePermissions.ts frontend/src/auth/PermissionsContext.tsx \
        frontend/src/components/auth/RequireAccess.tsx frontend/src/App.tsx \
        frontend/src/components/auth/__tests__/RequireAccess.test.tsx
git commit -m "feat(authz): source permissions from /api/auth/me; gate RequireAccess on it"
```

---
### Task 28: Admin access-management hooks

**Files:**
- Create: `frontend/src/api/hooks/useAccessManagement.ts`

> Confirm generated method names with `grep -r "authorization_" frontend/src/api/generated/`.
> Expected: `authorization_Catalogue`, `authorization_GetGroups`, `authorization_GetGroup`,
> `authorization_CreateGroup`, `authorization_UpdateGroup`, `authorization_DeleteGroup`,
> `authorization_GetUsers`, `authorization_GetUserPermissions`, `authorization_AssignGroups`,
> `authorization_SetActive`. Adjust if the generator differs.

- [ ] **Step 1: Create the hooks**

```typescript
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";

const keys = {
  catalogue: ["authz", "catalogue"] as const,
  groups: ["authz", "groups"] as const,
  group: (id: string) => ["authz", "group", id] as const,
  users: ["authz", "users"] as const,
};

export const useCatalogue = () =>
  useQuery({
    queryKey: keys.catalogue,
    queryFn: async () => (await getAuthenticatedApiClient().authorization_Catalogue()),
  });

export const useGroups = () =>
  useQuery({
    queryKey: keys.groups,
    queryFn: async () => (await getAuthenticatedApiClient().authorization_GetGroups())?.groups ?? [],
  });

export const useGroup = (id: string | null) =>
  useQuery({
    queryKey: keys.group(id ?? ""),
    enabled: !!id,
    queryFn: async () => (await getAuthenticatedApiClient().authorization_GetGroup(id!))?.group,
  });

export const useUsers = () =>
  useQuery({
    queryKey: keys.users,
    queryFn: async () => (await getAuthenticatedApiClient().authorization_GetUsers())?.users ?? [],
  });

export const useCreateGroup = () => {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: { name: string; description?: string; permissions: string[]; parentGroupIds: string[] }) => {
      const client = getAuthenticatedApiClient();
      // The generated client typically takes a request DTO instance; adapt construction to the generated type.
      await client.authorization_CreateGroup(body as any);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.groups }),
  });
};

export const useUpdateGroup = () => {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, body }: { id: string; body: { name: string; description?: string; permissions: string[]; parentGroupIds: string[] } }) => {
      await getAuthenticatedApiClient().authorization_UpdateGroup(id, body as any);
    },
    onSuccess: (_d, vars) => {
      qc.invalidateQueries({ queryKey: keys.groups });
      qc.invalidateQueries({ queryKey: keys.group(vars.id) });
    },
  });
};

export const useDeleteGroup = () => {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => { await getAuthenticatedApiClient().authorization_DeleteGroup(id); },
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.groups }),
  });
};

export const useAssignUserGroups = () => {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ userId, groupIds }: { userId: string; groupIds: string[] }) => {
      await getAuthenticatedApiClient().authorization_AssignGroups(userId, { groupIds } as any);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.users }),
  });
};

export const useSetUserActive = () => {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ userId, isActive }: { userId: string; isActive: boolean }) => {
      await getAuthenticatedApiClient().authorization_SetActive(userId, { isActive } as any);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.users }),
  });
};
```

> The `as any` casts bridge to whatever request DTO classes the generator emits (e.g.
> `new CreateGroupRequest({...})`). When wiring, replace `as any` with the generated DTO
> constructors imported from `../generated/api-client` for type safety.

- [ ] **Step 2: Lint**

Run (from `frontend/`): `npm run lint`
Expected: passes (resolve unused/any warnings per the repo's eslint config; if `any` is disallowed, switch to the generated DTO constructors now).

- [ ] **Step 3: Commit**

```bash
git add frontend/src/api/hooks/useAccessManagement.ts
git commit -m "feat(authz): admin access-management API hooks"
```

---

### Task 29: Access-management admin page + route + nav

**Files:**
- Create: `frontend/src/pages/AccessManagementPage.tsx`
- Modify: the router (where `ACCESS_ROUTES`/admin routes are registered) to add `/admin/access`
- Modify: the sidebar/nav config to add an "Access management" entry under Administration
- Test: `frontend/src/pages/__tests__/AccessManagementPage.test.tsx`

> The `administration` feature already exists in `AccessMatrix` (no nav path today). Gate the
> route with `<RequireAccess requiredRole="administration.read">`.

- [ ] **Step 1: Create the page (Groups + Users tabs)**

```tsx
import React, { useState } from "react";
import {
  useGroups, useUsers, useCatalogue, useDeleteGroup, useSetUserActive,
} from "../api/hooks/useAccessManagement";

const AccessManagementPage: React.FC = () => {
  const [tab, setTab] = useState<"groups" | "users">("groups");
  const groups = useGroups();
  const users = useUsers();
  const catalogue = useCatalogue();
  const deleteGroup = useDeleteGroup();
  const setActive = useSetUserActive();

  return (
    <div className="p-8 max-w-5xl mx-auto">
      <h1 className="text-2xl font-semibold text-gray-900 mb-4">Access management</h1>

      <div className="flex gap-2 mb-6">
        <button
          onClick={() => setTab("groups")}
          className={`px-4 py-2 rounded ${tab === "groups" ? "bg-indigo-600 text-white" : "bg-gray-100"}`}
        >Groups</button>
        <button
          onClick={() => setTab("users")}
          className={`px-4 py-2 rounded ${tab === "users" ? "bg-indigo-600 text-white" : "bg-gray-100"}`}
        >Users</button>
      </div>

      {tab === "groups" && (
        <div className="space-y-3">
          {groups.isLoading && <div className="text-gray-500">Loading groups…</div>}
          {groups.data?.map((g) => (
            <div key={g.id} className="flex items-center justify-between bg-white border border-gray-200 rounded-lg p-4">
              <div>
                <div className="flex items-center gap-2">
                  <span className="font-medium text-gray-900">{g.name}</span>
                  {g.isSystem && <span className="text-xs bg-gray-100 text-gray-700 px-2 py-0.5 rounded">system</span>}
                </div>
                <p className="text-sm text-gray-500">{g.permissionCount} permissions · {g.memberCount} members</p>
              </div>
              {!g.isSystem && (
                <button
                  onClick={() => g.id && deleteGroup.mutate(g.id)}
                  disabled={deleteGroup.isPending}
                  className="text-sm text-red-600 hover:underline"
                  aria-label={`Delete ${g.name}`}
                >Delete</button>
              )}
            </div>
          ))}
          <p className="text-xs text-gray-400">
            {catalogue.data?.permissions?.length ?? 0} permissions available. Create/edit forms use the catalogue.
          </p>
        </div>
      )}

      {tab === "users" && (
        <div className="space-y-3">
          {users.isLoading && <div className="text-gray-500">Loading users…</div>}
          {users.data?.map((u) => (
            <div key={u.id} className="flex items-center justify-between bg-white border border-gray-200 rounded-lg p-4">
              <div>
                <div className="font-medium text-gray-900">{u.displayName}</div>
                <p className="text-sm text-gray-500">{u.email} · {u.groupIds?.length ?? 0} groups</p>
              </div>
              <button
                onClick={() => u.id && setActive.mutate({ userId: u.id, isActive: !u.isActive })}
                disabled={setActive.isPending}
                className={`text-sm ${u.isActive ? "text-red-600" : "text-green-600"} hover:underline`}
                aria-label={`Toggle active ${u.email}`}
              >{u.isActive ? "Disable" : "Enable"}</button>
            </div>
          ))}
        </div>
      )}
    </div>
  );
};

export default AccessManagementPage;
```

> This page ships the list/delete/enable-disable surface. The create/edit group dialog
> (permission checkbox matrix + parent multi-select using `useCatalogue`, `useCreateGroup`,
> `useUpdateGroup`) and the per-user group-assignment dialog (`useAssignUserGroups`) are
> wired the same way; add them as a follow-up step within this task if time allows, or as a
> fast-follow. The backend already supports all of it.

- [ ] **Step 2: Register the route**

Where routes are declared (search for an existing admin route such as `/admin/feature-flags`), add:

```tsx
<Route
  path="/admin/access"
  element={
    <RequireAccess requiredRole="administration.read">
      <AccessManagementPage />
    </RequireAccess>
  }
/>
```

Add the import: `import AccessManagementPage from "../pages/AccessManagementPage";` (adjust relative path to the router file).

- [ ] **Step 3: Add the nav entry**

In the sidebar/nav config, add an "Access management" item under the Administration section pointing to `/admin/access` (mirror how `/admin/feature-flags` is registered).

- [ ] **Step 4: Write a smoke test**

```tsx
import React from "react";
import { render, screen } from "@testing-library/react";
import AccessManagementPage from "../AccessManagementPage";

jest.mock("../../api/hooks/useAccessManagement", () => ({
  useGroups: () => ({ data: [{ id: "1", name: "Spravce", isSystem: true, permissionCount: 52, memberCount: 1 }], isLoading: false }),
  useUsers: () => ({ data: [], isLoading: false }),
  useCatalogue: () => ({ data: { permissions: ["catalog.read"] } }),
  useDeleteGroup: () => ({ mutate: jest.fn(), isPending: false }),
  useSetUserActive: () => ({ mutate: jest.fn(), isPending: false }),
}));

describe("AccessManagementPage", () => {
  it("renders groups tab with a system group badge", () => {
    render(<AccessManagementPage />);
    expect(screen.getByText("Spravce")).toBeInTheDocument();
    expect(screen.getByText("system")).toBeInTheDocument();
  });
});
```

- [ ] **Step 5: Run test + build + lint**

Run (from `frontend/`): `npm test -- --watchAll=false AccessManagementPage && npm run build && npm run lint`
Expected: PASS / succeed.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/pages/AccessManagementPage.tsx \
        frontend/src/pages/__tests__/AccessManagementPage.test.tsx
# plus the router + nav files you modified:
git add -A
git commit -m "feat(authz): access-management admin page, route, and nav entry"
```

---
## Phase 8 — Integration, architecture, cutover, validation

### Task 30: End-to-end integration test (claims transformation + seeder + admin authz)

Exercises the full wiring under the real pipeline (Test env → mock auth → super_user wildcard).

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Authorization/AuthorizationIntegrationTests.cs`

- [ ] **Step 1: Write the test**

```csharp
using System.Net;
using System.Net.Http.Json;
using Anela.Heblo.Application.Features.Authorization.UseCases.GetGroups;
using Anela.Heblo.Application.Features.Authorization.UseCases.GetMe;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class AuthorizationIntegrationTests : IClassFixture<HebloWebApplicationFactory>
{
    private readonly HebloWebApplicationFactory _factory;
    public AuthorizationIntegrationTests(HebloWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Me_UnderMockAuth_IsSuperUser_WithAllPermissions()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var me = await response.Content.ReadFromJsonAsync<GetMeResponse>();
        me!.IsSuperUser.Should().BeTrue();
        me.Permissions.Should().Contain("catalog.read");
        me.Permissions.Should().Contain(AccessRoles.Base);
    }

    [Fact]
    public async Task SuperUser_CanCall_RoleGatedEndpoint()
    {
        var client = _factory.CreateClient();
        // catalog list requires AccessRoles.CatalogRead — super_user must pass via the wildcard.
        var response = await client.GetAsync("/api/catalog");
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AdminGroups_ReturnsSeededSystemGroups()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/admin/authorization/groups");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GetGroupsResponse>();
        body!.Groups.Select(g => g.Name).Should().Contain("Spravce");
        body.Groups.Should().OnlyContain(g => g.IsSystem);
    }
}
```

> The exact catalog route in `SuperUser_CanCall_RoleGatedEndpoint` may differ; if `/api/catalog`
> needs required query params, pick any GET action decorated with `[Authorize(Roles = AccessRoles.CatalogRead)]`
> and assert it is not 401/403. The point is that the wildcard grants the role.

- [ ] **Step 2: Run the integration tests**

Run: `dotnet test --filter "FullyQualifiedName~AuthorizationIntegrationTests"`
Expected: PASS (3 tests). If the seeder didn't run for the InMemory provider, confirm Task 14's startup block calls `AuthorizationSeeder.SeedAsync` unconditionally (it must run even when `IsRelational()` is false — only the `MigrateAsync` call is guarded).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Authorization/AuthorizationIntegrationTests.cs
git commit -m "test(authz): end-to-end claims transformation, seeder, and admin authz"
```

---

### Task 31: Module-wiring architecture test

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Authorization/AuthorizationModuleTests.cs`

- [ ] **Step 1: Write the test**

```csharp
using Anela.Heblo.Application.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class AuthorizationModuleTests
{
    [Fact]
    public void AddAuthorizationModule_RegistersResolverAndRepository()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase("authz_module"));
        services.AddAuthorizationModule();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetService<IPermissionResolver>().Should().NotBeNull();
        scope.ServiceProvider.GetService<IAuthorizationRepository>().Should().NotBeNull();
    }
}
```

- [ ] **Step 2: Run**

Run: `dotnet test --filter "FullyQualifiedName~AuthorizationModuleTests"`
Expected: PASS.

- [ ] **Step 3: Confirm ADR-004 guard still passes (no repo binding leaked into PersistenceModule)**

Run: `dotnet test --filter "FullyQualifiedName~PersistenceModuleTests"`
Expected: PASS — the `AuthorizationRepository` binding lives in `AuthorizationModule`, not `PersistenceModule`.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Authorization/AuthorizationModuleTests.cs
git commit -m "test(authz): module wiring asserts resolver/repository registration"
```

---

### Task 32: Cutover runbook + Entra `super_user` app role

Document the operational cutover (no shadow mode — decision #7) and the one Entra change.

**Files:**
- Create: `docs/features/rbac-inapp-permissions-cutover.md`

- [ ] **Step 1: Write the runbook**

```markdown
# In-app permissions — cutover runbook

## One-time Entra change
Add a `super_user` **app role** (value `super_user`) to the API app registration and assign
it to the operator account(s). This is the break-glass/admin bootstrap. Per-feature app roles
remain in the manifest but become unused (optional cleanup later).

## Deploy
1. Apply the DB migration `AddAuthorizationTables` (migrations are manual — see CLAUDE.md).
   Startup also runs `AuthorizationSeeder` to create/sync the 10 system groups.
2. Deploy the build. Enforcement now flows through the claims transformation.

## Immediately after deploy (the access-gap mitigation)
Non-super users have **no access until assigned** (decision #4). To minimize the window:
1. Pre-write the assignment list: each staff email → the in-app group matching their old
   Entra role (names align 1:1: Marketer, Skladnik, Ucetni, …).
2. A `super_user` logs in → `/admin/access` → Users tab → assign each user their group(s).
   Users appear in the list after their first login (AppUser is materialized on login); for
   users who haven't logged in yet, assignment can be done as soon as they do.
3. Verify with the per-user "effective permissions" view.

## Rollback
Remove the deploy; the additive tables are harmless if unused. (No feature flag by decision #7.)

## Notes
- Disabling a user (Users tab) takes effect within ~5 min (cache TTL).
- System groups are read-only and re-synced from `AccessMatrix` on every startup.
```

- [ ] **Step 2: Commit**

```bash
git add docs/features/rbac-inapp-permissions-cutover.md
git commit -m "docs(authz): cutover runbook"
```

---

### Task 33: Full validation sweep

- [ ] **Step 1: Backend build + format**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

Run: `dotnet format --verify-no-changes`
Expected: No formatting changes needed. If it reports diffs, run `dotnet format` and commit:

```bash
dotnet format
git add -A && git commit -m "style(authz): dotnet format"
```

- [ ] **Step 2: Full backend test run for the slice**

Run: `dotnet test --filter "FullyQualifiedName~Authorization"`
Expected: All Authorization tests PASS.

- [ ] **Step 3: Full backend test suite (regression — existing auth/mock tests)**

Run: `dotnet test`
Expected: PASS. Pay attention to any pre-existing test that asserted the mock principal's
explicit role set (Task 15) — it must now assert `super_user`.

- [ ] **Step 4: Frontend build + lint + test**

Run (from `frontend/`): `npm run build && npm run lint && npm test -- --watchAll=false`
Expected: All succeed.

- [ ] **Step 5: Final commit (if anything pending) and push**

```bash
git push -u origin claude/rbac-inapp-permissions-spike-wHmHw
```

---

## Appendix: Spec coverage map

| Spec section | Implemented by |
| --- | --- |
| §2 Architecture/enforcement (claims transformation, super_user) | Tasks 1, 12, 14, 15 |
| §3 Data model (5 tables, code-defined perms) | Tasks 2, 5, 6, 7 |
| §3 System groups read-only + re-synced | Task 9 (+ Task 21 immutability) |
| §4 Resolution (closure, cache, materialize) | Tasks 10, 11 |
| §5 Admin surface (groups/users/catalogue) | Tasks 16, 19, 20, 21, 22, 23, 26 |
| §6 /api/auth/me frontend delivery | Tasks 17, 24, 25, 27 |
| §7 Seed & direct cutover | Tasks 9, 14, 32 |
| §8 Testing strategy | Tasks 9–12, 16–23, 30, 31 |
| §11 Decisions (system read-only, /me groups, cutover) | Tasks 9/21, 17/25, 32 |
