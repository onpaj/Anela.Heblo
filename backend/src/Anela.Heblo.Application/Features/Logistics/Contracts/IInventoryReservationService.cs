namespace Anela.Heblo.Application.Features.Logistics.Contracts;

/// <summary>
/// Logistics-owned abstraction over inventory reservation for transport-box operations.
/// Implemented by the Manufacture module via an adapter
/// (see <c>ManufactureInventoryReservationAdapter</c>) per the cross-module communication
/// pattern in <c>docs/architecture/development_guidelines.md</c>.
///
/// Implementations MUST NOT call SaveChangesAsync on any repository. The caller owns the
/// unit of work and commits the inventory mutation together with the transport-box mutation
/// against the shared ApplicationDbContext (ADR-001 Phase 1).
/// </summary>
public interface IInventoryReservationService
{
    /// <summary>
    /// Attempts to decrement inventory for a transport-box item. Returns a structured
    /// result distinguishing success, missing inventory record, and insufficient stock.
    /// Implementations must not throw Manufacture-owned exceptions across this boundary.
    /// </summary>
    Task<ConsumeInventoryResult> TryConsumeAsync(
        int inventoryId,
        decimal amount,
        string userName,
        DateTime timestamp,
        int boxId,
        string? boxCode,
        bool allowNegativeStock,
        CancellationToken cancellationToken);

    /// <summary>
    /// Restores inventory amount after a transport box transitions Opened → New.
    /// If the inventory id does not exist, the call is a no-op (implementations log a
    /// warning and return) — matching the original "log and skip" recovery semantics
    /// in <c>ChangeTransportBoxStateHandler</c>.
    /// </summary>
    Task RestoreAsync(
        int inventoryId,
        decimal amount,
        string userName,
        DateTime timestamp,
        int boxId,
        string? boxCode,
        CancellationToken cancellationToken);
}
