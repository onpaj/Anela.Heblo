namespace Anela.Heblo.Domain.Features.Ecomail;

/// <summary>
/// Per-automation, per-month event sums, read from /pipelines/{id}/stats-detail date windows —
/// the one filter Ecomail honours. Retroactively backfillable, so unlike the snapshot table this
/// one is recomputable. Sums only; rates are derived at read time.
///
/// Carries no conversions column on purpose: stats-detail has no conversion event
/// (CLUSTER-B-FINDINGS.md §8.5). Monthly conversions come from snapshot deltas instead.
/// </summary>
public class EcomailAutomationMonth
{
    public int Id { get; set; }

    public int PipelineId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }

    public int Send { get; set; }
    public int Open { get; set; }
    public int Click { get; set; }
    public int Unsub { get; set; }

    public DateTime ComputedAt { get; set; }

    /// <summary>True once the month has left the recompute window; only an explicit recompute touches it.</summary>
    public bool IsLocked { get; set; }

    public string? LastError { get; set; }
}
