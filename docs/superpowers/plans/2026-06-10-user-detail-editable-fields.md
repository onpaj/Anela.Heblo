# User Detail Editable Fields Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let an admin edit a user's Display name, Email, and Can-pack flag on the Access management → User detail page, committed alongside group changes via the existing Save button.

**Architecture:** Add one new MediatR use case `UpdateUser` (PUT `users/{id}`) handling scalar profile fields, following the existing `SetUserActive` pattern exactly. Group assignment stays on its own dedicated endpoint; the page's **Save** fires both mutations in parallel. Applies to both Entra (SSO) and Local users — auth is keyed on `EntraObjectId`, so editing DB display name/email is a safe independent override.

**Tech Stack:** .NET 8, MediatR, FluentValidation, EF Core (in-memory for tests), xUnit + FluentAssertions + Moq; React + TanStack Query, NSwag-generated TypeScript client.

---

## File Structure

**Backend (new — `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/UpdateUser/`):**
- `UpdateUserRequest.cs` — MediatR request, OpenAPI contract (plain class, **not a record**)
- `UpdateUserResponse.cs` — `: BaseResponse` (MUST inherit BaseResponse or the CI reflection contract test fails)
- `UpdateUserHandler.cs` — load user, set fields, save, invalidate cache
- `UpdateUserValidator.cs` — DisplayName required; Email optional but valid when present

**Backend (modify):**
- `backend/src/Anela.Heblo.Application/Features/Authorization/AuthorizationModule.cs` — register validator + ValidationBehavior (manual, per project rule)
- `backend/src/Anela.Heblo.API/Controllers/AuthorizationController.cs` — new `PUT users/{id}` endpoint

**Backend tests (new):**
- `backend/test/Anela.Heblo.Tests/Authorization/UpdateUserHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Authorization/UpdateUserValidatorTests.cs`

**Frontend (modify):**
- `frontend/src/api/generated/api-client.ts` — regenerated (do not hand-edit)
- `frontend/src/api/hooks/useAccessManagement.ts` — add `useUpdateUser`
- `frontend/src/pages/UserDetailPage.tsx` — editable form + combined save

---

