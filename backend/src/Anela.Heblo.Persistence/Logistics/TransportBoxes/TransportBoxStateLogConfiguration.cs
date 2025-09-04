using Anela.Heblo.Domain.Features.Logistics.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Logistics.TransportBoxes;

public class TransportBoxStateLogConfiguration : IEntityTypeConfiguration<TransportBoxStateLog>
{
    public void Configure(EntityTypeBuilder<TransportBoxStateLog> builder)
    {
        builder.ToTable("TransportBoxStateLog", "public");

        builder.HasKey(x => x.Id);

        // Configure StateDate for PostgreSQL backward compatibility
        builder.Property(x => x.StateDate)
            .HasColumnName("StateDate")
            .HasColumnType("timestamp without time zone")
            .IsRequired();
    }
}