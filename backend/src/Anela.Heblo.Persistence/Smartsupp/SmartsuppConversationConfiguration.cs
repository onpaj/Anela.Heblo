using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Smartsupp;

public class SmartsuppConversationConfiguration : IEntityTypeConfiguration<SmartsuppConversation>
{
    public void Configure(EntityTypeBuilder<SmartsuppConversation> builder)
    {
        builder.ToTable("SmartsuppConversations", "public");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasMaxLength(100);
        builder.Property(e => e.Subject).HasMaxLength(500);
        builder.Property(e => e.ContactName).HasMaxLength(200);
        builder.Property(e => e.ContactEmail).HasMaxLength(200);
        builder.Property(e => e.ContactAvatarUrl).HasMaxLength(500);
        builder.Property(e => e.LastMessagePreview).HasMaxLength(500);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone");
        builder.Property(e => e.UpdatedAt).HasColumnType("timestamp without time zone");
        builder.Property(e => e.LastMessageAt).HasColumnType("timestamp without time zone");
        builder.Property(e => e.SyncedAt).HasColumnType("timestamp without time zone");
        builder.HasIndex(e => new { e.Status, e.LastMessageAt });
        builder.HasMany(e => e.Messages)
            .WithOne(e => e.Conversation)
            .HasForeignKey(e => e.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
