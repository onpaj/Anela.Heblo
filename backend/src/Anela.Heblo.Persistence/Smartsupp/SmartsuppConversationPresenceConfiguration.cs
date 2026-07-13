using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Smartsupp;

public sealed class SmartsuppConversationPresenceConfiguration : IEntityTypeConfiguration<SmartsuppConversationPresence>
{
    public void Configure(EntityTypeBuilder<SmartsuppConversationPresence> builder)
    {
        builder.ToTable("SmartsuppConversationPresences", "public");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.ConversationId).HasMaxLength(100).IsRequired();
        builder.Property(e => e.AgentId).HasMaxLength(200).IsRequired();
        builder.Property(e => e.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Source).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.EnteredAt).HasColumnType("timestamp without time zone");
        builder.Property(e => e.LastSeenAt).HasColumnType("timestamp without time zone");

        // One row per operator per conversation per source; drives the ON CONFLICT upsert.
        builder.HasIndex(e => new { e.ConversationId, e.AgentId, e.Source }).IsUnique();
        builder.HasIndex(e => e.LastSeenAt);
    }
}
