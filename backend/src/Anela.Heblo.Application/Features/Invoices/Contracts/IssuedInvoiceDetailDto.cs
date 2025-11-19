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