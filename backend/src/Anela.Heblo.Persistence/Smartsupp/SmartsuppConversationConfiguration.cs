using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Smartsupp;

public sealed class SmartsuppConversationConfiguration : IEntityTypeConfiguration<SmartsuppConversation>
{
    public void Configure(EntityTypeBuilder<SmartsuppConversation> builder)
    {
        builder.ToTable("SmartsuppConversations", "public");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasMaxLength(100);
        builder.Property(e => e.ExtId).HasMaxLength(100);
        builder.Property(e => e.Subject).HasMaxLength(500);
        builder.Property(e => e.ContactId).HasMaxLength(100);
        builder.Property(e => e.ContactName).HasMaxLength(200);
        builder.Property(e => e.ContactEmail).HasMaxLength(200);
        builder.Property(e => e.ContactAvatarUrl).HasMaxLength(500);
        builder.Property(e => e.VisitorId).HasMaxLength(100);
        builder.Property(e => e.Domain).HasMaxLength(200);
        builder.Property(e => e.Referer).HasMaxLength(500);
        builder.Property(e => e.LocationCountry).HasMaxLength(100);
        builder.Property(e => e.LocationCity).HasMaxLength(100);
        builder.Property(e => e.LocationIp).HasMaxLength(50);
        builder.Property(e => e.LocationCode).HasMaxLength(10);
        builder.Property(e => e.LastMessagePreview).HasMaxLength(500);
        builder.Property(e => e.VariablesJson).HasColumnType("text");
        builder.Property(e => e.TagsJson).HasColumnType("text");
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone");
        builder.Property(e => e.UpdatedAt).HasColumnType("timestamp without time zone");
        builder.Property(e => e.FinishedAt).HasColumnType("timestamp without time zone");
        builder.Property(e => e.LastMessageAt).HasColumnType("timestamp without time zone");
        builder.Property(e => e.SyncedAt).HasColumnType("timestamp without time zone");
        builder.HasIndex(e => new { e.Status, e.LastMessageAt });
        builder.HasIndex(e => e.ContactId);
        builder.HasOne(e => e.Contact)
            .WithMany()
            .HasForeignKey(e => e.ContactId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);
        builder.HasMany(e => e.Messages)
            .WithOne(e => e.Conversation)
            .HasForeignKey(e => e.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Property(e => e.Rating);
        builder.Property(e => e.RatingText).HasMaxLength(1000);
        builder.Property(e => e.CloseType).HasMaxLength(50);
        builder.Property(e => e.ClosedByAgentId).HasMaxLength(100);
        builder.Property(e => e.AssignedAgentIdsJson).HasColumnType("text");
        builder.Property(e => e.Channel).HasMaxLength(50);
        builder.Property(e => e.LastClosedAt).HasColumnType("timestamp without time zone");
        builder.Property(e => e.VisitorUserAgent).HasColumnType("text");
        builder.Property(e => e.VisitorOs).HasMaxLength(100);
        builder.Property(e => e.VisitorBrowser).HasMaxLength(100);
        builder.Property(e => e.VisitorBrowserVersion).HasMaxLength(100);
        builder.Property(e => e.VisitorVisitsCount);
        builder.Property(e => e.VisitorInfoFetchedAt).HasColumnType("timestamp without time zone");
    }
}
