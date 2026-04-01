using Anela.Heblo.Domain.Features.KnowledgeBase;

namespace Anela.Heblo.Application.Features.KnowledgeBase;

public class OneDriveFolderMapping
{
    public string InboxPath { get; set; } = string.Empty;
    public string ArchivedPath { get; set; } = string.Empty;
    public DocumentType DocumentType { get; set; } = DocumentType.KnowledgeBase;
}
