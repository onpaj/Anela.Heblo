using Anela.Heblo.Application.Shared.Rag;

namespace Anela.Heblo.Application.Features.KnowledgeBase;

public class KnowledgeBaseOptions : RagFeatureOptions
{
    public const string SectionName = "KnowledgeBase";

    public KnowledgeBaseOptions()
    {
        QueryExpansionPrompt =
            """
            Jsi asistent kosmetické firmy Anela. Přepiš zákazníkův dotaz do strukturovaného
            formátu vhodného pro sémantické vyhledávání v databázi zákaznických konverzací.
            Vypiš POUZE relevantní položky (vynech kategorie bez obsahu):

            Problém: <co zákazník řeší, formuluj jako zákazníkův dotaz>
            Kontext: <typ pleti, stávající rutina, situace>
            Doporučení: <co by mohlo být doporučeno>
            Ingredience: <účinné látky, složky>

            Dotaz:
            """;
    }

    public int MaxRetrievedChunks { get; set; } = 5;

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
    /// How long (in minutes) the in-memory product lookup cache is considered valid
    /// before a fresh load from the catalog repository is triggered.
    /// </summary>
    public int ProductEnrichmentCacheTtlMinutes { get; set; } = 60;

    /// <summary>
    /// System prompt used by AskQuestionHandler. Supports {context}, {products} and {query} placeholders.
    /// {context} is replaced with retrieved chunks; {products} with the product table; {query} with the user's question.
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
        - Pokud zmiňuješ produkt Anela, uveď jeho produktový kódem
          v závorce (přesně takto: (AKL001)), nikdy ne jeho název. Použij pouze kódy z přiloženého seznamu produktů.

        Kontext z podobných konverzací:
        {context}

        Produkty Anela (CODE | Název):
        {products}

        Dotaz zákazníka:
        {query}
        """;
}
