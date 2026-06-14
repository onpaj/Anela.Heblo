using Anela.Heblo.Domain.Features.Logistics;

namespace Anela.Heblo.Application.Features.ExpeditionList.Contracts;

public class ExpeditionPickingRequest
{
    public const int DefaultSourceStateId = -2;
    public const int DefaultDesiredStateId = 26;
    public const int DefaultNoteStateId = 35;

    public IList<Carriers> Carriers { get; set; } = new List<Carriers>();
    public int SourceStateId { get; set; } = DefaultSourceStateId;
    public int DesiredStateId { get; set; } = DefaultDesiredStateId;
    public int NoteStateId { get; set; } = DefaultNoteStateId;
    public bool ChangeOrderState { get; set; }
    public bool SendToPrinter { get; set; }

    public static IList<Carriers> DefaultCarriers { get; } = new List<Carriers>
    {
        Anela.Heblo.Domain.Features.Logistics.Carriers.Zasilkovna,
        Anela.Heblo.Domain.Features.Logistics.Carriers.GLS,
        Anela.Heblo.Domain.Features.Logistics.Carriers.PPL,
        Anela.Heblo.Domain.Features.Logistics.Carriers.Osobak,
    };
}
