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

    public Task<BankAccount?> GetByCardNumberAsync(CardNumber cardNumber, CancellationToken ct = default) =>
        _db.Accounts.FirstOrDefaultAsync(a => a.CardNumber == cardNumber, ct);

    public async Task<HashSet<string>> GetExistingCardNumbersAsync(
        IReadOnlyCollection<CardNumber> cardNumbers, CancellationToken ct = default)
    {
        if (cardNumbers.Count == 0)
            return new HashSet<string>();

        var found = await _db.Accounts
            .AsNoTracking()
            .Where(a => cardNumbers.Contains(a.CardNumber))
            .Select(a => a.CardNumber)
            .ToListAsync(ct);

        return found.Select(c => c.Value).ToHashSet();
    }

    public async Task AddAsync(BankAccount account, CancellationToken ct = default) =>
        await _db.Accounts.AddAsync(account, ct);
}
