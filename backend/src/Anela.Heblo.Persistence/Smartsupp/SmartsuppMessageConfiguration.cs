using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Smartsupp;

public sealed class SmartsuppMessageConfiguration : IEntityTypeConfiguration<SmartsuppMessage>
{
    public void Configure(EntityTypeBuilder<SmartsuppMessage> builder)
    {
        builder.ToTable("SmartsuppMessages", "public");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasMaxLength(100);
        builder.Property(e => e.ConversationId).HasMaxLength(100);
        builder.Property(e => e.AuthorType).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.SubType).HasMaxLength(20);
        builder.Property(e => e.MessageType).HasMaxLength(20);
        builder.Property(e => e.AuthorName).HasMaxLength(200);
        builder.Property(e => e.TriggerName).HasMaxLength(200);
        builder.Property(e => e.TriggerId).HasMaxLength(100);
        builder.Property(e => e.AgentId).HasMaxLength(100);
        builder.Property(e => e.VisitorId).HasMaxLength(100);
        builder.Property(e => e.DeliveryStatus).HasMaxLength(50);
        builder.Property(e => e.Content).HasColumnType("text");
        builder.Property(e => e.PageUrl).HasColumnType("text");
        builder.Property(e => e.AttachmentsJson).HasColumnType("text");
        builder.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone");
        builder.Property(e => e.UpdatedAt).HasColumnType("timestamp without time zone");
        builder.Property(e => e.DeliveredAt).HasColumnType("timestamp without time zone");
        builder.HasIndex(e => new { e.ConversationId, e.CreatedAt });
        builder.HasIndex(e => new { e.ConversationId, e.SubType });
    }
}