## Task 1: Backend UpdateUser request, response, and handler

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/UpdateUser/UpdateUserRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/UpdateUser/UpdateUserResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/UpdateUser/UpdateUserHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Authorization/UpdateUserHandlerTests.cs`

- [ ] **Step 1: Write the failing handler test**

Create `backend/test/Anela.Heblo.Tests/Authorization/UpdateUserHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Authorization.UseCases.UpdateUser;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Authorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class UpdateUserHandlerTests
{
    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"updateuser_{Guid.NewGuid()}").Options);

    private static IPermissionResolver NoOpResolver() => new Mock<IPermissionResolver>().Object;

    private static async Task<AppUser> SeedUser(ApplicationDbContext db)
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            EntraObjectId = "oid",
            Email = "old@x.cz",
            DisplayName = "Old",
            IsActive = true,
            CanPack = false,
            Source = AppUserSource.Entra,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task Handle_UpdatesFields_WhenUserExists()
    {
        await using var db = NewDb();
        var user = await SeedUser(db);
        var handler = new UpdateUserHandler(new AuthorizationRepository(db), NoOpResolver());

        var result = await handler.Handle(new UpdateUserRequest
        {
            UserId = user.Id,
            DisplayName = "  New Name  ",
            Email = "  new@x.cz  ",
            CanPack = true,
        }, default);

        result.Success.Should().BeTrue();
        var saved = await db.AppUsers.SingleAsync(u => u.Id == user.Id);
        saved.DisplayName.Should().Be("New Name");   // trimmed
        saved.Email.Should().Be("new@x.cz");          // trimmed
        saved.CanPack.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_AllowsEmptyEmail()
    {
        await using var db = NewDb();
        var user = await SeedUser(db);
        var handler = new UpdateUserHandler(new AuthorizationRepository(db), NoOpResolver());

        var result = await handler.Handle(new UpdateUserRequest
        {
            UserId = user.Id,
            DisplayName = "Name",
            Email = null,
            CanPack = false,
        }, default);

        result.Success.Should().BeTrue();
        (await db.AppUsers.SingleAsync(u => u.Id == user.Id)).Email.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenUserMissing()
    {
        await using var db = NewDb();
        var handler = new UpdateUserHandler(new AuthorizationRepository(db), NoOpResolver());

        var result = await handler.Handle(new UpdateUserRequest
        {
            UserId = Guid.NewGuid(),
            DisplayName = "Name",
        }, default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.AuthorizationUserNotFound);
    }

    [Fact]
    public async Task Handle_InvalidatesCache_WhenEntraUser()
    {
        await using var db = NewDb();
        var user = await SeedUser(db);
        var resolverMock = new Mock<IPermissionResolver>();
        var handler = new UpdateUserHandler(new AuthorizationRepository(db), resolverMock.Object);

        await handler.Handle(new UpdateUserRequest { UserId = user.Id, DisplayName = "Name", CanPack = true }, default);

        resolverMock.Verify(r => r.InvalidateCache(user.EntraObjectId!), Times.Once);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails (does not compile)**

Run: `dotnet build backend/test/Anela.Heblo.Tests`
Expected: FAIL — `UpdateUserRequest`, `UpdateUserResponse`, `UpdateUserHandler` do not exist.

- [ ] **Step 3: Create `UpdateUserRequest.cs`**

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.UpdateUser;

public class UpdateUserRequest : IRequest<UpdateUserResponse>
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool CanPack { get; set; }
}
```

- [ ] **Step 4: Create `UpdateUserResponse.cs`**

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.UpdateUser;

public class UpdateUserResponse : BaseResponse
{
    public UpdateUserResponse() { }
    public UpdateUserResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

- [ ] **Step 5: Create `UpdateUserHandler.cs`**

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.UpdateUser;

public class UpdateUserHandler : IRequestHandler<UpdateUserRequest, UpdateUserResponse>
{
    private readonly IAuthorizationRepository _repo;
    private readonly IPermissionResolver _resolver;

    public UpdateUserHandler(IAuthorizationRepository repo, IPermissionResolver resolver)
    {
        _repo = repo;
        _resolver = resolver;
    }

    public async Task<UpdateUserResponse> Handle(UpdateUserRequest request, CancellationToken ct)
    {
        var user = await _repo.GetUserByIdAsync(request.UserId, ct);
        if (user is null)
            return new UpdateUserResponse(ErrorCodes.AuthorizationUserNotFound);

        user.DisplayName = request.DisplayName.Trim();
        user.Email = request.Email?.Trim() ?? string.Empty;
        user.CanPack = request.CanPack;
        await _repo.SaveChangesAsync(ct);

        if (user.EntraObjectId is not null)
            _resolver.InvalidateCache(user.EntraObjectId);

        return new UpdateUserResponse();
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~UpdateUserHandlerTests"`
Expected: PASS (4 tests).

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/UpdateUser backend/test/Anela.Heblo.Tests/Authorization/UpdateUserHandlerTests.cs
git commit -m "feat(authz): add UpdateUser handler for editable user profile fields"
```

---

## Task 2: Backend UpdateUser validator

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/UpdateUser/UpdateUserValidator.cs`
- Test: `backend/test/Anela.Heblo.Tests/Authorization/UpdateUserValidatorTests.cs`

- [ ] **Step 1: Write the failing validator test**

Create `backend/test/Anela.Heblo.Tests/Authorization/UpdateUserValidatorTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Authorization.UseCases.UpdateUser;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class UpdateUserValidatorTests
{
    private readonly UpdateUserValidator _validator = new();

    [Fact]
    public void Rejects_BlankDisplayName()
    {
        var result = _validator.Validate(new UpdateUserRequest { DisplayName = "", Email = "a@b.cz" });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_InvalidEmail()
    {
        var result = _validator.Validate(new UpdateUserRequest { DisplayName = "Name", Email = "not-an-email" });
        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Accepts_EmptyEmail(string? email)
    {
        var result = _validator.Validate(new UpdateUserRequest { DisplayName = "Name", Email = email });
        result.IsValid.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails (does not compile)**

Run: `dotnet build backend/test/Anela.Heblo.Tests`
Expected: FAIL — `UpdateUserValidator` does not exist.

- [ ] **Step 3: Create `UpdateUserValidator.cs`**

```csharp
using FluentValidation;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.UpdateUser;

public class UpdateUserValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserValidator()
    {
        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Display name is required.")
            .MaximumLength(255);

        RuleFor(x => x.Email)
            .MaximumLength(255)
            .EmailAddress().WithMessage("Email is not valid.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~UpdateUserValidatorTests"`
Expected: PASS (5 cases).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/UpdateUser/UpdateUserValidator.cs backend/test/Anela.Heblo.Tests/Authorization/UpdateUserValidatorTests.cs
git commit -m "feat(authz): add UpdateUser validator"
```

---

## Task 3: Register DI + expose controller endpoint

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Authorization/AuthorizationModule.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/AuthorizationController.cs`

- [ ] **Step 1: Register the validator and ValidationBehavior**

In `AuthorizationModule.cs`, add the `using` at the top (alongside the existing UseCases usings):

```csharp
using Anela.Heblo.Application.Features.Authorization.UseCases.UpdateUser;
```

Then add these two lines inside `AddAuthorizationModule`, right before `return services;`:

```csharp
        services.AddScoped<IValidator<UpdateUserRequest>, UpdateUserValidator>();
        services.AddTransient<IPipelineBehavior<UpdateUserRequest, UpdateUserResponse>,
            ValidationBehavior<UpdateUserRequest, UpdateUserResponse>>();
```

(MediatR auto-registers the handler via assembly scan — no handler registration needed.)

- [ ] **Step 2: Add the controller endpoint**

In `AuthorizationController.cs`, add the `using` at the top (alphabetically with the other UseCases usings):

```csharp
using Anela.Heblo.Application.Features.Authorization.UseCases.UpdateUser;
```

Then add this action after the `SetActive` method (line ~93), before `CreateLocalUser`:

```csharp
    [HttpPut("users/{id:guid}")]
    [FeatureAuthorize(Feature.Admin_Administration, AccessLevel.Write)]
    public async Task<ActionResult<UpdateUserResponse>> UpdateUser([FromRoute] Guid id, [FromBody] UpdateUserRequest request, CancellationToken ct)
    {
        request.UserId = id;
        return HandleResponse(await _mediator.Send(request, ct));
    }
```

- [ ] **Step 3: Build the backend and run the full Authorization test suite**

Run: `dotnet build backend/src/Anela.Heblo.API && dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~Authorization"`
Expected: PASS — including the `*Response : BaseResponse` reflection contract test and `AuthorizationModuleTests`.

- [ ] **Step 4: Format**

Run: `dotnet format backend/Anela.Heblo.sln`
Expected: no remaining diagnostics.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Authorization/AuthorizationModule.cs backend/src/Anela.Heblo.API/Controllers/AuthorizationController.cs
git commit -m "feat(authz): expose PUT users/{id} UpdateUser endpoint"
```

---

## Task 4: Regenerate TS client + add `useUpdateUser` hook

**Files:**
- Modify (generated): `frontend/src/api/generated/api-client.ts`
- Modify: `frontend/src/api/hooks/useAccessManagement.ts`

- [ ] **Step 1: Regenerate the TypeScript client**

Run: `cd frontend && npm run generate-client`
Expected: `frontend/src/api/generated/api-client.ts` now contains `UpdateUserRequest`, `UpdateUserResponse`, and an `authorization_UpdateUser(id, request)` method.

- [ ] **Step 2: Verify the generated symbols exist**

Run: `grep -n "authorization_UpdateUser\|class UpdateUserRequest\|class UpdateUserResponse" frontend/src/api/generated/api-client.ts`
Expected: at least 3 matches.

- [ ] **Step 3: Add the hook**

In `frontend/src/api/hooks/useAccessManagement.ts`:

Add `UpdateUserResponse` to the `import type { ... }` block (the type-only import list ending at line ~18):

```typescript
  UpdateUserResponse,
```

Add `UpdateUserRequest` to the value `import { ... }` block (lines ~19–27):

```typescript
  UpdateUserRequest,
```

Add this hook after `useSetUserActive` (after line ~177):

```typescript
export const useUpdateUser = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({
      id,
      request,
    }: {
      id: string;
      request: UpdateUserRequest;
    }): Promise<UpdateUserResponse> => {
      const client = getAuthenticatedApiClient();
      return client.authorization_UpdateUser(id, request);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: keys.users });
      queryClient.invalidateQueries({ queryKey: keys.userPermissionsPrefix, exact: false });
    },
  });
};
```

Add `UpdateUserRequest` to the re-export `export type { ... }` line at the bottom (line ~236):

```typescript
export type { CreateGroupRequest, UpdateGroupRequest, AssignUserGroupsRequest, SetUserActiveRequest, AddGroupMemberRequest, CreateLocalUserRequest, SetUserCanPackRequest, UpdateUserRequest };
```

- [ ] **Step 4: Type-check**

Run: `cd frontend && npx tsc --noEmit`
Expected: no errors.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/api/generated/api-client.ts frontend/src/api/hooks/useAccessManagement.ts
git commit -m "feat(authz): regenerate client and add useUpdateUser hook"
```

