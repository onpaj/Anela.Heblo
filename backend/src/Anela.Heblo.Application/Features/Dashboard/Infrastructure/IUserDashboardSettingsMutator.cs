using Anela.Heblo.Domain.Features.Dashboard;

namespace Anela.Heblo.Application.Features.Dashboard.Infrastructure;

/// <summary>
/// Encapsulates the shared scaffold for per-user UserDashboardSettings mutations:
/// userId normalization, pre-lock provisioning, lock acquisition, repository load,
/// LastModified stamping, and conditional persistence.
/// </summary>
/// <remarks>
/// <para>
/// <b>Provisioning order:</b> The mutator issues <c>GetUserSettingsRequest</c> via MediatR
/// BEFORE acquiring <see cref="IUserDashboardSettingsLock"/>. The underlying lock is
/// non-reentrant — invoking GetUserSettingsHandler from inside the lock would deadlock.
/// </para>
/// <para>
/// <b>Timestamping:</b> The mutator reads <c>TimeProvider.GetUtcNow()</c> exactly once per
/// invocation and reuses the value for both <c>UserDashboardTile.LastModified</c> (touched
/// or appended tile) and <c>UserDashboardSettings.LastModified</c>. Callers must NOT mutate
/// <c>LastModified</c> in the supplied delegates; the mutator owns those fields.
/// </para>
/// <para>
/// <b>Persistence:</b> <c>UpdateAsync</c> is called only when a tile was found or
/// appended. The "tile missing + <paramref name="onTileMissing"/> is null or returns null"
/// branch performs no write — preserving today's DisableTileHandler semantics.
/// </para>
/// </remarks>
internal interface IUserDashboardSettingsMutator
{
    /// <param name="userId">
    /// Caller-supplied user id. Null or empty is normalized to <c>"anonymous"</c> inside
    /// the mutator — handlers must not pre-normalize.
    /// </param>
    /// <param name="tileId">Identifier of the tile to mutate. Must be non-empty.</param>
    /// <param name="onTileFound">
    /// Invoked when a tile with <paramref name="tileId"/> already exists. Mutate domain
    /// fields (e.g. <c>IsVisible</c>) only; do NOT touch <c>LastModified</c>.
    /// </param>
    /// <param name="onTileMissing">
    /// Optional. When the tile is missing and this delegate is supplied, it is invoked
    /// with the loaded <see cref="UserDashboardSettings"/> and the resolved user id; it
    /// returns a new <see cref="UserDashboardTile"/> to append, or <c>null</c> to skip
    /// persistence. When this delegate is <c>null</c>, no write occurs and the result's
    /// <c>TileAppended</c> is <c>false</c>.
    /// </param>
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
