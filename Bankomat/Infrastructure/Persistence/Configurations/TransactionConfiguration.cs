using Bankomat.Domain.Transactions;
using Bankomat.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bankomat.Infrastructure.Persistence.Configurations;

public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> b)
    {
        b.ToTable("transactions");
        b.HasKey(x => x.Id);

        b.Property(x => x.AccountId).HasColumnName("account_id");
        b.Property(x => x.AtmId).HasColumnName("atm_id");
        b.Property(x => x.Type).HasColumnName("type").HasConversion<int>();

        b.Property(x => x.Amount).HasConversion(m => m.Amount, v => Money.Of(v))
            .HasColumnName("amount").HasColumnType("decimal(18,2)");

        b.Property(x => x.BalanceBefore).HasConversion(m => m.Amount, v => Money.Of(v))
            .HasColumnName("balance_before").HasColumnType("decimal(18,2)");

        b.Property(x => x.BalanceAfter).HasConversion(m => m.Amount, v => Money.Of(v))
            .HasColumnName("balance_after").HasColumnType("decimal(18,2)");

        b.Property(x => x.Description).HasColumnName("description").HasMaxLength(500);
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.CounterpartyCard).HasColumnName("counterparty_card").HasMaxLength(16);

        b.HasIndex(x => new { x.AccountId, x.CreatedAt });
    }
}