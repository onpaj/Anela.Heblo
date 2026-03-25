namespace Anela.Heblo.Adapters.Azure.Features.ExpeditionList;

public class AzureBlobPrintQueueOptions
{
    public const string ConfigurationKey = "ExpeditionListBlobStorage";

    public string ConnectionString { get; set; } = string.Empty;
    public string ContainerName { get; set; } = "expedition-lists";
}
