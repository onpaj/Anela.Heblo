using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.Leaflet;

public class LeafletOptions
{
    public const string SectionName = "Leaflet";

    [Required]
    public string DriveId { get; set; } = string.Empty;

    [Required]
    public string InboxPath { get; set; } = "/Leaflets/Inbox";

    [Required]
    public string ArchivedPath { get; set; } = "/Leaflets/Archived";

    public int ChunkSizeWords { get; set; } = 800;
    public int ChunkOverlapWords { get; set; } = 80;

    public int KbTopK { get; set; } = 8;
    public int LeafletTopK { get; set; } = 5;
    public double MinSimilarityScore { get; set; } = 0.55;

    [Required]
    public string ChatModel { get; set; } = "claude-sonnet-4-6";

    public int ChatMaxTokens { get; set; } = 2048;

    [Required]
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";

    public string IngestionCronExpression { get; set; } = "*/15 * * * *";

    public string Stage1SystemPrompt { get; set; } =
        "You extract factual ingredient and benefit information for cosmetics leaflets. " +
        "Topic: {topic}. Audience: {audience}. Target length: {length}. " +
        "Below is information from the company knowledge base. Build a structured outline " +
        "(headings, bullet points) covering ingredients, claimed benefits, target use cases, " +
        "and regulatory cautions. Do not invent facts. Use only the provided context. " +
        "If the context is empty, return a minimal outline based on common cosmetic-industry " +
        "knowledge for that topic.\n\nKnowledge Base context:\n{kbContext}";

    public string Stage2SystemPrompt { get; set; } =
        "You are a Czech marketing copywriter for Anela cosmetics. Rewrite the provided " +
        "factual outline into a polished marketing leaflet. Audience: {audience}. " +
        "Target length: {length} words. Output Czech-language Markdown only. " +
        "Use the leaflet excerpts below as a tone/style reference — match their voice, " +
        "register, and rhythm.\nIs cold start (no leaflet examples available): {coldStart}. " +
        "If cold start is true, use a neutral professional marketing register.\n\n" +
        "Leaflet style references:\n{leafletContext}";

    public int ShortWordTarget { get; set; } = 200;
    public int MediumWordTarget { get; set; } = 400;
    public int LongWordTarget { get; set; } = 700;
}
