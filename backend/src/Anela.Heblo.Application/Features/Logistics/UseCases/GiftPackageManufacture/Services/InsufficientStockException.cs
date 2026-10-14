namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services;

/// <summary>
/// A manufacture run was refused because the warehouse does not hold enough of at least one
/// ingredient. Distinct from a bare <see cref="InvalidOperationException"/> so the handler can map
/// it to a user-facing error without also swallowing EF Core's tracking and concurrency failures,
/// which use the same base type.
/// </summary>
public sealed class InsufficientStockException : InvalidOperationException
{
    public InsufficientStockException(string message) : base(message)
    {
    }
}
