using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.Article;

public sealed class ArticleOptions
{
    public const string SectionName = "Articles";

    [Required, MinLength(1)]
    public string DefaultModel { get; set; } = "claude-sonnet-4-6";

    [Range(1, 16384)]
    public int WriteMaxTokens { get; set; } = 8192;

    [Range(1, 4096)]
    public int AggregateMaxTokens { get; set; } = 1024;

    [Range(1, 20)]
    public int WebSearchTopK { get; set; } = 5;

    [Range(1, 20)]
    public int KnowledgeBaseTopK { get; set; } = 8;

    [Required, MinLength(1)]
    public string QueryPlannerModel { get; set; } = "claude-haiku-4-5-20251001";

    [Required, MinLength(1)]
    public string AggregateFactsModel { get; set; } = "claude-sonnet-4-6";

    [Required, MinLength(1)]
    public string ValidateFactsModel { get; set; } = "claude-haiku-4-5-20251001";

    public string QueryPlannerSystemPrompt { get; set; } =
        """
        Jsi asistent kosmetické firmy Anela. Na základě zadaného tématu vygeneruj 6–8
        vyhledávacích dotazů pro průzkum tématu. Odpověz VÝHRADNĚ validním JSON bez markdown:
        {"queries":["dotaz1","dotaz2",...]}
        Téma:
        """;

    public string AggregateFactsSystemPrompt { get; set; } =
        """
        Jsi redakční asistent. Na základě úryvků z různých zdrojů shrň klíčová fakta pro artikel.
        Odpověz VÝHRADNĚ validním JSON bez markdown (žádné code fences):
        {"facts":[{"claim":"tvrzení","confidence":0.9,"source_url":"url nebo null","source_title":"název zdroje nebo null"}],"summary":"shrnutí","gaps":"chybějící informace"}
        Kontext:
        """;

    public string ValidateFactsSystemPrompt { get; set; } =
        """
        Jsi ověřovatel faktů. Projdi seznam faktů a ke každému přidej poznámku o spolehlivosti.
        Odpověz JSON: {"validated_facts":[{"fact":"...","note":"...","reliable":true}]}.
        Fakta:
        """;

    public string WriteArticleSystemPrompt { get; set; } =
        """
        Jsi zkušený redaktor kosmetického obsahu. Píšeš výhradně v češtině.
        Odpověz POUZE validním JSON bez markdown nebo code fences.
        V poli article_html použij výhradně HTML tagy – nikdy nepište doslovný text "\n" jako obsah.
        {"article_title":"...","article_html":"<article>...</article>","sources_used":[{"title":"...","url":"..."}]}
        """;

    public string WriteArticleUserPromptTemplate { get; set; } =
        """
        Napiš {length} článek v češtině.
        Téma: {topic}
        Publikum: {audience}
        Úhel: {angle}
        Rozsah: {scope}
        {tone_note_line}

        Fakta k využití:
        {facts}

        {style_guide}

        Požadavky:
        - Piš výhradně v češtině
        - Cituj zdroje přirozeně v textu
        - Vrať validní HTML pro e-mail (bez <html>/<body>)
        - Uváděj jen ty zdroje, které podporují konkrétní tvrzení
        """;
}
