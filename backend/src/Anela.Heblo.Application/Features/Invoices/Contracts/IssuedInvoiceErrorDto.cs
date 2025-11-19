using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.Invoices.Contracts;

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