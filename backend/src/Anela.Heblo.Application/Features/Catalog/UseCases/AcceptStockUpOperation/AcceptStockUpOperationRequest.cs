using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.AcceptStockUpOperation;

public class AcceptStockUpOperationRequest : IRequest<AcceptStockUpOperationResponse>
{
    [Required]
    public int OperationId { get; set; }
}
