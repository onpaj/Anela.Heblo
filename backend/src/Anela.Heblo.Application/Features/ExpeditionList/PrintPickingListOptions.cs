namespace Anela.Heblo.Application.Features.ExpeditionList;

public class PrintPickingListOptions
{
    public const string ConfigurationKey = "ExpeditionList";

    public string EmailSender { get; set; } = string.Empty;
    public string PrintQueueFolder { get; set; } = string.Empty;
    public List<string> DefaultEmailRecipients { get; set; } = new();
    public int SourceStateId { get; set; } = -2;
    public int FixSourceStateId { get; set; } = 73;
    public int DesiredStateId { get; set; } = 26;
    public bool SendToPrinterByDefault { get; set; } = false;
    public bool ChangeOrderStateByDefault { get; set; } = true;
    public string PrintSink { get; set; } = "FileSystem"; // "FileSystem" | "AzureBlob" | "Cups"
    public string BlobConnectionString { get; set; } = string.Empty;
    public string BlobContainerName { get; set; } = "expedition-lists";
}
