using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.Invoices.Contracts;

/// <summary>
/// Detailed data transfer object for IssuedInvoice including sync history
/// </summary>
public class IssuedInvoiceDetailDto : IssuedInvoiceDto
{
    [JsonPropertyName("syncHistory")]
    public List<IssuedInvoiceSyncDataDto> SyncHistory { get; set; } = new();

    [JsonPropertyName("concurrencyStamp")]
    public string? ConcurrencyStamp { get; set; }

    [JsonPropertyName("lastModificationTime")]
    public DateTime? LastModificationTime { get; set; }

    [JsonPropertyName("creatorId")]
    public Guid? CreatorId { get; set; }

    [JsonPropertyName("lastModifierId")]
    public Guid? LastModifierId { get; set; }
}

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
}

/// <summary>
/// DTO for sync error information
/// </summary>
public class IssuedInvoiceErrorDto
{
    [JsonPropertyName("errorType")]
    public string ErrorType { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}