---

## Task 5: Editable form + combined save on UserDetailPage

**Files:**
- Modify: `frontend/src/pages/UserDetailPage.tsx`

- [ ] **Step 1: Update imports**

Change the hooks import (lines 3–8) to add `useUpdateUser`:

```typescript
import {
  useUsers,
  useAssignUserGroups,
  useSetUserActive,
  useUserPermissions,
  useUpdateUser,
} from "../api/hooks/useAccessManagement";
```

Change the api-client import (line 9) to add `UpdateUserRequest`:

```typescript
import { AssignUserGroupsRequest, SetUserActiveRequest, UpdateUserRequest } from "../api/generated/api-client";
```

- [ ] **Step 2: Extend the draft shape and seed all fields**

Replace the `UserDraft` interface (lines 13–15):

```typescript
interface UserDraft {
  displayName: string;
  email: string;
  canPack: boolean;
  groupIds: string[];
}
```

Add the hook with the other mutation hooks (after line 25):

```typescript
  const updateUser = useUpdateUser();
```

Replace the draft initialization inside the init `useEffect` (line 36):

```typescript
    const d: UserDraft = {
      displayName: user.displayName ?? "",
      email: user.email ?? "",
      canPack: user.canPack ?? false,
      groupIds: user.groupIds ?? [],
    };
```

