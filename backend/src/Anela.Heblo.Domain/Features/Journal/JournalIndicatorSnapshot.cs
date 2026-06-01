using System;

namespace Anela.Heblo.Domain.Features.Journal
{
    /// <summary>
    /// Repository-level read-model projection of per-product journal counts and last-entry metadata.
    /// Returned by <see cref="IJournalRepository.GetJournalIndicatorsAsync"/> keyed by product code.
    /// Not a domain entity — has no identity, behavior, or lifecycle.
    /// </summary>
    public readonly record struct JournalIndicatorSnapshot(
        int DirectEntries,
        DateTime? LastEntryDate,
        bool HasRecentEntries);
}
