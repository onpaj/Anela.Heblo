using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Manufacture;

public class ManufactureOrderNoteConfiguration : IEntityTypeConfiguration<ManufactureOrderNote>
{
    public void Configure(EntityTypeBuilder<ManufactureOrderNote> builder)
    {
        builder.ToTable("ManufactureOrderNotes", "public");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Text)
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .AsUtcTimestamp();

        builder.Property(x => x.CreatedByUser)
            .HasMaxLength(100)
            .IsRequired();

        // Indexes for performance
        builder.HasIndex(x => x.ManufactureOrderId)
            .HasDatabaseName("IX_ManufactureOrderNotes_ManufactureOrderId");

        builder.HasIndex(x => x.CreatedAt)
            .HasDatabaseName("IX_ManufactureOrderNotes_CreatedAt");
    }
}