namespace Anela.Heblo.Domain.Features.Logistics.Picking;

public class PrintPickingListRequest
{
    public const int DefaultSourceStateId = -2; // Vyrizuje se
    //private const string DesiredStateId = "26"; // Bali se
    public const int DefaultDesiredStateId = 26; // Bali se

    public IList<Carriers> Carriers { get; set; } = new List<Carriers>();
    public int SourceStateId { get; set; } = DefaultSourceStateId;
    public int DesiredStateId { get; set; } = DefaultDesiredStateId;
    public bool ChangeOrderState { get; set; }

    public static IList<Carriers> DefaultCarriers { get; set; } = new List<Carriers>()
    {
        Logistics.Carriers.Zasilkovna,
        Logistics.Carriers.GLS,
        Logistics.Carriers.PPL,
        Logistics.Carriers.Osobak
    };

    public bool SendToPrinter { get; set; }
}