using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;

namespace Anela.Heblo.API.Services;

public interface ITelemetryService
{
    void TrackBusinessEvent(string eventName, Dictionary<string, string>? properties = null, Dictionary<string, double>? metrics = null);
    void TrackException(Exception exception, Dictionary<string, string>? properties = null);
    void TrackMetric(string metricName, double value, Dictionary<string, string>? properties = null);
    void TrackDependency(string dependencyName, string commandName, DateTimeOffset startTime, TimeSpan duration, bool success);
    
    // Business-specific tracking methods
    void TrackInvoiceImport(string invoiceId, bool success, string? error = null);
    void TrackPaymentImport(string paymentId, bool success, string? error = null);
    void TrackCatalogSync(int itemsProcessed, TimeSpan duration, bool success, string? error = null);
    void TrackOrderProcessing(string orderId, string status, Dictionary<string, string>? additionalProperties = null);
    void TrackInventoryUpdate(string productId, int oldQuantity, int newQuantity, string updateReason);
}

public class TelemetryService : ITelemetryService
{
    private readonly TelemetryClient _telemetryClient;
    private readonly ILogger<TelemetryService> _logger;

    public TelemetryService(TelemetryClient telemetryClient, ILogger<TelemetryService> logger)
    {
        _telemetryClient = telemetryClient;
        _logger = logger;
    }

    public void TrackBusinessEvent(string eventName, Dictionary<string, string>? properties = null, Dictionary<string, double>? metrics = null)
    {
        var telemetryEvent = new EventTelemetry(eventName);
        
        if (properties != null)
        {
            foreach (var prop in properties)
            {
                telemetryEvent.Properties[prop.Key] = prop.Value;
            }
        }
        
        if (metrics != null)
        {
            foreach (var metric in metrics)
            {
                telemetryEvent.Metrics[metric.Key] = metric.Value;
            }
        }
        
        _telemetryClient.TrackEvent(telemetryEvent);
        _logger.LogInformation("Business event tracked: {EventName}", eventName);
    }

    public void TrackException(Exception exception, Dictionary<string, string>? properties = null)
    {
        var telemetryException = new ExceptionTelemetry(exception);
        
        if (properties != null)
        {
            foreach (var prop in properties)
            {
                telemetryException.Properties[prop.Key] = prop.Value;
            }
        }
        
        _telemetryClient.TrackException(telemetryException);
        _logger.LogError(exception, "Exception tracked");
    }

    public void TrackMetric(string metricName, double value, Dictionary<string, string>? properties = null)
    {
        var telemetryMetric = new MetricTelemetry(metricName, value);
        
        if (properties != null)
        {
            foreach (var prop in properties)
            {
                telemetryMetric.Properties[prop.Key] = prop.Value;
            }
        }
        
        _telemetryClient.TrackMetric(telemetryMetric);
        _logger.LogDebug("Metric tracked: {MetricName} = {Value}", metricName, value);
    }

    public void TrackDependency(string dependencyName, string commandName, DateTimeOffset startTime, TimeSpan duration, bool success)
    {
        var dependency = new DependencyTelemetry(dependencyName, commandName, startTime, duration, success);
        _telemetryClient.TrackDependency(dependency);
        _logger.LogInformation("Dependency tracked: {DependencyName} - {CommandName} - Success: {Success}", 
            dependencyName, commandName, success);
    }

    // Business-specific implementations
    public void TrackInvoiceImport(string invoiceId, bool success, string? error = null)
    {
        var properties = new Dictionary<string, string>
        {
            ["InvoiceId"] = invoiceId,
            ["Success"] = success.ToString(),
            ["ImportType"] = "Invoice"
        };
        
        if (!string.IsNullOrEmpty(error))
        {
            properties["Error"] = error;
        }
        
        TrackBusinessEvent("InvoiceImport", properties, new Dictionary<string, double> 
        { 
            ["ImportDuration"] = 0 // Will be set by actual import process
        });
        
        if (!success)
        {
            _logger.LogError("Invoice import failed for {InvoiceId}: {Error}", invoiceId, error);
        }
    }

    public void TrackPaymentImport(string paymentId, bool success, string? error = null)
    {
        var properties = new Dictionary<string, string>
        {
            ["PaymentId"] = paymentId,
            ["Success"] = success.ToString(),
            ["ImportType"] = "Payment"
        };
        
        if (!string.IsNullOrEmpty(error))
        {
            properties["Error"] = error;
        }
        
        TrackBusinessEvent("PaymentImport", properties);
        
        if (!success)
        {
            _logger.LogError("Payment import failed for {PaymentId}: {Error}", paymentId, error);
        }
    }

