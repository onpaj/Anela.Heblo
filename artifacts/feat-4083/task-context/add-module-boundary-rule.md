### task: add-module-boundary-rule

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`

**Depends on:** add-authorization-directory-adapter, update-user-display-name-resolver

- [ ] **Step 1: Add the empty allowlist field**

In `ModuleBoundariesTests.cs`, immediately after the existing
`private static readonly HashSet<string> AuthorizationUserManagementAllowlist = ...;` block (around
line 356), add:

```csharp
    // Allowlist for Shared.Users -> Authorization. Empty — AuthorizationUserDirectorySourceAdapter
    // (Authorization-owned, in Features.Authorization.Infrastructure) is the sole implementer of
    // Shared.Users.Contracts.IUserDirectorySource; it lives outside this rule's inspected namespace
    // prefix, so nothing under Shared.Users itself may ever reference Authorization directly.
    private static readonly HashSet<string> SharedUsersAuthorizationAllowlist = new(StringComparer.Ordinal);
```

- [ ] **Step 2: Add the rule to `Rules()`**

In the `Rules()` method, immediately after the existing `"Authorization -> UserManagement"` entry (the
first entry in the `TheoryData<ModuleBoundaryRule>`), add:

```csharp
        new ModuleBoundaryRule(
            Name: "Shared.Users -> Authorization",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Shared.Users",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Authorization",
                "Anela.Heblo.Application.Features.Authorization",
                "Anela.Heblo.Persistence.Features.Authorization",
            },
            Allowlist: SharedUsersAuthorizationAllowlist),
```

- [ ] **Step 3: Run the full boundary test suite**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests"`
Expected: `Passed! - Failed: 0` across all theory cases, including the new `"Shared.Users -> Authorization"` case with zero violations. If it fails, the failure message lists the exact `Consumer -> Referenced` entries — resolve them by finishing `update-user-display-name-resolver` fully (do not add entries to `SharedUsersAuthorizationAllowlist` to make it pass; that defeats FR-6).

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "test(architecture): enforce Shared.Users -> Authorization module boundary"
```

---
