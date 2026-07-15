using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Catalog.Inventory;

public class PrinterMediaStateConfiguration : IEntityTypeConfiguration<PrinterMediaState>
{
    public void Configure(EntityTypeBuilder<PrinterMediaState> builder)
    {
        builder.ToTable("PrinterMediaStates", "public");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.LastMediaType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(64);

        builder.Property(e => e.LastPrintedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(e => e.LastPrintedBy).HasMaxLength(256);
    }
}
