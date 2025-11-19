using System.Text.Json.Serialization;
using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Application.Features.Invoices.Contracts;

/// <summary>
/// Data transfer object for IssuedInvoice
/// </summary>
public class IssuedInvoiceDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("invoiceDate")]
    public DateTime InvoiceDate { get; set; }

    [JsonPropertyName("dueDate")]
    public DateTime DueDate { get; set; }

    [JsonPropertyName("taxDate")]
    public DateTime TaxDate { get; set; }

    [JsonPropertyName("varSymbol")]
    public long? VarSymbol { get; set; }

    [JsonPropertyName("billingMethod")]
    public BillingMethod BillingMethod { get; set; }

    [JsonPropertyName("shippingMethod")]
    public ShippingMethod ShippingMethod { get; set; }

    [JsonPropertyName("vatPayer")]
    public bool? VatPayer { get; set; }

    [JsonPropertyName("itemsCount")]
    public int ItemsCount { get; set; }

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("priceC")]
    public decimal PriceC { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("customerName")]
    public string? CustomerName { get; set; }

    [JsonPropertyName("isSynced")]
    public bool IsSynced { get; set; }

    [JsonPropertyName("lastSyncTime")]
    public DateTime? LastSyncTime { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("errorType")]
    public IssuedInvoiceErrorType? ErrorType { get; set; }

    [JsonPropertyName("syncHistoryCount")]
    public int SyncHistoryCount { get; set; }

    [JsonPropertyName("isCriticalError")]
    public bool IsCriticalError { get; set; }

    [JsonPropertyName("creationTime")]
    public DateTime CreationTime { get; set; }
}