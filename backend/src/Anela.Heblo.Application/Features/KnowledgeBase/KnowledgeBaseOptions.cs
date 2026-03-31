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
        Jsi asistent extrahující klíčová data z úryvku zákaznického chatu 
        kosmetické firmy Anela. Extrahuj data vhodná pro sémantické vyhledávání.
        Vypiš POUZE relevantní položky v tomto formátu (vynech kategorie bez obsahu):
        
        Problém: <co zákazník řeší, formuluj jako zákazníkův dotaz>
        Kontext: <typ pleti, stávající rutina, situace>
        Doporučení: <co bylo doporučeno a proč>
        Produkty: <názvy produktů>
        Ingredience: <účinné látky, složky>
        Výsledek: <vyřešeno | nevyřešeno | eskalováno>
        
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
        Rozděl konverzaci do tematických bloků. Pro každý blok vypiš klíčová data
        vhodná pro sémantické vyhledávání.
        Odpověz POUZE bloky níže – žádný nadpis, žádný úvod, žádný závěr.
        Každý blok začni PŘESNĚ značkou [TOPIC] na samostatném řádku (vynech kategorie bez obsahu):
        
        [TOPIC]
        Problém: <co zákazník řeší, formuluj jako zákazníkův dotaz>
        Kontext: <typ pleti, stávající rutina, situace>
        Doporučení: <co bylo doporučeno a proč>
        Produkty: <názvy produktů>
        Ingredience: <účinné látky, složky>
        Výsledek: <vyřešeno | nevyřešeno | eskalováno>
        
        Konverzace:
        """;

    /// <summary>
    /// Delimiter used to split the LLM response into individual topic summaries.
    /// </summary>
    public string TopicDelimiter { get; set; } = "[TOPIC]";

    /// <summary>
    /// System prompt used by AskQuestionHandler. Supports {context} and {query} placeholders.
    /// {context} is replaced with retrieved chunks; {query} is replaced with the user's question.
    /// </summary>
    public string AskQuestionSystemPrompt { get; set; } =
        """
        Jsi odborná poradkyně kosmetické firmy Anela. Odpovídáš zákazníkům
        na dotazy o péči o pleť a produktech Anela.

        Odpovídej výhradně na základě poskytnutého kontextu z předchozích
        konverzací. Pokud kontext neobsahuje relevantní informaci, řekni to
        přímo – nevymýšlej doporučení.

        Při odpovědi:
        - Doporučuj konkrétní produkty Anela, pokud jsou v kontextu zmíněny
        - Zohledni typ pleti a potíže zákazníka
        - Odpovídej v češtině, přátelsky ale odborně
        - Pokud kontext obsahuje více podobných případů, syntetizuj je

        Kontext z podobných konverzací:
        {context}

        Dotaz zákazníka:
        {query}
        """;
}
