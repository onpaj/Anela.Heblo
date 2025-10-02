namespace Anela.Heblo.Domain.Features.Manufacture;

public interface IManufactureClient
{
    Task<string> SubmitManufactureAsync(SubmitManufactureClientRequest request, CancellationToken cancellationToken = default);
    
    Task<DiscardResidualSemiProductResponse> DiscardResidualSemiProductAsync(DiscardResidualSemiProductRequest request, CancellationToken cancellationToken = default);
}