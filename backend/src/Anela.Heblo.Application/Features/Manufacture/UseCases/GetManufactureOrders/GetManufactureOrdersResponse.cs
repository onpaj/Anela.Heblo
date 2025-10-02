using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrders;

public class GetManufactureOrdersResponse : BaseResponse
{
    public List<ManufactureOrderDto> Orders { get; set; } = new();
}

public class ManufactureOrderDto
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = null!;
    public string? ErpOrderNumberSemiproduct { get; set; }
    public DateTime? ErpOrderNumberSemiproductDate { get; set; }
    public string? ErpOrderNumberProduct { get; set; }
    public DateTime? ErpOrderNumberProductDate { get; set; }
    public string? ErpDiscardResidueDocumentNumber { get; set; }
    public DateTime? ErpDiscardResidueDocumentNumberDate { get; set; }
    public DateTime CreatedDate { get; set; }
    public string CreatedByUser { get; set; } = null!;
    public string? ResponsiblePerson { get; set; }

    public DateOnly SemiProductPlannedDate { get; set; }
    public DateOnly ProductPlannedDate { get; set; }

    public ManufactureOrderState State { get; set; }
    public DateTime StateChangedAt { get; set; }
    public bool ManualActionRequired { get; set; }
    public string StateChangedByUser { get; set; } = null!;

    public ManufactureOrderSemiProductDto? SemiProduct { get; set; }
    public List<ManufactureOrderProductDto> Products { get; set; } = new();
    public List<ManufactureOrderNoteDto> Notes { get; set; } = new();
    public List<ManufactureOrderAuditLogDto> AuditLog { get; set; } = new();
}

public class ManufactureOrderSemiProductDto
{
    public int Id { get; set; }
    public string ProductCode { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public decimal PlannedQuantity { get; set; }
    public decimal? ActualQuantity { get; set; }
    public decimal BatchMultiplier { get; set; } // Multiplikátor z batch calculatoru
    public string? LotNumber { get; set; } // Šarže
    public DateOnly? ExpirationDate { get; set; } // Expirace
    public int ExpirationMonths { get; set; } // Počet měsíců pro expirace
}

public class ManufactureOrderProductDto
{
    public int Id { get; set; }
    public string ProductCode { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public string SemiProductCode { get; set; } = null!;
    public decimal PlannedQuantity { get; set; }
    public decimal? ActualQuantity { get; set; }
}

public class ManufactureOrderNoteDto
{
    public int Id { get; set; }
    public string Text { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public string CreatedByUser { get; set; } = null!;
}

public class ManufactureOrderAuditLogDto
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string User { get; set; } = null!;
    public ManufactureOrderAuditAction Action { get; set; }
    public string Details { get; set; } = null!;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}