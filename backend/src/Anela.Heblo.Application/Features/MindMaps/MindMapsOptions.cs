using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.MindMaps;

public class MindMapsOptions
{
    public const string SectionName = "MindMaps";

    /// <summary>Replaces the Claude updater with a deterministic stub (E2E/staging).</summary>
    public bool UseStubUpdater { get; set; }

    [Range(1024, 64000)]
    public int UpdaterMaxOutputTokens { get; set; } = 16384;
}
