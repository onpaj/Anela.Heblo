namespace Anela.Heblo.Application.Features.ExpeditionListArchive;

public class ExpeditionListArchiveOptions
{
    public const string ConfigurationKey = "ExpeditionListArchive";

    public string BlobContainerName { get; set; } = "expedition-lists";
}
