using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.RetryStockUpOperation;

public class RetryStockUpOperationRequest : IRequest<RetryStockUpOperationResponse>
{
    public int OperationId { get; set; }
}
