using Anela.Heblo.Application.Shared.Rag;

namespace Anela.Heblo.Application.Features.Leaflet;

public class LeafletOptions : RagFeatureOptions
{
    public const string SectionName = "Leaflet";

    public int KbTopK { get; set; } = 8;
    public int LeafletTopK { get; set; } = 5;

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