- [ ] **Step 3: Replace `onSave` with the combined, validated save**

Replace the `onSave` function (lines 44–55):

```typescript
  const onSave = async () => {
    if (!draft) return;
    if (!draft.displayName.trim()) {
      toast.showError("Save failed", "Display name is required");
      return;
    }
    try {
      await Promise.all([
        updateUser.mutateAsync({
          id,
          request: new UpdateUserRequest({
            userId: id,
            displayName: draft.displayName.trim(),
            email: draft.email.trim(),
            canPack: draft.canPack,
          }),
        }),
        assignUserGroups.mutateAsync({
          id,
          request: new AssignUserGroupsRequest({ userId: id, groupIds: draft.groupIds }),
        }),
      ]);
      toast.showSuccess("Saved", "User updated successfully");
    } catch {
      toast.showError("Save failed", "An error occurred while saving changes");
    }
  };
```

- [ ] **Step 4: Include the new mutation in the saving state**

Replace the `isSaving` line (line 74):

```typescript
  const isSaving = assignUserGroups.isPending || updateUser.isPending;
```

- [ ] **Step 5: Replace the read-only header card with editable inputs**

Replace the card block (lines 107–121, the `<div className="bg-white border ... justify-between">` … `</div>` showing email, last login, and the Disable/Enable button) with:

