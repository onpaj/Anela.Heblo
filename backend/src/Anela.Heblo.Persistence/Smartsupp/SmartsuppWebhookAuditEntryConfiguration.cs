using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Smartsupp;

public sealed class SmartsuppWebhookAuditEntryConfiguration : IEntityTypeConfiguration<SmartsuppWebhookAuditEntry>
{
    public void Configure(EntityTypeBuilder<SmartsuppWebhookAuditEntry> builder)
    {
        builder.ToTable("SmartsuppWebhookAuditEntries", "public");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.EventName).HasMaxLength(100);
        builder.Property(e => e.AccountId).HasMaxLength(100);
        builder.Property(e => e.SignatureStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.SignatureHeader).HasMaxLength(200);
        builder.Property(e => e.ProcessingStatus).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.RawBody).HasColumnType("text");
        builder.Property(e => e.HeadersJson).HasColumnType("text");
        builder.Property(e => e.ProcessingError).HasColumnType("text");
        builder.Property(e => e.ReceivedAt).HasColumnType("timestamp without time zone");
        builder.HasIndex(e => e.ReceivedAt);
        builder.HasIndex(e => new { e.ProcessingStatus, e.ReceivedAt });
    }
}
