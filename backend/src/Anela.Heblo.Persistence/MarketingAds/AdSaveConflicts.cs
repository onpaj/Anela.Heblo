using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anela.Heblo.Persistence.MarketingAds;

/// <summary>
/// Tells a lost race (concurrency token, or two writers inserting the same unique key) apart from any other failed
/// update. Only a lost race may be reported as MarketingAdsConcurrentChange; anything else is a real failure.
/// </summary>
internal static class AdSaveConflicts
{
    public static bool IsLostRace(DbUpdateException ex) =>
        ex is DbUpdateConcurrencyException
        || ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
