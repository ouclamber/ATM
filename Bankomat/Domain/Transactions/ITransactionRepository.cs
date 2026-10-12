namespace Bankomat.Domain.Transactions;

public interface ITransactionRepository
{
    Task AddAsync(Transaction transaction, CancellationToken ct = default);
    Task<IReadOnlyList<Transaction>> GetByAccountAsync(int accountId, int days, CancellationToken ct = default);
    Task<decimal> GetWithdrawSumLast24hAsync(int accountId, CancellationToken ct = default);
}