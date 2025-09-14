using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;

public class UpdateManufactureOrderResponse : BaseResponse
{
    public UpdateManufactureOrderDto? Order { get; set; }

    public UpdateManufactureOrderResponse() : base() { }

    public UpdateManufactureOrderResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}

public class UpdateManufactureOrderDto
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = null!;
    public DateTime CreatedDate { get; set; }
    public string CreatedByUser { get; set; } = null!;
    public string? ResponsiblePerson { get; set; }
    public DateOnly SemiProductPlannedDate { get; set; }
    public DateOnly ProductPlannedDate { get; set; }
    public string State { get; set; } = null!;
    public DateTime StateChangedAt { get; set; }
    public string StateChangedByUser { get; set; } = null!;
    public UpdateManufactureOrderSemiProductDto? SemiProduct { get; set; }
    public List<UpdateManufactureOrderProductDto> Products { get; set; } = new();
    public List<UpdateManufactureOrderNoteDto> Notes { get; set; } = new();
}

public class UpdateManufactureOrderSemiProductDto
{
    public int Id { get; set; }
    public string ProductCode { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public decimal PlannedQuantity { get; set; }
    public decimal ActualQuantity { get; set; }
    public string? LotNumber { get; set; } // Šarže
    public DateOnly? ExpirationDate { get; set; } // Expirace
}

public class UpdateManufactureOrderProductDto
{
    public int Id { get; set; }
    public string ProductCode { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public string SemiProductCode { get; set; } = null!;
    public decimal PlannedQuantity { get; set; }
    public decimal ActualQuantity { get; set; }
}

public class UpdateManufactureOrderNoteDto
{
    public int Id { get; set; }
    public string Text { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public string CreatedByUser { get; set; } = null!;
}