namespace Anela.Heblo.Application.Features.Photobank.Configuration;

public sealed class PhotobankTagsCacheOptions
{
    public const string SectionName = "Photobank:TagsCache";
    public int TtlSeconds { get; init; } = 60;
}
