using MediatR;
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.ConfirmSinglePhaseProduction;

public class ConfirmSinglePhaseProductionRequest : IRequest<ConfirmSinglePhaseProductionResponse>
{
    [Required]
    public int OrderId { get; set; }

    [Required]
    public Dictionary<int, decimal> ProductActualQuantities { get; set; } = new();

    [Required]
    public string UserId { get; set; } = null!;

    public string? ChangeReason { get; set; }
}