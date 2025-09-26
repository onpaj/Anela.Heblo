using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public class ConfirmProductCompletionRequest
{
    [Required]
    public int Id { get; set; }

    [Required]
    public List<ProductActualQuantityRequest> Products { get; set; } = new();

    public string? ChangeReason { get; set; }
}