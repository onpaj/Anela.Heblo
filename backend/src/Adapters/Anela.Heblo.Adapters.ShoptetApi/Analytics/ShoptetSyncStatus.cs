namespace Anela.Heblo.Adapters.ShoptetApi.Analytics;

/// <summary>
/// The values written to <c>shoptet_raw.sync_state.last_run_status</c>. They are the monitoring
/// surface this feature exposes through <c>v_sync_health</c>, so they are named in one place rather
/// than spelled out at each of the sites that set and compare them.
/// </summary>
internal static class ShoptetSyncStatus
{
    public const string Running = "RUNNING";
    public const string Ok = "OK";
    public const string Failed = "FAILED";

    /// <summary>Set when a run is cut short by the job timeout or an operator's Ctrl+C.</summary>
    public const string Cancelled = "CANCELLED";

    /// <summary>Matches the last_error_message column width.</summary>
    private const int MaxErrorLength = 2000;

    public static string TruncateError(string message) =>
        message.Length > MaxErrorLength ? message[..MaxErrorLength] : message;
}
