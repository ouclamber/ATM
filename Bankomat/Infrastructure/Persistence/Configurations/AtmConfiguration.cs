using Bankomat.Domain.Atms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bankomat.Infrastructure.Persistence.Configurations;

public class AtmConfiguration : IEntityTypeConfiguration<Atm>
{
    public void Configure(EntityTypeBuilder<Atm> b)
    {
        b.ToTable("atms");
        b.HasKey(x => x.Id);

        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        b.Property(x => x.Location).HasColumnName("location").HasMaxLength(200).IsRequired();
        b.Property(x => x.IsActive).HasColumnName("is_active");
        b.Property(x => x.LastMaintenanceAt).HasColumnName("last_maintenance_at");

        b.OwnsMany(x => x.Cassettes, cb =>
        {
            cb.ToTable("cassettes");
            cb.HasKey(c => c.Id);

            cb.WithOwner()
              .HasForeignKey(c => c.AtmId)
              .HasConstraintName("fk_cassettes_atms");

            cb.Property(c => c.Id).HasColumnName("id").ValueGeneratedOnAdd();
            cb.Property(c => c.AtmId).HasColumnName("atm_id").IsRequired();
            cb.Property(c => c.Denomination).HasColumnName("denomination").IsRequired();
            cb.Property(c => c.Count).HasColumnName("count").IsRequired();
            cb.Property(c => c.MaxCapacity).HasColumnName("max_capacity").IsRequired();

            cb.HasIndex(c => new { c.AtmId, c.Denomination })
              .IsUnique()
              .HasDatabaseName("ix_cassettes_atm_denomination");
        });
    }
}