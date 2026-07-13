using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Anela.Heblo.Persistence.Smartsupp;

public sealed class SmartsuppPresenceRepository : ISmartsuppPresenceRepository
{
    private readonly ApplicationDbContext _db;

    public SmartsuppPresenceRepository(ApplicationDbContext db) => _db = db;

    public async Task UpsertAsync(
        SmartsuppConversationPresence presence,
        CancellationToken cancellationToken)
    {
        // Raw SQL: column list must match the EF mapping in SmartsuppConversationPresenceConfiguration.
        // Source is stored as string (HasConversion<string>()) so must be passed as .ToString().
        // EnteredAt is preserved on conflict; only LastSeenAt/DisplayName are refreshed.
        // Timestamps are passed as explicitly-typed NpgsqlDbType.Timestamp parameters: in raw
        // interpolated SQL Npgsql cannot see the column type and would default a bare DateTime to
        // 'timestamp with time zone', which rejects our Kind=Unspecified (UTC wall-clock) values.
        var source = presence.Source.ToString();
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO public.""SmartsuppConversationPresences"" (
                    ""ConversationId"", ""AgentId"", ""DisplayName"", ""Source"", ""EnteredAt"", ""LastSeenAt"")
                VALUES (
                    {presence.ConversationId}, {presence.AgentId}, {presence.DisplayName}, {source},
                    {Timestamp(presence.EnteredAt)}, {Timestamp(presence.LastSeenAt)})
                ON CONFLICT (""ConversationId"", ""AgentId"", ""Source"") DO UPDATE
                    SET ""DisplayName"" = EXCLUDED.""DisplayName"",
                        ""LastSeenAt""  = EXCLUDED.""LastSeenAt""",
            cancellationToken);
    }

    private static NpgsqlParameter Timestamp(DateTime value) =>
        new() { NpgsqlDbType = NpgsqlDbType.Timestamp, Value = value };

    public async Task RemoveAsync(
        string conversationId,
        string agentId,
        SmartsuppPresenceSource source,
        CancellationToken cancellationToken) =>
        await _db.SmartsuppConversationPresences
            .Where(p => p.ConversationId == conversationId && p.AgentId == agentId && p.Source == source)
            .ExecuteDeleteAsync(cancellationToken);

    public async Task<Dictionary<string, List<SmartsuppConversationPresence>>> GetActiveByConversationAsync(
        IReadOnlyCollection<string> conversationIds,
        DateTime hebloThreshold,
        DateTime smartsuppThreshold,
        CancellationToken cancellationToken)
    {
        if (conversationIds.Count == 0)
            return new Dictionary<string, List<SmartsuppConversationPresence>>();

        var ids = conversationIds.ToList();
        var rows = await _db.SmartsuppConversationPresences
            .AsNoTracking()
            .Where(p => ids.Contains(p.ConversationId)
                && ((p.Source == SmartsuppPresenceSource.Heblo && p.LastSeenAt >= hebloThreshold)
                    || (p.Source == SmartsuppPresenceSource.Smartsupp && p.LastSeenAt >= smartsuppThreshold)))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(p => p.ConversationId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    public async Task<int> PurgeExpiredAsync(
        DateTime hebloThreshold,
        DateTime smartsuppThreshold,
        CancellationToken cancellationToken) =>
        await _db.SmartsuppConversationPresences
            .Where(p => (p.Source == SmartsuppPresenceSource.Heblo && p.LastSeenAt < hebloThreshold)
                || (p.Source == SmartsuppPresenceSource.Smartsupp && p.LastSeenAt < smartsuppThreshold))
            .ExecuteDeleteAsync(cancellationToken);
}
