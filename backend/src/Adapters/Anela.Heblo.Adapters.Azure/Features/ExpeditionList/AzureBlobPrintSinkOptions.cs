namespace Anela.Heblo.Adapters.Azure.Features.ExpeditionList;

/// <summary>
/// Configuration for the Azure Blob print-queue sink adapter (<see cref="AzureBlobPrintQueueSink"/>).
/// </summary>
/// <remarks>
/// Bound from the same <c>"ExpeditionList"</c> configuration section that
/// <see cref="Anela.Heblo.Application.Features.ExpeditionList.PrintPickingListOptions"/> binds from
/// (see <c>AzureAdapterModule.AddAzurePrintQueueSinkInfrastructure</c>) — not a separate section.
/// This is a deliberate choice (arch-review for issue #4335, Decision 2): it keeps this adapter's
/// configuration keys and the production Key Vault secret (<c>ExpeditionList--BlobConnectionString</c>)
/// unchanged, while still moving the C# property ownership out of the Application layer.
/// </remarks>
public class AzureBlobPrintSinkOptions
{
    public string BlobConnectionString { get; set; } = string.Empty;
    public string BlobContainerName { get; set; } = "expedition-lists";
}
