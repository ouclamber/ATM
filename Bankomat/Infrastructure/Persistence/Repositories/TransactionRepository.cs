using Bankomat.Domain.Transactions;
using Microsoft.EntityFrameworkCore;

namespace Bankomat.Infrastructure.Persistence.Repositories;

public class TransactionRepository : ITransactionRepository
{
    private readonly BankomatDbContext _db;
    public TransactionRepository(BankomatDbContext db) => _db = db;

    public async Task AddAsync(Transaction transaction, CancellationToken ct = default) =>
        await _db.Transactions.AddAsync(transaction, ct);

    public async Task<IReadOnlyList<Transaction>> GetByAccountAsync(
        int accountId, int days, int limit = 50, CancellationToken ct = default)
    {
        var from = DateTime.UtcNow.AddDays(-days);
        return await _db.Transactions
            .AsNoTracking()
            .Where(t => t.AccountId == accountId
                        && t.CreatedAt >= from
                        && t.Type != TransactionType.Login)
            .OrderByDescending(t => t.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<decimal> GetWithdrawSumLast24hAsync(int accountId, CancellationToken ct = default)
    {
        var from = DateTime.UtcNow.AddHours(-24);

        // Money хранится через value converter, поэтому SUM по нему EF не транслирует в SQL.
        // Выборка узкая (одна колонка, индекс account_id+created_at), строк за сутки — единицы.
        var amounts = await _db.Transactions
            .AsNoTracking()
            .Where(t => t.AccountId == accountId
                        && t.Type == TransactionType.Withdraw
                        && t.CreatedAt >= from)
            .Select(t => t.Amount)
            .ToListAsync(ct);

        return amounts.Sum(m => m.Amount);
    }
}
