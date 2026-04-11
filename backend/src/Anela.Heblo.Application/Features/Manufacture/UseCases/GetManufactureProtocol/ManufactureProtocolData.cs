using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureProtocol;

public class ManufactureProtocolData
{
    public string OrderNumber { get; set; } = null!;
    public DateTime CreatedDate { get; set; }
    public DateOnly PlannedDate { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ResponsiblePerson { get; set; }
    public ManufactureType ManufactureType { get; set; }

    public ManufactureProtocolSemiProduct? SemiProduct { get; set; }
    public List<ManufactureProtocolProduct> Products { get; set; } = new();

    public List<ManufactureProtocolErpDocument> ErpDocuments { get; set; } = new();
    public List<ManufactureProtocolNote> Notes { get; set; } = new();

    public DateTime GeneratedAt { get; set; }
}

public class ManufactureProtocolSemiProduct
{
    public string ProductCode { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public decimal PlannedQuantity { get; set; }
    public decimal? ActualQuantity { get; set; }
    public string? LotNumber { get; set; }
    public DateOnly? ExpirationDate { get; set; }
}

public class ManufactureProtocolProduct
{
    public string ProductCode { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public decimal PlannedQuantity { get; set; }
    public decimal? ActualQuantity { get; set; }
    public string? LotNumber { get; set; }
    public DateOnly? ExpirationDate { get; set; }
}

public class ManufactureProtocolErpDocument
{
    public string DocumentType { get; set; } = null!;
    public string DocumentCode { get; set; } = null!;
    public DateTime? DocumentDate { get; set; }
    public List<ManufactureErpDocumentItem> Items { get; set; } = new();
}

public class ManufactureProtocolNote
{
    public DateTime CreatedAt { get; set; }
    public string CreatedByUser { get; set; } = null!;
    public string Text { get; set; } = null!;
}
