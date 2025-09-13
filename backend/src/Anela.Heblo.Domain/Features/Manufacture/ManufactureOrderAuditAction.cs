namespace Anela.Heblo.Domain.Features.Manufacture;

public enum ManufactureOrderAuditAction
{
    StateChanged,
    QuantityChanged, 
    DateChanged,
    ResponsiblePersonAssigned,
    NoteAdded,
    OrderCreated
}