using Anela.Heblo.Domain.Features.Purchase;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Configurations.PurchaseOrders;

public class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable("PurchaseOrders", "dbo");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.OrderNumber)
            .IsRequired()
            .HasMaxLength(PurchaseOrderConstants.OrderNumberMaxLength);

        builder.HasIndex(x => x.OrderNumber)
            .IsUnique();

        builder.Property(x => x.SupplierName)
            .IsRequired()
            .HasMaxLength(PurchaseOrderConstants.SupplierNameMaxLength);

        builder.Property(x => x.OrderDate)
            .IsRequired();

        builder.Property(x => x.ExpectedDeliveryDate);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(x => x.Notes)
            .HasMaxLength(PurchaseOrderConstants.NotesMaxLength);

        builder.Property(x => x.CreatedBy)
            .IsRequired()
            .HasMaxLength(PurchaseOrderConstants.UserNameMaxLength);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.UpdatedBy)
            .HasMaxLength(PurchaseOrderConstants.UserNameMaxLength);

        builder.Property(x => x.UpdatedAt);

        builder.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey(x => x.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.History)
            .WithOne()
            .HasForeignKey(x => x.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Configure backing fields for collections
        builder.Navigation(x => x.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Navigation(x => x.History)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Navigation(x => x.Lines)
            .EnableLazyLoading(false);

        builder.Navigation(x => x.History)
            .EnableLazyLoading(false);
    }
}