namespace Anela.Heblo.Adapters.Plaud;

internal static class PlaudTelemetryEventNames
{
    public const string NearExpiry = "PlaudTokenNearExpiry";
    public const string Expired = "PlaudTokenExpired";
    public const string Refreshed = "PlaudTokenRefreshed";
    public const string RefreshFailed = "PlaudTokenRefreshFailed";
    public const string KeyVaultWriteFailed = "PlaudTokenRefreshKeyVaultWriteFailed";
}
