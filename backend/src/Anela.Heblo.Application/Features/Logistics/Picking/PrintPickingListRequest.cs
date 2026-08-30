using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
using Anela.Heblo.Domain.Features.Logistics;

namespace Anela.Heblo.Application.Features.Logistics.Picking;

public class PrintPickingListRequest
{
    public IList<Carriers> Carriers { get; set; } = new List<Carriers>();
    public int SourceStateId { get; set; } = ExpeditionPickingRequest.DefaultSourceStateId;
    public int DesiredStateId { get; set; } = ExpeditionPickingRequest.DefaultDesiredStateId;
    public int NoteStateId { get; set; } = ExpeditionPickingRequest.DefaultNoteStateId;
    public bool ChangeOrderState { get; set; }
    public bool SendToPrinter { get; set; }
    public string? OrderCode { get; set; }
}
