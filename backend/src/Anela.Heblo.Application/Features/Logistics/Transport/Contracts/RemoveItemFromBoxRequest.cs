using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.Transport.Contracts;

public class RemoveItemFromBoxRequest : IRequest<RemoveItemFromBoxResponse>
{
    public int BoxId { get; set; }

    [Required]
    public int ItemId { get; set; }
}