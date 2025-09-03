using Anela.Heblo.Domain.Features.Logistics.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Configurations.TransportBoxes;

public class TransportBoxItemConfiguration : IEntityTypeConfiguration<TransportBoxItem>
{
    public void Configure(EntityTypeBuilder<TransportBoxItem> builder)
    {
        builder.ToTable("TransportBoxItem", "public");

        builder.HasKey(x => x.Id);
        
        // Configure DateAdded for PostgreSQL backward compatibility
        builder.Property(x => x.DateAdded)
            .HasColumnName("DateAdded")
            .HasColumnType("timestamp without time zone")
            .IsRequired();
    }
}