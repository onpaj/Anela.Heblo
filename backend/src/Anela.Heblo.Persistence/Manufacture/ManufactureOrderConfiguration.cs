using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Manufacture;

public class ManufactureOrderConfiguration : IEntityTypeConfiguration<ManufactureOrder>
{
    public void Configure(EntityTypeBuilder<ManufactureOrder> builder)
    {
        builder.ToTable("ManufactureOrders", "public");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrderNumber)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.CreatedDate)
            .IsRequired()
            .AsUtcTimestamp();

        builder.Property(x => x.CreatedByUser)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.ResponsiblePerson)
            .HasMaxLength(100)
            .IsRequired(false);

        builder.Property(x => x.SemiProductPlannedDate)
            .IsRequired();

        builder.Property(x => x.ProductPlannedDate)
            .IsRequired();

        builder.Property(x => x.State)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(x => x.StateChangedAt)
            .IsRequired()
            .AsUtcTimestamp();

        builder.Property(x => x.StateChangedByUser)
            .HasMaxLength(100)
            .IsRequired();

        // Indexes for performance
        builder.HasIndex(x => x.OrderNumber)
            .IsUnique()
            .HasDatabaseName("IX_ManufactureOrders_OrderNumber");

        builder.HasIndex(x => x.State)
            .HasDatabaseName("IX_ManufactureOrders_State");

        builder.HasIndex(x => x.CreatedDate)
            .HasDatabaseName("IX_ManufactureOrders_CreatedDate");

        builder.HasIndex(x => x.ResponsiblePerson)
            .HasDatabaseName("IX_ManufactureOrders_ResponsiblePerson");

        // Navigation properties
        builder.HasMany(x => x.SemiProducts)
            .WithOne(x => x.ManufactureOrder)
            .HasForeignKey(x => x.ManufactureOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Products)
            .WithOne(x => x.ManufactureOrder)
            .HasForeignKey(x => x.ManufactureOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Notes)
            .WithOne(x => x.ManufactureOrder)
            .HasForeignKey(x => x.ManufactureOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.AuditLog)
            .WithOne(x => x.ManufactureOrder)
            .HasForeignKey(x => x.ManufactureOrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}