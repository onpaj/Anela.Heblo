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
/// Sealed class with an outcome discriminator — extensible to carry an optional
/// available-amount field without breaking the contract.
/// </summary>
public sealed class ConsumeInventoryResult
{
    public ConsumeInventoryOutcome Outcome { get; init; }

    public static ConsumeInventoryResult Success() => new() { Outcome = ConsumeInventoryOutcome.Success };
    public static ConsumeInventoryResult InventoryNotFound() => new() { Outcome = ConsumeInventoryOutcome.InventoryNotFound };
    public static ConsumeInventoryResult InsufficientStock() => new() { Outcome = ConsumeInventoryOutcome.InsufficientStock };
}
