namespace Anela.Heblo.Domain.Features.Rag;

public enum RagTagKind
{
    Problem = 0,
    Product = 1,
    RecommendedProduct = 2,
}

public sealed record RagTagCount(RagTagKind Kind, string Tag, int Count);
