namespace Anela.Heblo.Persistence.Telemetry;

/// <summary>
/// Service for tracking business events and metrics for observability
/// </summary>
public interface ITelemetryService
{
    // Core telemetry operations
    void TrackBusinessEvent(string eventName, Dictionary<string, string>? properties = null, Dictionary<string, double>? metrics = null);
    void TrackException(Exception exception, Dictionary<string, string>? properties = null);
    void TrackMetric(string metricName, double value, Dictionary<string, string>? properties = null);
    void TrackDependency(string dependencyName, string commandName, DateTimeOffset startTime, TimeSpan duration, bool success);

    // Business-specific operations
    void TrackInvoiceImport(string invoiceId, bool success, string? error = null);
    void TrackPaymentImport(string paymentId, bool success, string? error = null);
    void TrackCatalogSync(int itemsProcessed, TimeSpan duration, bool success, string? error = null);
    void TrackOrderProcessing(string orderId, string status, Dictionary<string, string>? additionalProperties = null);
    void TrackInventoryUpdate(string productId, int oldQuantity, int newQuantity, string updateReason);
}