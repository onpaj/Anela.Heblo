using Anela.Heblo.Domain.Features.Logistics.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Configurations.TransportBoxes;

public class TransportBoxConfiguration : IEntityTypeConfiguration<TransportBox>
{
    public void Configure(EntityTypeBuilder<TransportBox> builder)
    {
        builder.ToTable("TransportBox", "dbo");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.State)
            .HasConversion<string>();

        builder.Property(x => x.DefaultReceiveState)
            .HasConversion<string>();

        // Configure ConcurrencyStamp for PostgreSQL backward compatibility
        builder.Property(x => x.ConcurrencyStamp)
            .HasColumnName("ConcurrencyStamp")
            .HasMaxLength(40)
            .HasColumnType("character varying(40)");

        // Configure ExtraProperties for PostgreSQL backward compatibility
        builder.Property(x => x.ExtraProperties)
            .HasColumnName("ExtraProperties")
            .HasColumnType("text");

        // Configure CreationTime for PostgreSQL backward compatibility
        builder.Property(x => x.CreationTime)
            .HasColumnName("CreationTime")
            .HasColumnType("timestamp without time zone")
            .IsRequired();

        // Configure LastModificationTime for PostgreSQL backward compatibility
        builder.Property(x => x.LastModificationTime)
            .HasColumnName("LastModificationTime")
            .HasColumnType("timestamp without time zone");

        builder.HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey("TransportBoxId")
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.StateLog)
            .WithOne()
            .HasForeignKey("TransportBoxId")
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);
    }
}