using Anela.Heblo.Domain.Features.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Catalog.ProductIngredientOrder;

public class ProductIngredientOrderConfiguration : IEntityTypeConfiguration<Domain.Features.Catalog.ProductIngredientOrder>
{
    public void Configure(EntityTypeBuilder<Domain.Features.Catalog.ProductIngredientOrder> builder)
    {
        builder.ToTable("ProductIngredientOrders", "public");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ParentProductCode)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.IngredientProductCode)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.SortOrder)
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .IsRequired()
            .HasColumnType("timestamp without time zone");

        builder.Property(x => x.UpdatedBy)
            .HasMaxLength(100);

        builder.HasIndex(x => x.ParentProductCode)
            .HasDatabaseName("IX_ProductIngredientOrders_ParentProductCode");

        builder.HasIndex(x => new { x.ParentProductCode, x.IngredientProductCode })
            .IsUnique()
            .HasDatabaseName("UX_ProductIngredientOrders_Parent_Ingredient");
    }
}
