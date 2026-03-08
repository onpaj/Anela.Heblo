namespace Anela.Heblo.Application.Features.KnowledgeBase;

public class KnowledgeBaseOptions
{
    public string OneDriveInboxPath { get; set; } = "/KnowledgeBase/Inbox";
    public string OneDriveArchivedPath { get; set; } = "/KnowledgeBase/Archived";
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    public int EmbeddingDimensions { get; set; } = 1536;
    public int ChunkSize { get; set; } = 512;
    public int ChunkOverlapTokens { get; set; } = 50;
    public int MaxRetrievedChunks { get; set; } = 5;
    public string ClaudeModel { get; set; } = "claude-sonnet-4-6";
    public int ClaudeMaxTokens { get; set; } = 1024;

    /// <summary>
    /// UPN or object ID of the OneDrive user account used for ingestion (app-only access).
    /// Example: "service@anela.cz" or a GUID object ID.
    /// </summary>
    public string OneDriveUserId { get; set; } = string.Empty;
}
