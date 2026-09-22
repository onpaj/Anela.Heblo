namespace Anela.Heblo.Domain.Features.Ecomail;

/// <summary>
/// What an automation's cumulative counters read on a given day. Append-only: never recomputed,
/// never locked, because this is an observation rather than a derived aggregate.
///
/// This table is the entire reason the ingest exists. Ecomail exposes only lifetime totals for an
/// automation, and its date-filter parameters on /pipelines/{id}/stats are a no-op
/// (CLUSTER-B-FINDINGS.md §8.3). Monthly conversions are therefore the delta between two rows here,
/// and a day not captured is a hole that can never be backfilled.
/// </summary>
public class EcomailAutomationSnapshot
{
    public int Id { get; set; }

    public int PipelineId { get; set; }

    /// <summary>Date the counters were read, in Europe/Prague. Maps to a Postgres `date`.</summary>
    public DateOnly CapturedOn { get; set; }

    public int Triggered { get; set; }
    public int Ended { get; set; }
    public int Send { get; set; }
    public int Open { get; set; }
    public int Click { get; set; }
    public int Unsub { get; set; }
    public int Bounce { get; set; }
    public int Conversions { get; set; }
    public decimal ConversionsValue { get; set; }
}
