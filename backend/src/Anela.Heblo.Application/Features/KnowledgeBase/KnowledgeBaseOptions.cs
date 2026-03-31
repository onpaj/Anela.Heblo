namespace Anela.Heblo.Application.Features.KnowledgeBase;

public class KnowledgeBaseOptions
{
    public string OneDriveInboxPath { get; set; } = "/KnowledgeBase/Inbox";
    public string OneDriveArchivedPath { get; set; } = "/KnowledgeBase/Archived";
    public int ChunkSize { get; set; } = 512;
    public int ChunkOverlapTokens { get; set; } = 50;
    public int MaxRetrievedChunks { get; set; } = 5;

    /// <summary>
    /// UPN or object ID of the OneDrive user account used for ingestion (app-only access).
    /// Example: "service@anela.cz" or a GUID object ID.
    /// </summary>
    public string OneDriveUserId { get; set; } = string.Empty;

    /// <summary>
    /// Regex patterns stripped from documents before chunking.
    /// Patterns are compiled with Singleline + Multiline flags.
    /// Defaults cover the standard Anela chat transcript boilerplate.
    /// </summary>
    public List<string> PreprocessorPatterns { get; set; } =
    [
        @"Vítejte ve světě Anela.*?Napište nám, jsme tu pro Vás!",
        @"(?m)^datum:.*?\nzákazník:\s+[^\n]+",
        @"Zákazník-\d+:?\s*",
        @"Anela: Zrovna odpočíváme a nabíráme síly na další den\. Napište nám a my se Vám ozveme hned, jak to bude možné\. Děkujeme a těšíme se! 🤩"
    ];

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
        Jsi asistent extrahující klíčová data z úryvku zákaznického chatu kosmetické firmy Anela.
        Z textu vypiš POUZE relevantní položky v tomto formátu (vynech kategorie bez obsahu):

        Produkty: <názvy produktů>
        Ingredience: <účinné látky, složky>
        Problém zákazníka: <kožní potíže, dotazy>
        Doporučení: <rady, způsob použití>

        Text:
        """;

    /// <summary>
    /// Prompt used by ConversationTopicSummarizer. Instructs the LLM to segment
    /// the full transcript by topic and return keyword blocks separated by TopicDelimiter.
    /// The full transcript text is appended after a newline.
    /// </summary>
    public string TopicSummarizationPrompt { get; set; } =
        """
        Jsi asistent analyzující zákaznický chat kosmetické firmy Anela.
        Rozděl konverzaci do tematických bloků. Pro každý blok vypiš klíčová data.
        Každý blok začni značkou [TOPIC] na samostatném řádku (vynech kategorie bez obsahu):

        [TOPIC]
        Produkty: <názvy produktů>
        Ingredience: <účinné látky, složky>
        Problém zákazníka: <kožní potíže, dotazy>
        Doporučení: <rady, způsob použití>

        Konverzace:
        """;

    /// <summary>
    /// Delimiter used to split the LLM response into individual topic summaries.
    /// </summary>
    public string TopicDelimiter { get; set; } = "[TOPIC]";
}