```tsx
      <div className="bg-white border border-gray-200 rounded-lg p-4 space-y-4">
        <div className="flex items-center justify-between">
          <p className="text-sm text-gray-500">Last login: {lastLoginText}</p>
          <button
            type="button"
            onClick={onToggleActive}
            disabled={setActive.isPending}
            className={`text-sm ${user.isActive ? "text-red-600" : "text-green-600"} hover:underline disabled:opacity-50`}
            aria-label={user.isActive ? "Disable user" : "Enable user"}
          >
            {user.isActive ? "Disable" : "Enable"}
          </button>
        </div>

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
          <label className="block">
            <span className="block text-sm font-medium text-gray-700 mb-1">Display name</span>
            <input
              type="text"
              value={draft.displayName}
              onChange={(e) => updateDraft({ displayName: e.target.value })}
              className="w-full rounded border border-gray-300 px-3 py-2 text-sm"
            />
          </label>
          <label className="block">
            <span className="block text-sm font-medium text-gray-700 mb-1">Email</span>
            <input
              type="email"
              value={draft.email}
              onChange={(e) => updateDraft({ email: e.target.value })}
              className="w-full rounded border border-gray-300 px-3 py-2 text-sm"
            />
          </label>
        </div>

        <label className="flex items-center gap-2">
          <input
            type="checkbox"
            checked={draft.canPack}
            onChange={(e) => updateDraft({ canPack: e.target.checked })}
            className="rounded border-gray-300"
          />
          <span className="text-sm text-gray-700">Can pack</span>
        </label>
      </div>
```

(The `<h1>{user.displayName}</h1>` page title at line 104 stays bound to the saved value — it refreshes after Save via the `users` query invalidation.)

- [ ] **Step 6: Build the frontend (strict) and lint**

Run: `cd frontend && npm run build && npm run lint`
Expected: build succeeds (catches stricter type errors than `tsc`), lint clean.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/pages/UserDetailPage.tsx
git commit -m "feat(authz): edit display name, email, and can-pack on user detail page"
```

---

## Manual Verification

1. **Backend:** `dotnet build backend/Anela.Heblo.sln` + `dotnet format backend/Anela.Heblo.sln` + `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~Authorization"` — all green.
2. **Frontend:** `cd frontend && npm run build && npm run lint` — clean.
3. **End-to-end (run the app):**
   - Open **Access management → Users**, click a user to open the detail page.
   - Edit **Display name**, change **Email**, toggle **Can pack**, click **Save** → success toast.
   - Navigate back to the list and confirm the new display name/email persist; reopen detail to confirm Can-pack persisted.
   - Repeat for **one Entra (SSO) user and one Local operator** to confirm both sources are editable.
   - Clear Email and Save → succeeds (empty email allowed). Enter an invalid email (e.g. `abc`) and Save → backend rejects via validation (save fails toast). Clear Display name and Save → client blocks with "Display name is required".

## Self-Review Notes

- **Spec coverage:** Display name (Task 5 input + Task 1 handler), Email (same), Can pack (same), both user sources (no source gating anywhere — Task 1 handler treats all users uniformly; cache invalidation guarded only by `EntraObjectId != null`). ✔
- **Type consistency:** `UpdateUserRequest { userId, displayName, email, canPack }` is identical across Task 1 (C# request), Task 4 (hook generic), and Task 5 (constructor call). Hook method `authorization_UpdateUser(id, request)` matches the controller route `PUT users/{id}`. ✔
- **No placeholders:** every code/step is complete and runnable. ✔
