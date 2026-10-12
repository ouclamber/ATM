using Bankomat.Domain.Accounts;
using Bankomat.Domain.Atms;
using Bankomat.Domain.Transactions;
using Microsoft.EntityFrameworkCore;

namespace Bankomat.Infrastructure.Persistence;

public class BankomatDbContext : DbContext
{
    public DbSet<BankAccount> Accounts => Set<BankAccount>();
    public DbSet<Atm> Atms => Set<Atm>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    public BankomatDbContext(DbContextOptions<BankomatDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.ApplyConfigurationsFromAssembly(typeof(BankomatDbContext).Assembly);
    }
}