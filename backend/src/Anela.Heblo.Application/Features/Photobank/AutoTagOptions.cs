namespace Anela.Heblo.Application.Features.Photobank;

public sealed class AutoTagOptions
{
    public const string SectionName = "Photobank:AutoTag";

    public int BatchSize { get; init; } = 50;
    public int MaxPhotosPerRun { get; init; } = 5_000;
    public string Model { get; init; } = "claude-haiku-4-5-20251001";
    public int MaxTagsPerPhoto { get; init; } = 5;
}
