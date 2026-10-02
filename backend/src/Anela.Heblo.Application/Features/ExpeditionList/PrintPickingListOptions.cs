namespace Anela.Heblo.Application.Features.ExpeditionList;

/// <summary>
/// Application-layer configuration for expedition-list picking, printing, and order-state
/// transitions. This class intentionally does NOT hold Azure Blob print-sink connection details
/// or print-sink selection — see
/// <see cref="Anela.Heblo.Adapters.Azure.Features.ExpeditionList.AzureBlobPrintSinkOptions"/>
/// (Blob connection/container) and <c>ServiceCollectionExtensions.AddPrintQueueSink</c>, which reads
/// <c>configuration["ExpeditionList:PrintSink"]</c> directly to select the print-sink adapter.
/// Both still read from this same <c>"ExpeditionList"</c> configuration section (see
/// <see cref="ConfigurationKey"/>) — only the C# property ownership is split by layer.
/// </summary>
public class PrintPickingListOptions
{
    public const string ConfigurationKey = "ExpeditionList";

    public string EmailSender { get; set; } = string.Empty;
    public string PrintQueueFolder { get; set; } = string.Empty;
    public List<string> DefaultEmailRecipients { get; set; } = new();
    public int SourceStateId { get; set; } = -2;
    public int FixSourceStateId { get; set; } = 73;
    public int DesiredStateId { get; set; } = 26;
    public string DesiredStateName { get; set; } = "Balí se";
    public int NoteStateId { get; set; } = 35;
    public bool SendToPrinterByDefault { get; set; } = false;
    public bool ChangeOrderStateByDefault { get; set; } = true;
}