    public void TrackCatalogSync(int itemsProcessed, TimeSpan duration, bool success, string? error = null)
    {
        var properties = new Dictionary<string, string>
        {
            ["Success"] = success.ToString(),
            ["SyncType"] = "Catalog"
        };
        
        if (!string.IsNullOrEmpty(error))
        {
            properties["Error"] = error;
        }
        
        var metrics = new Dictionary<string, double>
        {
            ["ItemsProcessed"] = itemsProcessed,
            ["DurationSeconds"] = duration.TotalSeconds
        };
        
        TrackBusinessEvent("CatalogSync", properties, metrics);
        TrackMetric("CatalogSyncDuration", duration.TotalSeconds);
        TrackMetric("CatalogItemsProcessed", itemsProcessed);
        
        if (!success)
        {
            _logger.LogError("Catalog sync failed after processing {ItemsProcessed} items: {Error}", 
                itemsProcessed, error);
        }
    }

    public void TrackOrderProcessing(string orderId, string status, Dictionary<string, string>? additionalProperties = null)
    {
        var properties = new Dictionary<string, string>
        {
            ["OrderId"] = orderId,
            ["Status"] = status,
            ["ProcessType"] = "Order"
        };
        
        if (additionalProperties != null)
        {
            foreach (var prop in additionalProperties)
            {
                properties[prop.Key] = prop.Value;
            }
        }
        
        TrackBusinessEvent("OrderProcessing", properties);
        _logger.LogInformation("Order {OrderId} processed with status: {Status}", orderId, status);
    }

    public void TrackInventoryUpdate(string productId, int oldQuantity, int newQuantity, string updateReason)
    {
        var properties = new Dictionary<string, string>
        {
            ["ProductId"] = productId,
            ["UpdateReason"] = updateReason,
            ["UpdateType"] = "Inventory"
        };
        
        var metrics = new Dictionary<string, double>
        {
            ["OldQuantity"] = oldQuantity,
            ["NewQuantity"] = newQuantity,
            ["QuantityChange"] = newQuantity - oldQuantity
        };
        
        TrackBusinessEvent("InventoryUpdate", properties, metrics);
        _logger.LogInformation("Inventory updated for {ProductId}: {OldQuantity} -> {NewQuantity} ({Reason})", 
            productId, oldQuantity, newQuantity, updateReason);
    }
}

/// <summary>
/// No-operation implementation of ITelemetryService for development environments 
/// where Application Insights is not configured
/// </summary>
public class NoOpTelemetryService : ITelemetryService
{
    private readonly ILogger<NoOpTelemetryService> _logger;

    public NoOpTelemetryService(ILogger<NoOpTelemetryService> logger)
    {
        _logger = logger;
    }

    public void TrackBusinessEvent(string eventName, Dictionary<string, string>? properties = null, Dictionary<string, double>? metrics = null)
    {
        _logger.LogDebug("NoOp: TrackBusinessEvent - {EventName}", eventName);
    }

    public void TrackException(Exception exception, Dictionary<string, string>? properties = null)
    {
        _logger.LogError(exception, "NoOp: TrackException");
    }

    public void TrackMetric(string metricName, double value, Dictionary<string, string>? properties = null)
    {
        _logger.LogDebug("NoOp: TrackMetric - {MetricName} = {Value}", metricName, value);
    }

    public void TrackDependency(string dependencyName, string commandName, DateTimeOffset startTime, TimeSpan duration, bool success)
    {
        _logger.LogDebug("NoOp: TrackDependency - {DependencyName} - {CommandName} - Success: {Success}", 
            dependencyName, commandName, success);
    }

    public void TrackInvoiceImport(string invoiceId, bool success, string? error = null)
    {
        _logger.LogDebug("NoOp: TrackInvoiceImport - {InvoiceId}, Success: {Success}", invoiceId, success);
        if (!success && !string.IsNullOrEmpty(error))
        {
            _logger.LogError("Invoice import failed for {InvoiceId}: {Error}", invoiceId, error);
        }
    }

    public void TrackPaymentImport(string paymentId, bool success, string? error = null)
    {
        _logger.LogDebug("NoOp: TrackPaymentImport - {PaymentId}, Success: {Success}", paymentId, success);
        if (!success && !string.IsNullOrEmpty(error))
        {
            _logger.LogError("Payment import failed for {PaymentId}: {Error}", paymentId, error);
        }
    }

    public void TrackCatalogSync(int itemsProcessed, TimeSpan duration, bool success, string? error = null)
    {
        _logger.LogDebug("NoOp: TrackCatalogSync - Items: {ItemsProcessed}, Duration: {Duration}, Success: {Success}", 
            itemsProcessed, duration, success);
        if (!success && !string.IsNullOrEmpty(error))
        {
            _logger.LogError("Catalog sync failed after processing {ItemsProcessed} items: {Error}", 
                itemsProcessed, error);
        }
    }

    public void TrackOrderProcessing(string orderId, string status, Dictionary<string, string>? additionalProperties = null)
    {
        _logger.LogDebug("NoOp: TrackOrderProcessing - {OrderId}, Status: {Status}", orderId, status);
    }

    public void TrackInventoryUpdate(string productId, int oldQuantity, int newQuantity, string updateReason)
    {
        _logger.LogDebug("NoOp: TrackInventoryUpdate - {ProductId}: {OldQuantity} -> {NewQuantity} ({Reason})", 
            productId, oldQuantity, newQuantity, updateReason);
    }
}