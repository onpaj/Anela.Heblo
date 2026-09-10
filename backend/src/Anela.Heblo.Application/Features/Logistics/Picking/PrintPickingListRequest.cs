using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
using Anela.Heblo.Domain.Features.Logistics;

namespace Anela.Heblo.Application.Features.Logistics.Picking;

public class PrintPickingListRequest
{
    public const int DefaultSourceStateId = ExpeditionPickingRequest.DefaultSourceStateId;
    public const int DefaultDesiredStateId = ExpeditionPickingRequest.DefaultDesiredStateId;
    public const int DefaultNoteStateId = ExpeditionPickingRequest.DefaultNoteStateId;

    public IList<Carriers> Carriers { get; set; } = new List<Carriers>();
    public int SourceStateId { get; set; } = DefaultSourceStateId;
    public int DesiredStateId { get; set; } = DefaultDesiredStateId;
    public int NoteStateId { get; set; } = DefaultNoteStateId;
    public bool ChangeOrderState { get; set; }
    public bool SendToPrinter { get; set; }
    public string? OrderCode { get; set; }
}
