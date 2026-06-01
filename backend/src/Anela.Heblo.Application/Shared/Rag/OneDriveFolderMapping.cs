using Anela.Heblo.Domain.Shared.Rag;

namespace Anela.Heblo.Application.Shared.Rag;

public class OneDriveFolderMapping
{
    public string InboxPath { get; set; } = string.Empty;
    public string ArchivedPath { get; set; } = string.Empty;
    public DocumentType DocumentType { get; set; } = DocumentType.KnowledgeBase;

    /// <summary>
    /// SharePoint drive ID. Find it via Graph API:
    /// GET /v1.0/sites/{hostname}:/sites/{site-name} → get siteId
    /// GET /v1.0/sites/{siteId}/drives → find drive by name, copy "id"
    /// </summary>
    public string DriveId { get; set; } = string.Empty;
}
