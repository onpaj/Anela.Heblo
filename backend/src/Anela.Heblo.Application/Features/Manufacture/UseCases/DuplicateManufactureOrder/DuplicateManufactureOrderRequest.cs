using MediatR;
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.DuplicateManufactureOrder;

public class DuplicateManufactureOrderRequest : IRequest<DuplicateManufactureOrderResponse>
{
    [Required]
    public int SourceOrderId { get; set; }
}