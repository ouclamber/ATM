namespace Bankomat.Domain.Transactions;

public interface ITransactionRepository
{
    Task AddAsync(Transaction transaction, CancellationToken ct = default);

    /// <summary>
    /// Финансовые операции счёта за период (без служебных записей о входе), новые сверху.
    /// Возвращает объекты только для чтения.
    /// </summary>
    Task<IReadOnlyList<Transaction>> GetByAccountAsync(
        int accountId, int days, int limit = 50, CancellationToken ct = default);

    Task<decimal> GetWithdrawSumLast24hAsync(int accountId, CancellationToken ct = default);
}
