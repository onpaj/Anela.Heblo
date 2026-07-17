using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.RemoveItemFromBox;

public class RemoveItemFromBoxRequest : IRequest<RemoveItemFromBoxResponse>
{
    public int BoxId { get; set; }

    [Required]
    public int ItemId { get; set; }

    /// <summary>
    /// Amount to remove from the grouped item. When null, the entire remaining amount is removed
    /// (removes the whole row). Backward compatible with callers that omit it.
    /// </summary>
    public double? Amount { get; set; }
}