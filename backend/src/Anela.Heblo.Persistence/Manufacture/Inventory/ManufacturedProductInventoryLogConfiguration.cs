using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Manufacture.Inventory;

public class ManufacturedProductInventoryLogConfiguration : IEntityTypeConfiguration<ManufacturedProductInventoryLog>
{
    public void Configure(EntityTypeBuilder<ManufacturedProductInventoryLog> builder)
    {
        builder.ToTable("ManufacturedProductInventoryLogs", "public");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.ChangeType).HasConversion<int>().IsRequired();
        builder.Property(x => x.AmountDelta).HasColumnType("numeric(18,4)").IsRequired();
        builder.Property(x => x.AmountAfter).HasColumnType("numeric(18,4)").IsRequired();
        builder.Property(x => x.User).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ReferenceType).HasMaxLength(100);
        builder.Property(x => x.ReferenceId).HasMaxLength(100);
        builder.Property(x => x.Note).HasMaxLength(1000);
        builder.Property(x => x.Timestamp)
            .HasColumnType("timestamp without time zone").IsRequired();

        builder.HasIndex(x => x.InventoryItemId)
            .HasDatabaseName("IX_ManufacturedProductInventoryLogs_InventoryItemId");
    }
}
