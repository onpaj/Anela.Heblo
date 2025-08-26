using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.Transport.Contracts;

public class ConfirmTransitRequest : IRequest<ConfirmTransitResponse>
{
    public int BoxId { get; set; }

    [Required]
    public string ConfirmationBoxNumber { get; set; } = null!;
}