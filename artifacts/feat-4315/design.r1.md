# Design: Bulk mutation support in IUserDashboardSettingsMutator to eliminate SaveUserSettingsHandler's duplicated scaffold

## Component Design

### `IUserDashboardSettingsMutator` (interface, extended)
`backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/IUserDashboardSettingsMutator.cs`

Adds one new method alongside the existing `MutateAsync`:

```csharp
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
/// results in a persisted write (see remarks on unconditional persistence below).
/// </param>
/// <remarks>
/// Shares the exact same provision → lock → load → mutate → save scaffold as
/// <see cref="MutateAsync"/>, including the provisioning-before-lock invariant
/// documented on this interface. Unlike <see cref="MutateAsync"/>, which skips the
/// persistence call when nothing was found or appended, <c>MutateBulkAsync</c> always
/// calls <c>UpdateAsync</c> once <c>settings</c> is loaded (even for an empty
/// <paramref name="tiles"/> list), to preserve <c>SaveUserSettingsHandler</c>'s
/// existing "always persist on save" behavior.
/// </remarks>
Task<UserDashboardSettingsMutationResult> MutateBulkAsync(
    string? userId,
    IReadOnlyList<UserDashboardTileDto> tiles,
    CancellationToken cancellationToken);
```

Responsibility: same as the interface's existing responsibility, generalized from "one tile" to
"a batch of tiles," under one lock/provision/load cycle. `UserDashboardSettingsMutationResult` is
reused unchanged as the return type; `TileFound`/`TileAppended` mean "at least one tile in the
batch was found / appended" for this method.

### `UserDashboardSettingsMutator` (implementation, extended)
`backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/UserDashboardSettingsMutator.cs`

Implements `MutateBulkAsync` by reusing the same private scaffold steps `MutateAsync` already
performs (userId normalization, `_mediator.Send(new GetUserSettingsRequest(), ct)`, `_lock.AcquireAsync`,
`_repository.GetByUserIdAsync`), then diverges after `settings` is loaded:

```
resolvedUserId = userId is null/empty ? "anonymous" : userId
await mediator.Send(GetUserSettingsRequest)          // provisioning, outside lock
await using lockHandle = await lock.AcquireAsync(resolvedUserId)
settings = await repository.GetByUserIdAsync(resolvedUserId)
if settings is null:
    return (SettingsLoaded: false, TileFound: false, TileAppended: false)   // no write

now = timeProvider.GetUtcNow().DateTime                // one timestamp for the whole call
anyFound = false; anyAppended = false
for each tileDto in tiles:
    existing = settings.Tiles.FirstOrDefault(t => t.TileId == tileDto.TileId)
    if existing is not null:
        existing.IsVisible = tileDto.IsVisible
        existing.DisplayOrder = tileDto.DisplayOrder
        existing.LastModified = now
        anyFound = true
    else:
        settings.Tiles.Add(new UserDashboardTile {
            UserId = resolvedUserId, TileId = tileDto.TileId,
            IsVisible = tileDto.IsVisible, DisplayOrder = tileDto.DisplayOrder,
            LastModified = now, DashboardSettings = settings
        })
        anyAppended = true

settings.UserId = resolvedUserId
settings.LastModified = now
await repository.UpdateAsync(settings)                 // unconditional once settings loaded

return (SettingsLoaded: true, TileFound: anyFound, TileAppended: anyAppended)
```

No new fields, no new private helper classes — this is added as a sibling method on the existing
`internal sealed class UserDashboardSettingsMutator`, reusing its existing four injected
dependencies (`_repository`, `_lock`, `_timeProvider`, `_mediator`) unchanged.

### `SaveUserSettingsHandler` (rewritten, thinned)
`backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/SaveUserSettings/SaveUserSettingsHandler.cs`

```csharp
public class SaveUserSettingsHandler : IRequestHandler<SaveUserSettingsRequest, SaveUserSettingsResponse>
{
    private readonly IUserDashboardSettingsMutator _mutator;
    private readonly ICurrentUserService _currentUserService;

    public SaveUserSettingsHandler(
        IUserDashboardSettingsMutator mutator,
        ICurrentUserService currentUserService)
    {
        _mutator = mutator;
        _currentUserService = currentUserService;
    }

    public async Task<SaveUserSettingsResponse> Handle(SaveUserSettingsRequest request, CancellationToken cancellationToken)
    {
        var currentUser = _currentUserService.GetCurrentUser();
        var userId = currentUser.Id;

        await _mutator.MutateBulkAsync(
            userId,
            request.Tiles ?? Array.Empty<UserDashboardTileDto>(),
            cancellationToken);

        return new SaveUserSettingsResponse();
    }
}
```

Responsibility: resolve current user, delegate the entire upsert-and-persist operation to the
mutator, return a fixed success response — mirroring `EnableTileHandler`/`DisableTileHandler`'s
existing shape exactly. Anonymous-user normalization (`null`/empty → `"anonymous"`) is no longer
the handler's job; it moves into `MutateBulkAsync`, consistent with how `MutateAsync` already
owns that normalization for `EnableTile`/`DisableTile`.

### Test components (new/changed, no production behavior)

- `backend/test/Anela.Heblo.Tests/Features/Dashboard/SaveUserSettingsHandlerTests.cs` — rewritten
  to mock `IUserDashboardSettingsMutator` directly (matching the existing pattern in
  `EnableTileHandlerTests.cs`/`DisableTileHandlerTests.cs`), asserting `MutateBulkAsync` is
  called once with the resolved `userId` and the request's tiles (or an empty list when
  `request.Tiles` is null), and that the handler always returns a success response.
- `backend/test/Anela.Heblo.Tests/Features/Dashboard/Infrastructure/UserDashboardSettingsMutatorTests.cs`
  (new file) — covers `MutateBulkAsync`'s scaffold behavior directly: provisioning-before-lock
  ordering, lock-acquired-exactly-once, anonymous fallback, per-tile match-vs-append semantics,
  shared `LastModified` timestamp across all tiles in one call, unconditional `UpdateAsync` when
  settings is found (including for an empty `tiles` list), and no `UpdateAsync` when settings is
  `null`.

## Data Schemas

No schema changes. No new or modified:
- Database tables/columns (`UserDashboardSettings`, `UserDashboardTile` entities unchanged).
- API request/response shapes (`SaveUserSettingsRequest`, `SaveUserSettingsResponse` unchanged).
- Event payloads (none exist in this flow).

`UserDashboardTileDto` (existing, in `Dashboard/Contracts/`) is reused unchanged as the element
type of the new `MutateBulkAsync(..., IReadOnlyList<UserDashboardTileDto> tiles, ...)` parameter:

```csharp
public class UserDashboardTileDto
{
    public string TileId { get; set; } = string.Empty;
    public bool IsVisible { get; set; }
    public int DisplayOrder { get; set; }
}
```

`UserDashboardSettingsMutationResult` (existing, `internal readonly record struct` in
`IUserDashboardSettingsMutator.cs`) is reused unchanged as `MutateBulkAsync`'s return type:

```csharp
internal readonly record struct UserDashboardSettingsMutationResult(
    bool SettingsLoaded,
    bool TileFound,
    bool TileAppended);
```
