using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Catalog.Inventory;

public class MaterialContainerConfiguration : IEntityTypeConfiguration<MaterialContainer>
{
    public void Configure(EntityTypeBuilder<MaterialContainer> builder)
    {
        builder.ToTable("MaterialContainers", "public");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(x => x.LotId)
            .IsRequired();

        builder.Property(x => x.Amount)
            .IsRequired()
            .HasPrecision(18, 4);

        builder.Property(x => x.Unit)
            .IsRequired()
            .HasMaxLength(20);

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

        builder.Property(x => x.Status).IsRequired().HasConversion<int>();

        builder.HasIndex(x => x.Code)
            .IsUnique()
            .HasDatabaseName("IX_MaterialContainers_Code");

        builder.HasIndex(x => x.LotId)
            .HasDatabaseName("IX_MaterialContainers_LotId");

        // ON DELETE RESTRICT: deleting Lot while MaterialContainers exist raises an error
        builder.HasOne<Lot>()
            .WithMany()
            .HasForeignKey(x => x.LotId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_MaterialContainers_Lots_LotId");
    }
}
