using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Smartsupp;

public sealed class SmartsuppContactConfiguration : IEntityTypeConfiguration<SmartsuppContact>
{
    public void Configure(EntityTypeBuilder<SmartsuppContact> builder)
    {
        builder.ToTable("SmartsuppContacts", "public");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasMaxLength(100);
        builder.Property(e => e.Email).HasMaxLength(200);
        builder.Property(e => e.Name).HasMaxLength(200);
        builder.Property(e => e.Phone).HasMaxLength(50);
        builder.Property(e => e.BannedBy).HasMaxLength(200);
        builder.Property(e => e.Note).HasColumnType("text");
        builder.Property(e => e.TagsJson).HasColumnType("text");
        builder.Property(e => e.PropertiesJson).HasColumnType("text");
        builder.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone");
        builder.Property(e => e.UpdatedAt).HasColumnType("timestamp without time zone");
        builder.Property(e => e.SyncedAt).HasColumnType("timestamp without time zone");
        builder.Property(e => e.BannedAt).HasColumnType("timestamp without time zone");
        builder.HasIndex(e => e.Email);
    }
}
