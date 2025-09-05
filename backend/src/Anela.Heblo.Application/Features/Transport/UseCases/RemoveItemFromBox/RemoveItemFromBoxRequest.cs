using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.Transport.UseCases;

public class RemoveItemFromBoxRequest : IRequest<RemoveItemFromBoxResponse>
{
    public int BoxId { get; set; }

    [Required]
    public int ItemId { get; set; }
}