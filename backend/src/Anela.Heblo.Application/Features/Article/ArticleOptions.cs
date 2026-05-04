using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.Article;

public class ArticleOptions
{
    public const string SectionName = "Articles";

    [Required]
    public string DefaultModel { get; set; } = "claude-sonnet-4-6";

    public int WriteMaxTokens { get; set; } = 4096;

    public int AggregateMaxTokens { get; set; } = 1024;

    [Range(1, 20)]
    public int WebSearchTopK { get; set; } = 5;

    [Range(1, 20)]
    public int KnowledgeBaseTopK { get; set; } = 8;

    [Required]
    public string QueryPlannerModel { get; set; } = "claude-haiku-4-5-20251001";

    [Required]
    public string AggregateFactsModel { get; set; } = "claude-sonnet-4-6";

    [Required]
    public string ValidateFactsModel { get; set; } = "claude-haiku-4-5-20251001";

    public string QueryPlannerSystemPrompt { get; set; } =
        """
        Jsi asistent kosmetické firmy Anela. Na základě zadaného tématu vygeneruj 6–8
        vyhledávacích dotazů pro průzkum tématu. Odpověz výhradně JSON polem řetězců: ["dotaz1","dotaz2",...].
        Téma:
        """;

    public string AggregateFactsSystemPrompt { get; set; } =
        """
        Jsi redakční asistent. Na základě úryvků z různých zdrojů shrň klíčová fakta pro artikel.
        Odpověz JSON objektem: {"facts":["fakt1","fakt2"],"sources_used":["url nebo název zdroje"]}.
        Kontext:
        """;

    public string ValidateFactsSystemPrompt { get; set; } =
        """
        Jsi ověřovatel faktů. Projdi seznam faktů a ke každému přidej poznámku o spolehlivosti.
        Odpověz JSON: {"validated_facts":[{"fact":"...","note":"...","reliable":true}]}.
        Fakta:
        """;

    public string WriteArticleSystemPromptTemplate { get; set; } =
        """
        Jsi zkušený redaktor kosmetického obsahu. Napiš článek na téma {topic} pro publikum {audience}.
        Délka: {length}. Úhel pohledu: {angle}.
        Využij tato fakta: {facts}
        {style_guide}
        Odpověz JSON: {"title":"...","html_content":"<article>...</article>"}.
        """;
}
