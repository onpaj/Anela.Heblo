namespace Anela.Heblo.Application.Features.Logistics.Contracts;

/// <summary>
/// Outcome of an <see cref="IInventoryReservationService.TryConsumeAsync"/> call.
/// </summary>
public enum ConsumeInventoryOutcome
{
    Success,
    InventoryNotFound,
    InsufficientStock,
}

/// <summary>
/// Logistics-owned result of attempting to consume inventory.
/// Sealed record with an outcome discriminator — extensible to carry an optional
/// available-amount field without breaking the contract.
/// </summary>
public sealed record ConsumeInventoryResult(ConsumeInventoryOutcome Outcome);
