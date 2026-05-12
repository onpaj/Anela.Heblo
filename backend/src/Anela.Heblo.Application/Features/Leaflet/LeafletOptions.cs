using Anela.Heblo.Application.Shared.Rag;

namespace Anela.Heblo.Application.Features.Leaflet;

public class LeafletOptions : RagFeatureOptions
{
    public const string SectionName = "Leaflet";

    public LeafletOptions()
    {
        QueryExpansionPrompt =
            """
            Jsi asistent kosmetické firmy Anela. Zákazník zadal téma pro produktový leták.
            Přepiš téma do krátkého strukturovaného popisu vhodného pro sémantické
            vyhledávání v databázi znalostí a vzorových letáků.
            Vypiš POUZE relevantní položky (vynech kategorie bez obsahu):

            Produkt: <název nebo kategorie produktu>
            Kontext: <typ pleti, kategorie kosmetiky, použití>
            Klíčové ingredience: <pravděpodobné účinné látky a složky>
            Benefity: <očekávané přínosy a use-cases>
            Cílová skupina: <komu je produkt určen>

            Téma:
            """;
    }

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

    /// <summary>
    /// When true, each chunk is summarized by the LLM before embedding.
    /// Set to false to skip LLM calls (e.g. in tests or cost-free re-index runs).
    /// </summary>
    public bool SummarizationEnabled { get; set; } = true;

    /// <summary>
    /// System prompt prepended to each chunk when requesting a keyword summary.
    /// The chunk text is appended after a newline.
    /// </summary>
    public string SummarizationPrompt { get; set; } =
        """
        Jsi asistent extrahující klíčová data z úryvku marketingového letáku
        kosmetické firmy Anela. Extrahuj data vhodná pro sémantické vyhledávání.
        Vypiš POUZE relevantní položky v tomto formátu (vynech kategorie bez obsahu):

        Produkt: <název nebo kategorie produktu>
        Kontext: <typ pleti, kategorie kosmetiky, použití>
        Ingredience: <účinné látky, složky>
        Benefity: <přínos produktu, use-cases>
        Cílová skupina: <komu je produkt určen>

        Text:
        """;
}
