using Bankomat.Domain.Accounts;
using Bankomat.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bankomat.Infrastructure.Persistence.Configurations;

public class BankAccountConfiguration : IEntityTypeConfiguration<BankAccount>
{
    public void Configure(EntityTypeBuilder<BankAccount> b)
    {
        b.ToTable("bank_accounts");
        b.HasKey(x => x.Id);

        // CardNumber VO → string column
        b.Property(x => x.CardNumber)
            .HasConversion(
                vo => vo.Value,
                str => CardNumber.Create(str).Value)
            .HasColumnName("card_number")
            .HasMaxLength(16)
            .IsRequired();
        b.HasIndex(x => x.CardNumber).IsUnique();

        // Money VO
        b.Property(x => x.Balance)
            .HasConversion(
                m => m.Amount,
                v => Money.Of(v))
            .HasColumnName("balance")
            .HasColumnType("decimal(18,2)");

        b.Property(x => x.PinHash).HasColumnName("pin_hash").HasMaxLength(100);
        b.Property(x => x.HolderName).HasColumnName("holder_name").HasMaxLength(100);
        b.Property(x => x.IsBlocked).HasColumnName("is_blocked");
        b.Property(x => x.FailedLoginAttempts).HasColumnName("failed_login_attempts");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.LastAccessAt).HasColumnName("last_access_at");

        // Оптимистическая блокировка через системную колонку PostgreSQL xmin
        // (меняется при каждом UPDATE строки, физически колонку создавать не нужно).
        // Защищает от потерянного обновления баланса при параллельных операциях.
        b.Property<uint>("xmin").IsRowVersion();
    }
}