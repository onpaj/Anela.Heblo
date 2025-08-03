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