using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.Invoices.Contracts;

/// <summary>
/// DTO for sync data history entry
/// </summary>
public class IssuedInvoiceSyncDataDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("data")]
    public string? Data { get; set; }

    [JsonPropertyName("isSuccess")]
    public bool IsSuccess { get; set; }

    [JsonPropertyName("syncTime")]
    public DateTime SyncTime { get; set; }

    [JsonPropertyName("error")]
    public IssuedInvoiceErrorDto? Error { get; set; }

    public object ErrorMessage { get; set; }
    public object ErrorType { get; set; }
}