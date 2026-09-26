### task: add-mutate-bulk-async-interface

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/IUserDashboardSettingsMutator.cs`

- [ ] **Step 1: Add the `MutateBulkAsync` method signature with XML docs to the interface**

Open `backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/IUserDashboardSettingsMutator.cs`. It currently ends like this:

```csharp
    Task<UserDashboardSettingsMutationResult> MutateAsync(
        string? userId,
        string tileId,
        Action<UserDashboardSettings, UserDashboardTile> onTileFound,
        Func<UserDashboardSettings, string, UserDashboardTile?>? onTileMissing,
        CancellationToken cancellationToken);
}

internal readonly record struct UserDashboardSettingsMutationResult(
    bool SettingsLoaded,
    bool TileFound,
    bool TileAppended);
```

Add the new method directly after `MutateAsync`'s closing `);` and before the interface's closing `}`, so the file becomes:

```csharp
    Task<UserDashboardSettingsMutationResult> MutateAsync(
        string? userId,
        string tileId,
        Action<UserDashboardSettings, UserDashboardTile> onTileFound,
        Func<UserDashboardSettings, string, UserDashboardTile?>? onTileMissing,
        CancellationToken cancellationToken);

    /// <param name="userId">
    /// Caller-supplied user id. Null or empty is normalized to <c>"anonymous"</c> inside
    /// the mutator — handlers must not pre-normalize.
    /// </param>
    /// <param name="tiles">
    /// The full set of tile upserts to apply in this call. For each entry, an existing
    /// <see cref="UserDashboardTile"/> with matching <c>TileId</c> is updated in place
    /// (<c>IsVisible</c>, <c>DisplayOrder</c>); otherwise a new tile is appended. Tiles
    /// present in storage but absent from this list are left untouched — this is an
    /// upsert-by-id operation, not a full replace. An empty list is valid and still
    /// results in a persisted write (see remarks).
    /// </param>
    /// <remarks>
    /// Shares the exact same provision → lock → load → mutate → save scaffold as
    /// <see cref="MutateAsync"/>, including the provisioning-before-lock invariant
    /// documented on this interface. Unlike <see cref="MutateAsync"/>, which skips the
    /// persistence call when nothing was found or appended, <c>MutateBulkAsync</c> always
    /// calls <c>UpdateAsync</c> once <c>settings</c> is loaded (even for an empty
    /// <paramref name="tiles"/> list), to preserve <c>SaveUserSettingsHandler</c>'s
    /// existing "always persist on save" behavior. <c>TileFound</c>/<c>TileAppended</c> on
    /// the returned <see cref="UserDashboardSettingsMutationResult"/> mean "at least one
    /// tile in the batch was found / appended", not "the one tile".
    /// </remarks>
    Task<UserDashboardSettingsMutationResult> MutateBulkAsync(
        string? userId,
        IReadOnlyList<UserDashboardTileDto> tiles,
        CancellationToken cancellationToken);
}

internal readonly record struct UserDashboardSettingsMutationResult(
    bool SettingsLoaded,
    bool TileFound,
    bool TileAppended);
```

Note: `UserDashboardTileDto` lives in `Anela.Heblo.Application.Features.Dashboard.Contracts`. Add
`using Anela.Heblo.Application.Features.Dashboard.Contracts;` to the top of this file (it
currently only has `using Anela.Heblo.Domain.Features.Dashboard;`).

- [ ] **Step 2: Build to confirm the interface change compiles (implementation not yet updated, so build will fail — that's expected)**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: FAIL with `'UserDashboardSettingsMutator' does not implement interface member 'IUserDashboardSettingsMutator.MutateBulkAsync(...)'`. This confirms the interface change took effect; the next task implements it.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/IUserDashboardSettingsMutator.cs
git commit -m "feat(dashboard): add MutateBulkAsync to IUserDashboardSettingsMutator"
```

---
