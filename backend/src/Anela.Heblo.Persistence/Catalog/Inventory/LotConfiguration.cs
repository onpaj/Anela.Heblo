using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Catalog.Inventory;

public class LotConfiguration : IEntityTypeConfiguration<Lot>
{
    public void Configure(EntityTypeBuilder<Lot> builder)
    {
        builder.ToTable("Lots", "public");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.MaterialCode)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.LotCode)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Expiration)
            .HasColumnType("date");

        builder.Property(x => x.ReceivedDate)
            .IsRequired()
            .HasColumnType("date");

        builder.Property(x => x.Notes)
            .HasMaxLength(2000);

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp without time zone");

        builder.Property(x => x.CreatedBy)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.UpdatedAt)
            .HasColumnType("timestamp without time zone");

        builder.Property(x => x.UpdatedBy)
            .HasMaxLength(100);

        builder.HasIndex(x => new { x.MaterialCode, x.LotCode })
            .IsUnique()
            .HasDatabaseName("IX_Lots_MaterialCode_LotCode");

        builder.HasIndex(x => x.MaterialCode)
            .HasDatabaseName("IX_Lots_MaterialCode");
    }
}
