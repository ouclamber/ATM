using Bankomat.Domain.Accounts;
using Bankomat.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Bankomat.Infrastructure.Persistence.Repositories;

public class BankAccountRepository : IBankAccountRepository
{
    private readonly BankomatDbContext _db;
    public BankAccountRepository(BankomatDbContext db) => _db = db;

    public Task<BankAccount?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _db.Accounts.FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<BankAccount?> GetByCardNumberAsync(CardNumber cardNumber, CancellationToken ct = default)
    {
        return _db.Accounts.FirstOrDefaultAsync(a => a.CardNumber == cardNumber, ct);
    }

    public async Task AddAsync(BankAccount account, CancellationToken ct = default) =>
        await _db.Accounts.AddAsync(account, ct);
}