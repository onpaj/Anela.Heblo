namespace Anela.Heblo.Domain.Features.BackgroundJobs;

/// <summary>
/// Functional grouping of recurring jobs, used to organise the administration UI.
/// Purely developer-owned metadata — never editable by an administrator, so it is
/// not persisted alongside <see cref="RecurringJobConfiguration"/>.
/// </summary>
public enum RecurringJobCategory
{
    /// <summary>
    /// Job exists in the database but no longer has a matching implementation in code.
    /// </summary>
    Uncategorized = 0,

    /// <summary>Invoice and payment imports, invoice classification.</summary>
    Finance = 1,

    /// <summary>Newsletter, campaign calendar and marketing performance data.</summary>
    Marketing = 2,

    /// <summary>Product catalogue, prices and consumption figures.</summary>
    Catalog = 3,

    /// <summary>Warehouse operations, picking and shipping.</summary>
    Warehouse = 4,

    /// <summary>Scheduled data quality tests.</summary>
    DataQuality = 5,

    /// <summary>Content ingestion and AI enrichment (knowledge base, leaflets, photobank).</summary>
    Content = 6,

    /// <summary>Attendance and working-hours processing.</summary>
    Attendance = 7,

    /// <summary>External system synchronisation and housekeeping.</summary>
    Integrations = 8
}
