using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Catalog.Inventory;

public class Lot : IEntity<int>
{
    protected Lot() { } // EF Core

    public Lot(string materialCode, string lotCode, DateOnly? expiration, DateOnly receivedDate, string? notes, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(materialCode)) throw new ArgumentException("MaterialCode is required.", nameof(materialCode));
        if (string.IsNullOrWhiteSpace(lotCode)) throw new ArgumentException("LotCode is required.", nameof(lotCode));
        if (string.IsNullOrWhiteSpace(createdBy)) throw new ArgumentException("CreatedBy is required.", nameof(createdBy));

        MaterialCode = materialCode;
        LotCode = lotCode;
        Expiration = expiration;
        ReceivedDate = receivedDate;
        Notes = notes;
        CreatedAt = DateTime.UtcNow;
        CreatedBy = createdBy;
    }

    public int Id { get; private set; }
    public string MaterialCode { get; private set; } = null!;
    public string LotCode { get; private set; } = null!;
    public DateOnly? Expiration { get; private set; }
    public DateOnly ReceivedDate { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTime? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public void Update(DateOnly? expiration, DateOnly receivedDate, string? notes, string updatedBy)
    {
        if (string.IsNullOrWhiteSpace(updatedBy)) throw new ArgumentException("UpdatedBy is required.", nameof(updatedBy));

        Expiration = expiration;
        ReceivedDate = receivedDate;
        Notes = notes;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }
}
