using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.Contracts;

public class ConfirmProductCompletionRequest : IRequest<ConfirmProductCompletionResponse>
{
    [Required]
    public int Id { get; set; }

    [Required]
    public List<ProductActualQuantityRequest> Products { get; set; } = new();

    public bool OverrideConfirmed { get; set; } = false;

    public string? ChangeReason { get; set; }
}